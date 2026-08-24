using System.Drawing;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private static readonly Icon AppIcon = LoadAppIcon();
    private static readonly Size MainWindowSize = new(972, 672);

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
            Size = MainWindowSize,
            MinimumSize = MainWindowSize,
            MaximumSize = MainWindowSize,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
        };
        BuildFeature.Attach(form);
        ProjectLibraryFeature.Attach(form);
        HeroCatalogFeature.Attach(form);
        ProjectHeaderFeature.Attach(form);
        ProjectFilesFeature.Attach(form);
        UiTheme.ApplyCustomPalette(form, settings.UiTheme);
        form.Shown += (_, _) => form.BeginInvoke((Action)(() => form.ActiveControl = null));
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
