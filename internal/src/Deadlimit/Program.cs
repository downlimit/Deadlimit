using System.Drawing;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private const string StartupSmokeArgument = "--startup-smoke";

    private static readonly Icon AppIcon = LoadAppIcon();
    private static readonly Size MainWindowSize = new(972, 672);

    [STAThread]
    private static void Main(string[] args)
    {
        var startupSmoke = args.Any(argument =>
            string.Equals(argument, StartupSmokeArgument, StringComparison.OrdinalIgnoreCase));

        var settings = ProjectStore.GetToolPathSettings();
        UiTheme.ConfigureApplication(settings.UiTheme);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new MainForm
        {
            Icon = AppIcon,
            Size = MainWindowSize,
            MinimumSize = MainWindowSize,
            MaximumSize = MainWindowSize,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
        };
        BuildFeature.Attach(form);
        ExtractionProgressFeature.Attach(form);
        ProjectLibraryFeature.Attach(form);
        HeroCatalogFeature.Attach(form);
        ProjectLogsFeature.Attach(form);
        ProjectHeaderFeature.Attach(form);
        ProjectFilesFeature.Attach(form);
        UiTheme.ApplyCustomPalette(form, settings.UiTheme);
        WindowProgressFeature.Attach(form);
        SteamStatusFeature.Attach(form, settings.UiTheme);
        SettingsVersionFeature.Attach();
        form.Shown += (_, _) => form.BeginInvoke((Action)(() => form.ActiveControl = null));

        if (!startupSmoke)
        {
            Application.Run(form);
            return;
        }

        using var smokeTimer = new System.Windows.Forms.Timer
        {
            Interval = 500,
        };
        smokeTimer.Tick += (_, _) =>
        {
            smokeTimer.Stop();
            form.Close();
        };
        form.Shown += (_, _) => smokeTimer.Start();
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
