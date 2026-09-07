using System.Diagnostics;

namespace Deadlimit.App;

internal static class SettingsShiftShortcutFeature
{
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;

    private const string CsdkPageUrl = "https://deadlockmodding.pages.dev/modding-tools/csdk-12";
    private const string DeadlockToolsPageUrl = "https://github.com/dotryen/DeadlockTools/releases/latest";

    private static readonly ShiftShortcutMessageFilter MessageFilter = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SettingsForm, object> PreparedForms = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, object> PreparedButtons = new();
    private static int _attached;

    public static void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) != 0)
        {
            return;
        }

        Application.AddMessageFilter(MessageFilter);
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>())
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

        var grid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel => panel.ColumnCount == 7 && panel.RowCount >= 5);
        if (grid is null)
        {
            return;
        }

        var expectedShortcuts = 0;
        var preparedShortcuts = 0;
        for (var row = 0; row < grid.RowCount; row++)
        {
            var target = ResolveTarget(grid, row);
            if (target == ShortcutTarget.None)
            {
                continue;
            }

            expectedShortcuts++;
            if (grid.GetControlFromPosition(5, row) is not Button primaryButton)
            {
                continue;
            }

            if (!PreparedButtons.TryGetValue(primaryButton, out _))
            {
                var shortcutTarget = target;
                primaryButton.MouseEnter += (_, _) => EnsureShortcutHint(primaryButton, shortcutTarget);
                PreparedButtons.Add(primaryButton, new object());
            }

            EnsureShortcutHint(primaryButton, target);
            preparedShortcuts++;
        }

        if (expectedShortcuts == 3 && preparedShortcuts == expectedShortcuts)
        {
            PreparedForms.Add(form, new object());
        }
    }

    private static void EnsureShortcutHint(Button button, ShortcutTarget target)
    {
        var hint = ShortcutHint(target);
        RichToolTip.TryAppendToolTip(button, hint);
        AppendAccessibleDescription(button, hint);
    }

    private static bool TryHandleShiftShortcut(Control? control)
    {
        var button = FindButton(control);
        if (button?.FindForm() is not SettingsForm
            || button.Parent is not TableLayoutPanel grid
            || grid.ColumnCount != 7)
        {
            return false;
        }

        var position = grid.GetPositionFromControl(button);
        if (position.Column != 5 || position.Row < 0)
        {
            return false;
        }

        var target = ResolveTarget(grid, position.Row);
        switch (target)
        {
            case ShortcutTarget.Csdk:
                OpenBrowser(
                    button,
                    CsdkPageUrl,
                    UiText.T("Could not open Reduced CSDK page", "Не удалось открыть страницу Reduced CSDK"));
                return true;

            case ShortcutTarget.DeadlockTools:
                OpenBrowser(
                    button,
                    DeadlockToolsPageUrl,
                    UiText.T("Could not open DeadlockTools page", "Не удалось открыть страницу DeadlockTools"));
                return true;

            case ShortcutTarget.DeadlockVpk:
                var retailRoot = (grid.GetControlFromPosition(2, position.Row) as TextBox)?.Text?.Trim() ?? string.Empty;
                OpenRetailVpkFolder(button, retailRoot);
                return true;

            default:
                return false;
        }
    }

    private static ShortcutTarget ResolveTarget(TableLayoutPanel grid, int row)
    {
        var caption = (grid.GetControlFromPosition(0, row)?.Text ?? string.Empty)
            .Trim()
            .TrimEnd(':')
            .Trim();

        if (string.Equals(caption, "Reduced CSDK", StringComparison.Ordinal))
        {
            return ShortcutTarget.Csdk;
        }

        if (string.Equals(caption, "DeadlockTools", StringComparison.Ordinal))
        {
            return ShortcutTarget.DeadlockTools;
        }

        if (string.Equals(caption, "Deadlock client", StringComparison.Ordinal)
            || string.Equals(caption, "Deadlock клиент", StringComparison.Ordinal))
        {
            return ShortcutTarget.DeadlockVpk;
        }

        return ShortcutTarget.None;
    }

    private static string ShortcutHint(ShortcutTarget target) => target switch
    {
        ShortcutTarget.Csdk => UiText.T(
            "SHIFT+click: open the Reduced CSDK download page in your browser.",
            "SHIFT-клик: открыть страницу загрузки Reduced CSDK в браузере."),
        ShortcutTarget.DeadlockTools => UiText.T(
            "SHIFT+click: open the DeadlockTools releases page in your browser.",
            "SHIFT-клик: открыть страницу релизов DeadlockTools в браузере."),
        ShortcutTarget.DeadlockVpk => UiText.T(
            "SHIFT+click: open the retail VPK folder (game\\citadel\\addons).",
            "SHIFT-клик: открыть папку retail VPK (game\\citadel\\addons)."),
        _ => string.Empty,
    };

    private static void AppendAccessibleDescription(Button button, string hint)
    {
        var current = button.AccessibleDescription?.Trim() ?? string.Empty;
        if (current.Contains(hint, StringComparison.Ordinal))
        {
            return;
        }

        button.AccessibleDescription = current.Length == 0
            ? hint
            : $"{current} {hint}";
    }

    private static Button? FindButton(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is Button button)
            {
                return button;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void OpenBrowser(IWin32Window owner, string url, string errorTitle)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(owner, exception.Message, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenRetailVpkFolder(IWin32Window owner, string retailRoot)
    {
        if (string.IsNullOrWhiteSpace(retailRoot))
        {
            MessageBox.Show(
                owner,
                UiText.T(
                    "No Deadlock client folder is configured yet.",
                    "Папка Deadlock клиента пока не указана."),
                UiText.T("VPK folder unavailable", "Папка VPK недоступна"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var vpkFolder = Path.Combine(retailRoot, "game", "citadel", "addons");
        if (!Directory.Exists(vpkFolder))
        {
            MessageBox.Show(
                owner,
                UiText.T(
                    $"The retail VPK folder does not exist yet:\n{vpkFolder}\n\nBuild & Test creates it automatically when the first VPK is deployed.",
                    $"Папка retail VPK пока не существует:\n{vpkFolder}\n\nBuild & Test создаст её автоматически при первой выгрузке VPK."),
                UiText.T("VPK folder unavailable", "Папка VPK недоступна"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{vpkFolder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                exception.Message,
                UiText.T("Could not open VPK folder", "Не удалось открыть папку VPK"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private sealed class ShiftShortcutMessageFilter : IMessageFilter
    {
        private IntPtr _swallowMouseUpHandle;

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg == WmLButtonDown && _swallowMouseUpHandle != IntPtr.Zero)
            {
                _swallowMouseUpHandle = IntPtr.Zero;
            }

            if (message.Msg == WmLButtonUp && _swallowMouseUpHandle != IntPtr.Zero)
            {
                if (message.HWnd == _swallowMouseUpHandle)
                {
                    _swallowMouseUpHandle = IntPtr.Zero;
                    return true;
                }

                _swallowMouseUpHandle = IntPtr.Zero;
            }

            if (message.Msg != WmLButtonDown
                || (Control.ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                return false;
            }

            var control = Control.FromHandle(message.HWnd);
            if (!TryHandleShiftShortcut(control))
            {
                return false;
            }

            _swallowMouseUpHandle = message.HWnd;
            return true;
        }
    }

    private enum ShortcutTarget
    {
        None,
        Csdk,
        DeadlockTools,
        DeadlockVpk,
    }
}
