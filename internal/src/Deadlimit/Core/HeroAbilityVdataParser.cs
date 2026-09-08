using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record HeroAbilityVdataSelection(
    IReadOnlyList<string> AbilityNames,
    IReadOnlyList<string> VisualResourcePaths);

internal static class HeroAbilityVdataParser
{
    private static readonly Regex TopLevelBlockRegex = new(
        @"(?m)^\t(?<key>[A-Za-z0-9_]+)\s*=\s*\r?\n\t\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssignedQuotedValueRegex = new(
        "=\\s*(?:[A-Za-z_]+:)?\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedValueRegex = new(
        "\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ModelPropertyNames =
    [
        "m_strMainOnlyModelName",
        "m_strModelName",
        "m_strWIPModelName",
    ];

    private static readonly string[] VisualRootExtensions =
    [
        ".vpcf",
        ".vmdl",
        ".vmesh",
        ".vmat",
        ".vtex",
        ".vsnap",
        ".vphys",
        ".vanim",
        ".vagrp",
        ".vseq",
    ];

    internal static HeroAbilityVdataSelection Resolve(
        string heroesText,
        string abilitiesText,
        string mainModelResourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heroesText);
        ArgumentException.ThrowIfNullOrWhiteSpace(abilitiesText);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainModelResourcePath);

        var heroBlocks = ParseTopLevelBlocks(heroesText);
        var expectedModelPath = ToSourceResourcePath(mainModelResourcePath);
        var heroBlock = heroBlocks.Values.FirstOrDefault(block =>
            ModelPropertyNames
                .Select(propertyName => GetStringProperty(block, propertyName))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Any(value => string.Equals(
                    NormalizeResourcePath(value!),
                    expectedModelPath,
                    StringComparison.OrdinalIgnoreCase)));

        if (heroBlock is null)
        {
            throw new InvalidDataException(
                $"Could not match the selected hero model '{mainModelResourcePath}' to a heroes.vdata entry.");
        }

        var boundAbilitiesBlock = GetObjectProperty(heroBlock, "m_mapBoundAbilities");
        if (boundAbilitiesBlock is null)
        {
            return new HeroAbilityVdataSelection([], []);
        }

        var boundAbilityNames = AssignedQuotedValueRegex.Matches(boundAbilitiesBlock)
            .Select(match => UnescapeQuotedValue(match.Groups["value"].Value).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (boundAbilityNames.Length == 0)
        {
            return new HeroAbilityVdataSelection([], []);
        }

        var abilityBlocks = ParseTopLevelBlocks(abilitiesText);
        var pending = new Queue<string>(boundAbilityNames);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visualRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var abilityName = pending.Dequeue().Trim();
            if (abilityName.Length == 0 || !selectedNames.Add(abilityName))
            {
                continue;
            }

            if (!abilityBlocks.TryGetValue(abilityName, out var abilityBlock))
            {
                continue;
            }

            var baseName = GetStringProperty(abilityBlock, "_base");
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                pending.Enqueue(baseName);
            }

            foreach (var multiBaseName in GetArrayStrings(abilityBlock, "_multibase"))
            {
                pending.Enqueue(multiBaseName);
            }

            foreach (Match match in QuotedValueRegex.Matches(abilityBlock))
            {
                var value = UnescapeQuotedValue(match.Groups["value"].Value).Trim();
                if (TryNormalizeVisualRoot(value, out var resourcePath))
                {
                    visualRoots.Add(resourcePath);
                }
            }
        }

        return new HeroAbilityVdataSelection(
            selectedNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            visualRoots.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static Dictionary<string, string> ParseTopLevelBlocks(string text)
    {
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TopLevelBlockRegex.Matches(text))
        {
            var key = match.Groups["key"].Value;
            var openingBrace = text.IndexOf('{', match.Index);
            if (openingBrace < 0)
            {
                continue;
            }

            var closingBrace = FindMatchingBrace(text, openingBrace);
            if (closingBrace < 0)
            {
                continue;
            }

            blocks[key] = text[(openingBrace + 1)..closingBrace];
        }

        return blocks;
    }

    private static string? GetStringProperty(string block, string propertyName)
    {
        var pattern = "(?m)^\\s*"
            + Regex.Escape(propertyName)
            + "\\s*=\\s*(?:[A-Za-z_]+:)?\"(?<value>(?:\\\\.|[^\"])*)\"";
        var match = Regex.Match(block, pattern, RegexOptions.CultureInvariant);
        return match.Success
            ? UnescapeQuotedValue(match.Groups["value"].Value).Trim()
            : null;
    }

    private static string? GetObjectProperty(string block, string propertyName)
    {
        var match = Regex.Match(
            block,
            "(?m)^\\s*" + Regex.Escape(propertyName) + "\\s*=\\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var openingBrace = block.IndexOf('{', match.Index + match.Length);
        if (openingBrace < 0)
        {
            return null;
        }

        var closingBrace = FindMatchingBrace(block, openingBrace);
        return closingBrace < 0
            ? null
            : block[(openingBrace + 1)..closingBrace];
    }

    private static IReadOnlyList<string> GetArrayStrings(string block, string propertyName)
    {
        var match = Regex.Match(
            block,
            "(?m)^\\s*" + Regex.Escape(propertyName) + "\\s*=\\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return [];
        }

        var openingBracket = block.IndexOf('[', match.Index + match.Length);
        if (openingBracket < 0)
        {
            return [];
        }

        var closingBracket = FindMatchingBracket(block, openingBracket);
        if (closingBracket < 0)
        {
            return [];
        }

        var arrayText = block[(openingBracket + 1)..closingBracket];
        return QuotedValueRegex.Matches(arrayText)
            .Select(item => UnescapeQuotedValue(item.Groups["value"].Value).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryNormalizeVisualRoot(string rawValue, out string resourcePath)
    {
        resourcePath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();
        const string resourcePrefix = "s2r://";
        if (normalized.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[resourcePrefix.Length..];
        }

        normalized = NormalizeResourcePath(normalized);
        if (normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }

        if (!VisualRootExtensions.Any(extension =>
                normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        resourcePath = normalized;
        return true;
    }

    private static string ToSourceResourcePath(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        return normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^2]
            : normalized;
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').Trim().Trim('"').TrimStart('/');

    private static string UnescapeQuotedValue(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static int FindMatchingBrace(string text, int openingBrace) =>
        FindMatchingDelimiter(text, openingBrace, '{', '}');

    private static int FindMatchingBracket(string text, int openingBracket) =>
        FindMatchingDelimiter(text, openingBracket, '[', ']');

    private static int FindMatchingDelimiter(string text, int openingIndex, char opening, char closing)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = openingIndex; index < text.Length; index++)
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
            else if (current == opening)
            {
                depth++;
            }
            else if (current == closing)
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
}
