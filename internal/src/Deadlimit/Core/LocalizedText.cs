namespace Deadlimit.Core;

internal static class LocalizedText
{
    public static bool IsRussian =>
        string.Equals(
            ProjectStore.GetToolPathSettings().UiLanguage,
            "ru",
            StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string russian) =>
        IsRussian ? russian : english;
}
