namespace Deadlimit.App;

internal static class DialogUiFactory
{
    internal static Button CreateActionButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(72, 26),
        Margin = new Padding(6, 0, 0, 0),
    };
}
