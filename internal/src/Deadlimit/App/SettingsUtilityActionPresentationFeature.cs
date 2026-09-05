using System.Runtime.CompilerServices;

namespace Deadlimit.App;

internal static class SettingsUtilityActionPresentationFeature
{
    private const int WmShowWindow = 0x0018;
    private static readonly ConditionalWeakTable<SettingsForm, PreparedMarker> PreparedForms = new();
    private static readonly FirstShowFilter MessageFilter = new();

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        Application.AddMessageFilter(MessageFilter);
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
        {
            Prepare(form);
        }
    }

    private static void Prepare(SettingsForm form)
    {
        if (PreparedForms.TryGetValue(form, out _))
        {
            return;
        }

        var scriptsButton = EnumerateControls(form)
            .OfType<Button>()
            .FirstOrDefault(button => button.Text is "Open scripts section" or "Открыть раздел скриптов");
        if (scriptsButton is null)
        {
            return;
        }

        scriptsButton.Text = UiText.T("📂 Open section", "📂 Открыть раздел");
        scriptsButton.Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point);
        PreparedForms.Add(form, new PreparedMarker());
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateControls(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FirstShowFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg == WmShowWindow
                && message.WParam != IntPtr.Zero
                && Control.FromHandle(message.HWnd) is SettingsForm form
                && !form.IsDisposed)
            {
                Prepare(form);
            }

            return false;
        }
    }

    private sealed class PreparedMarker
    {
    }
}
