namespace Deadlimit.App;

internal static class UiSettingsChangeBus
{
    public static event EventHandler? Changed;

    public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
