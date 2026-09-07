using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailModLoadingResult(
    string GameInfoPath,
    bool AlreadyEnabled,
    bool Patched,
    string? BackupPath);

public sealed class RetailModLoadingService
{
    private static readonly Regex SearchPathsBlockRegex = new(
        @"(?im)^(?<header>\s*SearchPaths\s*\r?\n\s*\{\s*\r?\n)(?<body>.*?)(?<close>^\s*\}\s*$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AddonsGamePathRegex = new(
        "(?im)^\\s*Game\\s+\"?citadel[\\\\/]addons\"?\\s*(?://.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex CitadelGamePathRegex = new(
        "(?im)^(?<indent>\\s*)Game\\s+\"?citadel\"?\\s*(?://.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex AnyModPathRegex = new(
        "(?im)^\\s*Mod\\s+.+$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex AnyWritePathRegex = new(
        "(?im)^\\s*Write\\s+.+$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public RetailModLoadingService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public RetailModLoadingResult EnsureEnabled(ProjectManifest manifest)
    {
        var gameInfoPath = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "gameinfo.gi");
        if (!File.Exists(gameInfoPath))
        {
            throw new FileNotFoundException(
                "Retail Deadlock gameinfo.gi was not found. Check the Retail Deadlock path in SETTINGS.",
                gameInfoPath);
        }

        var text = File.ReadAllText(gameInfoPath);
        var searchPathsMatch = SearchPathsBlockRegex.Match(text);
        if (!searchPathsMatch.Success)
        {
            throw new InvalidOperationException(
                "Deadlimit Manager could not safely locate the SearchPaths block in retail gameinfo.gi. " +
                "The file was not modified. Verify Deadlock files through Steam if gameinfo.gi is malformed.");
        }

        var body = searchPathsMatch.Groups["body"].Value;
        var newline = DetectNewLine(text);
        var patchedBody = PatchSearchPathsBody(body, newline);
        if (string.Equals(patchedBody, body, StringComparison.Ordinal))
        {
            return new RetailModLoadingResult(gameInfoPath, AlreadyEnabled: true, Patched: false, BackupPath: null);
        }

        var patchedText = text[..searchPathsMatch.Groups["body"].Index]
            + patchedBody
            + text[(searchPathsMatch.Groups["body"].Index + searchPathsMatch.Groups["body"].Length)..];

        var validationMatch = SearchPathsBlockRegex.Match(patchedText);
        var validationBody = validationMatch.Success
            ? validationMatch.Groups["body"].Value
            : string.Empty;
        if (!validationMatch.Success
            || !AddonsGamePathRegex.IsMatch(validationBody)
            || !AnyModPathRegex.IsMatch(validationBody)
            || !AnyWritePathRegex.IsMatch(validationBody))
        {
            throw new InvalidOperationException(
                "Deadlimit Manager prepared a gameinfo.gi patch, but validation did not detect the required addons, MOD and write search paths. " +
                "The retail file was not modified.");
        }

        var backupFolder = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), "backups");
        Directory.CreateDirectory(backupFolder);
        var backupPath = Path.Combine(
            backupFolder,
            $"gameinfo-{DateTime.Now:yyyyMMdd-HHmmss}.gi.bak");

        File.Copy(gameInfoPath, backupPath, overwrite: false);
        AtomicFile.WriteAllText(
            gameInfoPath,
            patchedText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new RetailModLoadingResult(gameInfoPath, AlreadyEnabled: false, Patched: true, BackupPath: backupPath);
    }

    private static string PatchSearchPathsBody(string body, string newline)
    {
        var hasAddons = AddonsGamePathRegex.IsMatch(body);
        var hasExplicitMod = AnyModPathRegex.IsMatch(body);
        var hasExplicitWrite = AnyWritePathRegex.IsMatch(body);

        if (hasAddons && hasExplicitMod && hasExplicitWrite)
        {
            return body;
        }

        var citadelMatch = CitadelGamePathRegex.Match(body);
        if (!citadelMatch.Success)
        {
            throw new InvalidOperationException(
                "Deadlimit Manager found SearchPaths in retail gameinfo.gi, but could not find the normal 'Game citadel' entry. " +
                "The file was not modified because the current layout is not safely recognized.");
        }

        var indentation = citadelMatch.Groups["indent"].Value;
        var insertion = new StringBuilder();

        if (!hasAddons)
        {
            insertion.Append($"{indentation}Game                citadel/addons{newline}");
        }

        // Stock Deadlock has no explicit Mod/Write entries. Source 2 therefore derives
        // both from the first Game path, which is normally citadel. Inserting addons
        // before that Game entry without materializing the implicit paths changes the
        // runtime MOD/default-write roots to citadel/addons and can break startup.
        if (!hasExplicitMod)
        {
            insertion.Append($"{indentation}Mod                 citadel{newline}");
        }

        if (!hasExplicitWrite)
        {
            insertion.Append($"{indentation}Write               citadel{newline}");
        }

        return body.Insert(citadelMatch.Index, insertion.ToString());
    }

    private static string DetectNewLine(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
