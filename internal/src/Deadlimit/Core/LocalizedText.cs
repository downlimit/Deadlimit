using System.Runtime.CompilerServices;

namespace Deadlimit.Core;

internal static class LocalizedText
{
    public static string Language => ProjectStore.GetToolPathSettings().UiLanguage;

    public static bool IsRussian =>
        string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    public static bool IsSimplifiedChinese =>
        string.Equals(Language, "zh-CN", StringComparison.OrdinalIgnoreCase);

    public static bool IsBrazilianPortuguese =>
        string.Equals(Language, "pt-BR", StringComparison.OrdinalIgnoreCase);

    public static string T(
        string english,
        string russian,
        [CallerArgumentExpression(nameof(english))] string? englishExpression = null) =>
        Language switch
        {
            "ru" => russian,
            "zh-CN" => LocalizedTextCatalog.Translate("zh-CN", english, englishExpression),
            "pt-BR" => LocalizedTextCatalog.Translate("pt-BR", english, englishExpression),
            _ => english,
        };
}
