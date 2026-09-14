namespace Deadlimit.App;

internal static class SettingsUiFactory
{
    internal static Button CreateActionButton() => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 0, 4),
    };
}
