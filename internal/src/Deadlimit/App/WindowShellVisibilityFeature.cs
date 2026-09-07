namespace Deadlimit.App;

/// <summary>
/// Keeps every visible user-facing Deadlimit window reachable through the Windows
/// taskbar and Alt+Tab. Older dialogs intentionally used ShowInTaskbar=false while
/// also being modal/owned, which could leave the disabled MainForm as the only shell
/// entry and make the active dialog effectively unreachable after switching apps.
/// </summary>
internal static class WindowShellVisibilityFeature
{
    private static int _attached;

    public static void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) != 0)
        {
            return;
        }

        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form.IsDisposed
                || !form.Visible
                || form is StartupProgressForm
                || form.ShowInTaskbar)
            {
                continue;
            }

            // StartupProgressForm is the only deliberately shell-hidden top-level
            // window. Settings, build summaries and themed message dialogs are all
            // interactive and must remain reachable while their owner is disabled.
            form.ShowInTaskbar = true;
        }
    }
}
