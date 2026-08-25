using System.Text;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Deadlimit.Core;

public sealed record HeroCatalogEntry(
    string DisplayName,
    string LookupName,
    string ModelResourcePath)
{
    public override string ToString() => DisplayName;
}

public sealed class HeroCatalogSnapshot
{
    public DateTimeOffset RefreshedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SourceVpkPath { get; set; } = string.Empty;
    public List<HeroCatalogEntry> Heroes { get; set; } = [];
}

public sealed class HeroCatalogService
{
    private const string HeroesResourcePath = "scripts/heroes.vdata_c";
    private const string HeroNamesLocalizationPath =
        "resource/localization/citadel_gc_hero_names/citadel_gc_hero_names_english.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex TopLevelBlockRegex = new(
        @"(?m)^\t(?<key>[A-Za-z0-9_]+)\s*=\s*\r?\n\t\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LocalizationRegex = new(
        "^\\s*\"(?<key>[^\"]+)\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly DeadlimitPaths _paths;

    public HeroCatalogService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public static IReadOnlyList<HeroCatalogEntry> LoadCached()
    {
        var cachePath = GetCachePath();
        if (!File.Exists(cachePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(cachePath);
            var snapshot = JsonSerializer.Deserialize<HeroCatalogSnapshot>(json, JsonOptions);
            return snapshot?.Heroes
                .Where(IsUsableEntry)
                .OrderBy(hero => hero.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public Task<IReadOnlyList<HeroCatalogEntry>> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Refresh(cancellationToken), cancellationToken);

    private IReadOnlyList<HeroCatalogEntry> Refresh(CancellationToken cancellationToken)
    {
        var vpkPath = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "pak01_dir.vpk");
        if (!File.Exists(vpkPath))
        {
            throw new FileNotFoundException(
                "Retail Deadlock pak01_dir.vpk was not found. Check the Retail Deadlock path in Settings.",
                vpkPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var package = new Package();
        package.Read(vpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        var entries = packageEntries.SelectMany(group => group.Value).ToArray();

        var heroesEntry = entries.FirstOrDefault(entry =>
            string.Equals(
                NormalizeResourcePath(entry.GetFullPath()),
                HeroesResourcePath,
                StringComparison.OrdinalIgnoreCase));
        if (heroesEntry is null)
        {
            throw new InvalidDataException($"{HeroesResourcePath} was not found in the current retail Deadlock VPK.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        package.ReadEntry(heroesEntry, out byte[] heroesRawData);
        using var heroesStream = new MemoryStream(heroesRawData, writable: false);
        using var heroesResource = new Resource { FileName = HeroesResourcePath };
        heroesResource.Read(heroesStream);
        using var fileLoader = new GameFileLoader(package, package.FileName);
        using var heroesContent = FileExtract.Extract(heroesResource, fileLoader, null);
        if (heroesContent.Data is null)
        {
            throw new InvalidDataException("ValveResourceFormat did not return decompiled heroes.vdata text.");
        }

        var heroesText = Encoding.UTF8.GetString(heroesContent.Data);

        var localizationText = string.Empty;
        var localizationEntry = entries.FirstOrDefault(entry =>
            string.Equals(
                NormalizeResourcePath(entry.GetFullPath()),
                HeroNamesLocalizationPath,
                StringComparison.OrdinalIgnoreCase));
        if (localizationEntry is not null)
        {
            package.ReadEntry(localizationEntry, out byte[] localizationRawData);
            localizationText = Encoding.UTF8.GetString(localizationRawData);
        }

        var localizations = ParseLocalization(localizationText);
        var heroes = ParseHeroes(heroesText, localizations)
            .Where(IsUsableEntry)
            .GroupBy(hero => hero.LookupName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(hero => hero.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (heroes.Length == 0)
        {
            throw new InvalidDataException(
                "No player-selectable heroes with models were found in the current retail heroes.vdata.");
        }

        SaveCache(new HeroCatalogSnapshot
        {
            RefreshedUtc = DateTimeOffset.UtcNow,
            SourceVpkPath = Path.GetFullPath(vpkPath),
            Heroes = [.. heroes],
        });

        return heroes;
    }

    private static IReadOnlyList<HeroCatalogEntry> ParseHeroes(
        string heroesText,
        IReadOnlyDictionary<string, string> localizations)
    {
        var heroes = new List<HeroCatalogEntry>();

        foreach (Match match in TopLevelBlockRegex.Matches(heroesText))
        {
            var blockKey = match.Groups["key"].Value;
            var openingBrace = heroesText.IndexOf('{', match.Index);
            if (openingBrace < 0)
            {
                continue;
            }

            var closingBrace = FindMatchingBrace(heroesText, openingBrace);
            if (closingBrace < 0)
            {
                continue;
            }

            var block = heroesText[(openingBrace + 1)..closingBrace];
            if (GetBoolProperty(block, "m_bPlayerSelectable") is not true
                || GetBoolProperty(block, "m_bDisabled") is true)
            {
                continue;
            }

            var modelResourcePath = FirstNonEmpty(
                GetStringProperty(block, "m_strMainOnlyModelName"),
                GetStringProperty(block, "m_strModelName"),
                GetStringProperty(block, "m_strWIPModelName"));
            if (modelResourcePath is null)
            {
                continue;
            }

            var lookupName = Path.GetFileNameWithoutExtension(modelResourcePath);
            if (string.IsNullOrWhiteSpace(lookupName))
            {
                continue;
            }

            var searchToken = GetStringProperty(block, "m_strHeroSearchName");
            var displayName = ResolveLocalizedName(searchToken, localizations)
                ?? ResolveLocalizedName($"#{blockKey}", localizations)
                ?? DisplayNameFromLogo(GetStringProperty(block, "m_strLogoImageEnglish"))
                ?? HumanizeIdentifier(blockKey);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            heroes.Add(new HeroCatalogEntry(
                displayName.Trim(),
                lookupName.Trim(),
                NormalizeResourcePath(modelResourcePath)));
        }

        return heroes;
    }

    private static Dictionary<string, string> ParseLocalization(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return values;
        }

        foreach (Match match in LocalizationRegex.Matches(text))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = UnescapeQuotedValue(match.Groups["value"].Value).Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            values[key] = value;

            var suffixIndex = key.IndexOf(':');
            if (suffixIndex > 0)
            {
                values.TryAdd(key[..suffixIndex], value);
            }
        }

        return values;
    }

    private static string? ResolveLocalizedName(
        string? token,
        IReadOnlyDictionary<string, string> localizations)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var key = token.Trim().TrimStart('#');
        if (localizations.TryGetValue(key, out var localized))
        {
            return localized;
        }

        return localizations.TryGetValue($"{key}:n", out localized) ? localized : null;
    }

    private static string? DisplayNameFromLogo(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        var fileName = Path.GetFileNameWithoutExtension(logoPath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return HumanizeIdentifier(fileName);
    }

    private static string HumanizeIdentifier(string value)
    {
        var normalized = value.Trim().TrimStart('#');
        if (normalized.StartsWith("hero_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..];
        }

        foreach (var suffix in new[] { "_search", "_sort", "_localized" })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        var words = normalized
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Equals("and", StringComparison.OrdinalIgnoreCase)
                ? "&"
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    private static string? GetStringProperty(string block, string propertyName)
    {
        var pattern = "(?m)^\\s*"
            + Regex.Escape(propertyName)
            + "\\s*=\\s*(?:[A-Za-z_]+:)?\"(?<value>(?:\\\\.|[^\"])*)\"";
        var match = Regex.Match(block, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var value = UnescapeQuotedValue(match.Groups["value"].Value).Trim();
        return value.Length == 0 ? null : value;
    }

    private static bool? GetBoolProperty(string block, string propertyName)
    {
        var pattern = "(?m)^\\s*"
            + Regex.Escape(propertyName)
            + "\\s*=\\s*(?<value>true|false)\\s*$";
        var match = Regex.Match(
            block,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success
            ? bool.Parse(match.Groups["value"].Value)
            : null;
    }

    private static int FindMatchingBrace(string text, int openingBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = openingBrace; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
            }
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string UnescapeQuotedValue(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static bool IsUsableEntry(HeroCatalogEntry hero) =>
        !string.IsNullOrWhiteSpace(hero.DisplayName)
        && !string.IsNullOrWhiteSpace(hero.LookupName);

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static void SaveCache(HeroCatalogSnapshot snapshot)
    {
        var cachePath = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        AtomicFile.WriteJson(cachePath, snapshot, JsonOptions);
    }

    private static string GetCachePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Deadlimit",
            "hero_catalog.json");
}
