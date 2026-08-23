using System.Drawing;

namespace Deadlimit.App;

internal static class UiTheme
{
    private static readonly Palette LightPalette = new(
        Background: Color.FromArgb(242, 242, 242),
        Surface: Color.FromArgb(248, 248, 248),
        Input: Color.FromArgb(255, 255, 255),
        Button: Color.FromArgb(232, 232, 232),
        ButtonHover: Color.FromArgb(224, 224, 224),
        ButtonPressed: Color.FromArgb(214, 214, 214),
        Border: Color.FromArgb(190, 190, 190),
        Text: Color.FromArgb(34, 34, 34),
        MutedText: Color.FromArgb(86, 86, 86));

    private static readonly Palette GrayPalette = new(
        Background: Color.FromArgb(58, 58, 58),
        Surface: Color.FromArgb(65, 65, 65),
        Input: Color.FromArgb(72, 72, 72),
        Button: Color.FromArgb(76, 76, 76),
        ButtonHover: Color.FromArgb(84, 84, 84),
        ButtonPressed: Color.FromArgb(92, 92, 92),
        Border: Color.FromArgb(96, 96, 96),
        Text: Color.FromArgb(226, 226, 226),
        MutedText: Color.FromArgb(184, 184, 184));

    // Tuned from the current CSDK12 visual hierarchy: low-contrast dark surfaces,
    // subdued borders and controls that are only slightly lighter than the window.
    private static readonly Palette DarkPalette = new(
        Background: Color.FromArgb(27, 27, 27),
        Surface: Color.FromArgb(33, 33, 33),
        Input: Color.FromArgb(36, 36, 36),
        Button: Color.FromArgb(52, 52, 52),
        ButtonHover: Color.FromArgb(61, 61, 61),
        ButtonPressed: Color.FromArgb(70, 70, 70),
        Border: Color.FromArgb(72, 72, 72),
        Text: Color.FromArgb(210, 210, 210),
        MutedText: Color.FromArgb(160, 160, 160));

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
        if (SystemInformation.HighContrast)
        {
            return;
        }

        ApplyPalette(root, ResolvePalette(theme));
    }

    private static Palette ResolvePalette(string theme)
    {
        return Normalize(theme) switch
        {
            "light" => LightPalette,
            "gray" => GrayPalette,
            "dark" => DarkPalette,
            _ => Application.IsDarkModeEnabled ? DarkPalette : LightPalette,
        };
    }

    private static void ApplyPalette(Control control, Palette palette)
    {
        switch (control)
        {
            case Form:
                control.BackColor = palette.Background;
                control.ForeColor = palette.Text;
                break;

            case GroupBox groupBox:
                groupBox.FlatStyle = FlatStyle.Flat;
                groupBox.BackColor = palette.Surface;
                groupBox.ForeColor = palette.MutedText;
                break;

            case TextBoxBase textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = palette.Input;
                textBox.ForeColor = palette.Text;
                break;

            case ListBox listBox:
                listBox.BorderStyle = BorderStyle.FixedSingle;
                listBox.BackColor = palette.Input;
                listBox.ForeColor = palette.Text;
                break;

            case ComboBox comboBox:
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = palette.Input;
                comboBox.ForeColor = palette.Text;
                break;

            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = palette.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = palette.ButtonPressed;
                button.BackColor = palette.Button;
                button.ForeColor = palette.Text;
                break;

            case StatusStrip statusStrip:
                statusStrip.BackColor = palette.Surface;
                statusStrip.ForeColor = palette.MutedText;
                foreach (ToolStripItem item in statusStrip.Items)
                {
                    item.BackColor = palette.Surface;
                    item.ForeColor = palette.MutedText;
                }
                break;

            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = palette.Text;
                break;

            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = palette.Text;
                break;

            case RadioButton radioButton:
                radioButton.BackColor = Color.Transparent;
                radioButton.ForeColor = palette.Text;
                break;

            case TableLayoutPanel:
            case FlowLayoutPanel:
            case Panel:
                control.BackColor = control.Parent?.BackColor ?? palette.Background;
                control.ForeColor = palette.Text;
                break;

            default:
                control.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyPalette(child, palette);
        }
    }

    private static string Normalize(string? theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        return normalized is "light" or "gray" or "dark" ? normalized : "system";
    }

    private sealed record Palette(
        Color Background,
        Color Surface,
        Color Input,
        Color Button,
        Color ButtonHover,
        Color ButtonPressed,
        Color Border,
        Color Text,
        Color MutedText);
}
