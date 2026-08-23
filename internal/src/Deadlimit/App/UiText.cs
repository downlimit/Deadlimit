using Deadlimit.Core;

namespace Deadlimit.App;

internal static class UiText
{
    public static string Language => ProjectStore.GetToolPathSettings().UiLanguage;

    public static bool IsRussian =>
        string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string russian) =>
        IsRussian ? russian : english;
}
