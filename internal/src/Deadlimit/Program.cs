using System.Drawing;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private static readonly Icon AppIcon = LoadAppIcon();

    [STAThread]
    private static void Main()
    {
        var settings = ProjectStore.GetToolPathSettings();
        UiTheme.ConfigureApplication(settings.UiTheme);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm
        {
            Icon = AppIcon,
        };
        BuildFeature.Attach(form);
        UiTheme.ApplyCustomPalette(form, settings.UiTheme);
        Application.Run(form);
    }

    private static Icon LoadAppIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Deadlimit.AppIcon.ico")
            ?? throw new InvalidOperationException("Embedded Deadlimit application icon was not found.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
