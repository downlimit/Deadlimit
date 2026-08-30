using Deadlimit.Core;

namespace Deadlimit.App;

internal static class UiText
{
    public const string ProductName = "Deadlimit Manager";

    public static string Language => ProjectStore.GetToolPathSettings().UiLanguage;

    public static bool IsRussian =>
        string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string russian) =>
        NormalizeProductNames(IsRussian ? russian : english);

    public static string NormalizeProductNames(string value) =>
        value
            .Replace("Deadlimit Aggregator", ProductName, StringComparison.Ordinal)
            .Replace("DeadlimitAggregator.exe", "DeadlimitManager.exe", StringComparison.Ordinal)
            .Replace("Reduced CSDK12", "Reduced CSDK", StringComparison.Ordinal)
            .Replace("CSDK12", "CSDK", StringComparison.Ordinal)
            .Replace("Choose an empty folder for DeadlockTools", "Choose where DeadlockTools should be installed", StringComparison.Ordinal)
            .Replace("Выберите пустую папку для DeadlockTools", "Выберите папку, в которой создать DeadlockTools", StringComparison.Ordinal)
            .Replace("downloads the latest official Windows x64 release from GitHub into an empty folder", "downloads the latest official Windows x64 release from GitHub and creates one DeadlockTools folder in the selected location", StringComparison.Ordinal)
            .Replace("скачивает последний официальный Windows x64 release с GitHub в пустую папку", "скачивает последний официальный Windows x64 release с GitHub и сам создаёт одну папку DeadlockTools в выбранном месте", StringComparison.Ordinal);
}
