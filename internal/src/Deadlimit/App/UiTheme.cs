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
        HoverBorder: Color.FromArgb(150, 150, 150),
        Text: Color.FromArgb(34, 34, 34),
        MutedText: Color.FromArgb(86, 86, 86),
        ButtonText: Color.FromArgb(34, 34, 34),
        ButtonBorderSize: 1,
        ButtonHoverBorderSize: 1);

    private static readonly Palette GrayPalette = new(
        Background: Color.FromArgb(58, 58, 58),
        Surface: Color.FromArgb(65, 65, 65),
        Input: Color.FromArgb(72, 72, 72),
        Button: Color.FromArgb(76, 76, 76),
        ButtonHover: Color.FromArgb(84, 84, 84),
        ButtonPressed: Color.FromArgb(92, 92, 92),
        Border: Color.FromArgb(96, 96, 96),
        HoverBorder: Color.FromArgb(150, 150, 150),
        Text: Color.FromArgb(226, 226, 226),
        MutedText: Color.FromArgb(184, 184, 184),
        ButtonText: Color.FromArgb(226, 226, 226),
        ButtonBorderSize: 1,
        ButtonHoverBorderSize: 1);

    // Measured from the current CSDK12 reference. Normal buttons are #3C3C3C without
    // a bright outline; hovered buttons are #464646 with an approximately #969696 outline.
    private static readonly Palette DarkPalette = new(
        Background: Color.FromArgb(27, 27, 27),
        Surface: Color.FromArgb(33, 33, 33),
        Input: Color.FromArgb(36, 36, 36),
        Button: Color.FromArgb(60, 60, 60),
        ButtonHover: Color.FromArgb(70, 70, 70),
        ButtonPressed: Color.FromArgb(50, 50, 50),
        Border: Color.FromArgb(63, 63, 63),
        HoverBorder: Color.FromArgb(150, 150, 150),
        Text: Color.FromArgb(210, 210, 210),
        MutedText: Color.FromArgb(160, 160, 160),
        ButtonText: Color.FromArgb(180, 180, 180),
        ButtonBorderSize: 0,
        ButtonHoverBorderSize: 1);

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
                groupBox.Paint += (_, e) => DrawGroupBox(groupBox, e.Graphics, palette.Border);
                break;

            case TextBoxBase textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = palette.Input;
                textBox.ForeColor = palette.Text;
                break;

            case NumericUpDown numericUpDown:
                numericUpDown.BorderStyle = BorderStyle.FixedSingle;
                numericUpDown.BackColor = palette.Input;
                numericUpDown.ForeColor = palette.Text;
                break;

            case ListBox listBox:
                // Native WinForms list borders can pick up the Windows accent/focus color.
                // The enclosing Deadlimit section already provides the visual boundary.
                listBox.BorderStyle = BorderStyle.None;
                listBox.BackColor = palette.Input;
                listBox.ForeColor = palette.Text;
                break;

            case ComboBox comboBox:
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = palette.Input;
                comboBox.ForeColor = palette.Text;
                break;

            case Button button:
                ConfigureButton(button, palette);
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

    private static void ConfigureButton(Button button, Palette palette)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.ForeColor = palette.ButtonText;

        SetButtonNormal(button, palette);

        // Explicit state changes are used instead of relying only on FlatAppearance's
        // themed hover handling, which is inconsistent under WinForms dark mode.
        button.MouseEnter += (_, _) => SetButtonHover(button, palette);
        button.MouseLeave += (_, _) => SetButtonNormal(button, palette);
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                button.BackColor = palette.ButtonPressed;
                button.FlatAppearance.BorderColor = palette.HoverBorder;
                button.FlatAppearance.BorderSize = palette.ButtonHoverBorderSize;
            }
        };
        button.MouseUp += (_, _) =>
        {
            var pointer = button.PointToClient(Cursor.Position);
            if (button.ClientRectangle.Contains(pointer))
            {
                SetButtonHover(button, palette);
            }
            else
            {
                SetButtonNormal(button, palette);
            }
        };
    }

    private static void SetButtonNormal(Button button, Palette palette)
    {
        button.BackColor = palette.Button;
        button.FlatAppearance.BorderColor = palette.Border;
        button.FlatAppearance.BorderSize = palette.ButtonBorderSize;
        button.FlatAppearance.MouseOverBackColor = palette.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = palette.ButtonPressed;
    }

    private static void SetButtonHover(Button button, Palette palette)
    {
        button.BackColor = palette.ButtonHover;
        button.FlatAppearance.BorderColor = palette.HoverBorder;
        button.FlatAppearance.BorderSize = palette.ButtonHoverBorderSize;
    }

    private static void DrawGroupBox(GroupBox groupBox, Graphics graphics, Color borderColor)
    {
        if (groupBox.ClientSize.Width < 2 || groupBox.ClientSize.Height < 2)
        {
            return;
        }

        // Clear the native GroupBox painting entirely so Windows hover/focus accent
        // rendering cannot leak through around the custom Deadlimit frame.
        graphics.Clear(groupBox.BackColor);

        var captionSize = TextRenderer.MeasureText(
            graphics,
            groupBox.Text,
            groupBox.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var borderTop = Math.Max(7, captionSize.Height / 2);
        var right = groupBox.ClientSize.Width - 1;
        var bottom = groupBox.ClientSize.Height - 1;
        var captionLeft = 8;
        var captionRight = Math.Min(right, captionLeft + captionSize.Width + 6);

        using var borderPen = new Pen(borderColor);
        graphics.DrawLine(borderPen, 0, borderTop, Math.Max(0, captionLeft - 3), borderTop);
        if (captionRight < right)
        {
            graphics.DrawLine(borderPen, captionRight, borderTop, right, borderTop);
        }
        graphics.DrawLine(borderPen, 0, borderTop, 0, bottom);
        graphics.DrawLine(borderPen, right, borderTop, right, bottom);
        graphics.DrawLine(borderPen, 0, bottom, right, bottom);

        var captionRect = new Rectangle(captionLeft, 0, captionSize.Width, captionSize.Height);
        TextRenderer.DrawText(
            graphics,
            groupBox.Text,
            groupBox.Font,
            captionRect,
            groupBox.ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
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
        Color HoverBorder,
        Color Text,
        Color MutedText,
        Color ButtonText,
        int ButtonBorderSize,
        int ButtonHoverBorderSize);
}
