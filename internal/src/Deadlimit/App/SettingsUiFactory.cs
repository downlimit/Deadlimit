namespace Deadlimit.App;

internal static class SettingsUiFactory
{
    internal static Button CreateActionButton() => new()
    {
        AutoSize = false,
        Width = 94,
        Height = 26,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 5, 3),
    };
}
