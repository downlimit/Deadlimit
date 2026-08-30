using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private const string StartupSmokeArgument = "--startup-smoke";
    private const string WriteVertexColorScriptArgument = "--write-vertex-color-script";
    private const string SingleInstanceMutexName = @"Local\Deadlimit.Gui.SingleInstance.v1";
    private const int SwRestore = 9;

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

        Mutex? singleInstanceMutex = null;
        if (!startupSmoke)
        {
            singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                singleInstanceMutex.Dispose();
                TryActivateExistingWindow();
                return 0;
            }
        }

        try
        {
            return RunApplication(startupSmoke);
        }
        finally
        {
            if (singleInstanceMutex is not null)
            {
                singleInstanceMutex.ReleaseMutex();
                singleInstanceMutex.Dispose();
            }
        }
    }

    private static int RunApplication(bool startupSmoke)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = ProjectStore.GetToolPathSettings();
        UiTheme.ConfigureApplication(settings.UiTheme);

        using var startup = startupSmoke
            ? null
            : new StartupProgressForm(AppIcon, settings.UiTheme);
        if (startup is not null)
        {
            startup.Show();
            UpdateStartup(startup, 12, UiText.T("Loading settings...", "Загрузка настроек..."));
        }

        UpdateStartup(startup, 28, UiText.T("Building interface...", "Создание интерфейса..."));
        using var form = new MainForm
        {
            Icon = AppIcon,
            Size = MainWindowSize,
            MinimumSize = MainWindowSize,
            MaximumSize = MainWindowSize,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
        };

        UpdateStartup(startup, 46, UiText.T("Initializing CSDK actions...", "Инициализация действий CSDK..."));
        BuildFeature.Attach(form);
        OnlinePreparationFeature.Attach(form);
        ExtractionProgressFeature.Attach(form);

        UpdateStartup(startup, 62, UiText.T("Loading project controls...", "Загрузка элементов проекта..."));
        ProjectLibraryFeature.Attach(form);
        HeroCatalogFeature.Attach(form);
        ProjectLogsFeature.Attach(form);
        ProjectSaveStateFeature.Attach(form);

        UpdateStartup(startup, 78, UiText.T("Preparing project workspace...", "Подготовка рабочей области проекта..."));
        ProjectHeaderFeature.Attach(form);
        OnlineCsdkPulseFeature.Attach(form);
        ProjectFilesFeature.Attach(form);
        VertexColorExportFeature.Attach(form);

        UpdateStartup(startup, 90, UiText.T("Applying interface settings...", "Применение настроек интерфейса..."));
        UiTheme.ApplyCustomPalette(form, settings.UiTheme);
        WindowProgressFeature.Attach(form);
        SteamStatusFeature.Attach(form, settings.UiTheme);
        SettingsVersionFeature.Attach();
        UpdateStartup(startup, 94, UiText.T("Loading projects and finalizing...", "Загрузка проектов и завершение запуска..."));
        form.Shown += (_, _) =>
        {
            form.BeginInvoke((Action)(() => form.ActiveControl = null));
            if (startup is not null && !startup.IsDisposed)
            {
                startup.UpdateProgress(100, UiText.T("Ready", "Готово"));
                startup.Close();
            }
        };

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

    private static void UpdateStartup(StartupProgressForm? startup, int value, string message)
    {
        if (startup is null || startup.IsDisposed)
        {
            return;
        }

        startup.UpdateProgress(value, message);
        Application.DoEvents();
    }

    private static void TryActivateExistingWindow()
    {
        using var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id == currentProcess.Id || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                ShowWindow(process.MainWindowHandle, SwRestore);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    private static int WriteVertexColorScript(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: DeadlimitManager.exe --write-vertex-color-script <folder>");
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
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("DeadlimitManager.AppIcon.ico")
            ?? throw new InvalidOperationException("Embedded Deadlimit Manager application icon was not found.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
