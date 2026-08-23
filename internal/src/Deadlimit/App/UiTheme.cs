using System.Drawing;

namespace Deadlimit.App;

internal static class UiTheme
{
    private static readonly Color GrayBackground = Color.FromArgb(78, 78, 78);
    private static readonly Color GraySurface = Color.FromArgb(92, 92, 92);
    private static readonly Color GrayInput = Color.FromArgb(108, 108, 108);
    private static readonly Color GrayText = Color.FromArgb(242, 242, 242);

    public static void ConfigureApplication(string theme)
    {
        Application.SetColorMode(Normalize(theme) switch
        {
            "dark" => SystemColorMode.Dark,
            "system" => SystemColorMode.System,
            _ => SystemColorMode.Classic,
        });
    }

    public static void ApplyCustomPalette(Control root, string theme)
    {
        if (!string.Equals(Normalize(theme), "gray", StringComparison.Ordinal))
        {
            return;
        }

        ApplyGray(root);
    }

    private static void ApplyGray(Control control)
    {
        switch (control)
        {
            case TextBoxBase textBox:
                textBox.BackColor = GrayInput;
                textBox.ForeColor = GrayText;
                break;

            case ListBox listBox:
                listBox.BackColor = GrayInput;
                listBox.ForeColor = GrayText;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = GrayInput;
                comboBox.ForeColor = GrayText;
                break;

            case Button button:
                button.UseVisualStyleBackColor = false;
                button.BackColor = GraySurface;
                button.ForeColor = GrayText;
                break;

            case StatusStrip statusStrip:
                statusStrip.BackColor = GraySurface;
                statusStrip.ForeColor = GrayText;
                foreach (ToolStripItem item in statusStrip.Items)
                {
                    item.BackColor = GraySurface;
                    item.ForeColor = GrayText;
                }
                break;

            case Form:
            case GroupBox:
            case Panel:
                control.BackColor = GrayBackground;
                control.ForeColor = GrayText;
                break;

            default:
                control.ForeColor = GrayText;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyGray(child);
        }
    }

    private static string Normalize(string? theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        return normalized is "light" or "gray" or "dark" ? normalized : "system";
    }
}
