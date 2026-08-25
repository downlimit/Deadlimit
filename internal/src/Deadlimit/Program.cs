using System.Drawing;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private const string StartupSmokeArgument = "--startup-smoke";
    private const string WriteVertexColorScriptArgument = "--write-vertex-color-script";

    private static readonly Icon AppIcon = LoadAppIcon();
    private static readonly Size MainWindowSize = new(972, 672);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0
            && string.Equals(args[0], WriteVertexColorScriptArgument, StringComparison.OrdinalIgnoreCase))
        {
            return WriteVertexColorScript(args);
        }

        var startupSmoke = args.Any(argument =>
            string.Equals(argument, StartupSmokeArgument, StringComparison.OrdinalIgnoreCase));

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = ProjectStore.GetToolPathSettings();
        UiTheme.ConfigureApplication(settings.UiTheme);

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
        OnlinePreparationFeature.Attach(form);
        ExtractionProgressFeature.Attach(form);
        ProjectLibraryFeature.Attach(form);
        HeroCatalogFeature.Attach(form);
        ProjectLogsFeature.Attach(form);
        ProjectSaveStateFeature.Attach(form);
        ProjectHeaderFeature.Attach(form);
        ProjectFilesFeature.Attach(form);
        VertexColorExportFeature.Attach(form);
        UiTheme.ApplyCustomPalette(form, settings.UiTheme);
        WindowProgressFeature.Attach(form);
        SteamStatusFeature.Attach(form, settings.UiTheme);
        SettingsVersionFeature.Attach();
        form.Shown += (_, _) => form.BeginInvoke((Action)(() => form.ActiveControl = null));

        if (!startupSmoke)
        {
            Application.Run(form);
            return 0;
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
        return 0;
    }

    private static int WriteVertexColorScript(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: Deadlimit.exe --write-vertex-color-script <folder>");
            return 64;
        }

        try
        {
            Console.Out.WriteLine(VertexColorMaxScriptService.WriteScript(args[1]));
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Failed: {ex.Message}");
            return 70;
        }
    }

    private static Icon LoadAppIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Deadlimit.AppIcon.ico")
            ?? throw new InvalidOperationException("Embedded Deadlimit application icon was not found.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
