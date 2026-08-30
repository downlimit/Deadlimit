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
            .Replace("скачивает последний официальный Windows x64 release с GitHub в пустую папку", "скачивает последний официальный Windows x64 release с GitHub и сам создаёт одну папку DeadlockTools в выбранном месте", StringComparison.Ordinal)
            .Replace("Theme changes preview immediately; Save applies language and theme without restarting Deadlimit Manager.", "Theme changes preview immediately; Apply commits language and theme without restarting Deadlimit Manager.", StringComparison.Ordinal)
            .Replace("Тема меняется сразу; после сохранения язык и тема применяются без перезапуска Deadlimit Manager.", "Тема меняется сразу; кнопка «Применить» фиксирует язык и тему без перезапуска Deadlimit Manager.", StringComparison.Ordinal)
            .Replace("Run the optional full CSDK setup from the current installation guide.", "Run the optional CSDK fine-tuning from the current installation guide.", StringComparison.Ordinal)
            .Replace("Выполнить дополнительную полную настройку CSDK по актуальной инструкции.", "Выполнить дополнительную донастройку CSDK по актуальной инструкции.", StringComparison.Ordinal)
            .Replace("Could not complete CSDK setup", "Could not complete CSDK fine-tuning", StringComparison.Ordinal)
            .Replace("Не удалось выполнить настройку CSDK", "Не удалось выполнить донастройку CSDK", StringComparison.Ordinal);
}
