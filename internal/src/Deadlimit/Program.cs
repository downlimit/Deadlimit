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
        try
        {
            WriteStartupDiagnostic("Main entered");

            var settings = ProjectStore.GetToolPathSettings();
            WriteStartupDiagnostic("Settings loaded");
            UiTheme.ConfigureApplication(settings.UiTheme);
            WriteStartupDiagnostic("Application theme configured");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            WriteStartupDiagnostic("WinForms configured");

            var form = new MainForm
            {
                Icon = AppIcon,
                Size = MainWindowSize,
                MinimumSize = MainWindowSize,
                MaximumSize = MainWindowSize,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
            };
            WriteStartupDiagnostic("MainForm constructed");

            BuildFeature.Attach(form);
            WriteStartupDiagnostic("BuildFeature attached");
            ProjectLibraryFeature.Attach(form);
            WriteStartupDiagnostic("ProjectLibraryFeature attached");
            HeroCatalogFeature.Attach(form);
            WriteStartupDiagnostic("HeroCatalogFeature attached");
            UiTheme.ApplyCustomPalette(form, settings.UiTheme);
            WriteStartupDiagnostic("Custom palette applied");

            form.Shown += (_, _) => form.BeginInvoke((Action)(() => form.ActiveControl = null));
            WriteStartupDiagnostic("Entering Application.Run");
            Application.Run(form);
            WriteStartupDiagnostic("Application.Run returned");
        }
        catch (Exception ex)
        {
            WriteStartupDiagnostic($"UNHANDLED STARTUP EXCEPTION: {ex}");
            throw;
        }
    }

    private static void WriteStartupDiagnostic(string message)
    {
        var path = Environment.GetEnvironmentVariable("DEADLIMIT_STARTUP_DIAGNOSTIC");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never change application startup behavior.
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
