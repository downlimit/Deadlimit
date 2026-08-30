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
            .Replace("DeadlimitAggregator.exe", "DeadlimitManager.exe", StringComparison.Ordinal);
}
