using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Deadlimit.App;
using Deadlimit.Core;

namespace Deadlimit;

internal static class Program
{
    private const string StartupSmokeArgument = "--startup-smoke";
    private const string ReleasePolicySmokeArgument = "--release-policy-smoke";
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

        if (args.Any(argument =>
                string.Equals(argument, ReleasePolicySmokeArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return RunReleasePolicySmoke();
        }

        var startupSmoke = args.Any(argument =>
            string.Equals(argument, StartupSmokeArgument, StringComparison.OrdinalIgnoreCase));

        Mutex? singleInstanceMutex = null;
        var ownsSingleInstanceMutex = false;
        if (!startupSmoke)
        {
            singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
            try
            {
                // A short wait also makes any legacy Application.Restart call robust:
                // the replacement process can acquire the mutex as soon as the old UI exits.
                ownsSingleInstanceMutex = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                ownsSingleInstanceMutex = true;
            }

            if (!ownsSingleInstanceMutex)
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
                if (ownsSingleInstanceMutex)
                {
                    singleInstanceMutex.ReleaseMutex();
                }
                singleInstanceMutex.Dispose();
            }
        }
    }

    private static int RunReleasePolicySmoke()
    {
        var isPortable = ReleaseChannelPolicy.IsPortableRelease;
        if (!ReleaseChannelPolicy.AllowsUnverifiedToolchainAutomation)
        {
            return 2;
        }

        var expectedPortableDataRoot = Path.Combine(AppContext.BaseDirectory, "UserData");
        var usesPortableDataRoot = string.Equals(
            UserDataPaths.Root,
            expectedPortableDataRoot,
            StringComparison.OrdinalIgnoreCase);
        if (usesPortableDataRoot != isPortable)
        {
            return 4;
        }

        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        return 0;
    }

    private static int RunApplication(bool startupSmoke)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (startupSmoke)
        {
            var settingsLayoutResult = SettingsForm.RunFooterLayoutSmoke();
            if (settingsLayoutResult != 0)
            {
                return 20 + settingsLayoutResult;
            }
        }

        var settings = ProjectStore.GetToolPathSettings();
        DeadlockToolsLayoutMigration.TryMigrateManagedRelease(settings.DeadlockToolsRoot);
        UiTheme.ConfigureApplication(settings.UiTheme);

        using var startup = startupSmoke
            ? null
            : new StartupProgressForm(AppIcon, settings.UiTheme);
        if (startup is not null)
        {
            startup.Show();
            UpdateStartup(startup, 12, UiText.T("Loading settings...", "Загрузка настроек..."));
        }

        SettingsVersionFeature.Attach();
        SettingsToolchainProgressFeature.Attach();
        using var context = new DeadlimitApplicationContext(startupSmoke, startup);
        Application.Run(context);
        return 0;
    }

    private static MainForm CreateMainForm(StartupProgressForm? startup)
    {
        var settings = ProjectStore.GetToolPathSettings();
        UiTheme.ConfigureApplication(settings.UiTheme);

        UpdateStartup(startup, 28, UiText.T("Building interface...", "Создание интерфейса..."));
        var form = new MainForm
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
        ProjectLibraryHotfixFeature.Attach(form);
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
        UpdateStartup(startup, 94, UiText.T("Loading projects and finalizing...", "Загрузка проектов и завершение запуска..."));
        return form;
    }

    private sealed class DeadlimitApplicationContext : ApplicationContext
    {
        private readonly bool _startupSmoke;
        private StartupProgressForm? _startup;
        private bool _reloadPending;
        private bool _reloading;
        private System.Windows.Forms.Timer? _smokeTimer;

        public DeadlimitApplicationContext(bool startupSmoke, StartupProgressForm? startup)
        {
            _startupSmoke = startupSmoke;
            _startup = startup;
            UiSettingsChangeBus.Changed += OnUiSettingsChanged;
            Application.Idle += OnApplicationIdle;
            ShowMainForm(CreateMainForm(_startup), initial: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UiSettingsChangeBus.Changed -= OnUiSettingsChanged;
                Application.Idle -= OnApplicationIdle;
                _smokeTimer?.Stop();
                _smokeTimer?.Dispose();
                _smokeTimer = null;
            }
            base.Dispose(disposing);
        }

        private void OnUiSettingsChanged(object? sender, EventArgs e)
        {
            _reloadPending = true;
        }

        private void OnApplicationIdle(object? sender, EventArgs e)
        {
            if (!_reloadPending || _reloading)
            {
                return;
            }

            // Settings owns a modal loop. Wait until it has actually closed before
            // replacing its owner, otherwise WinForms can tear down the modal chain.
            if (Application.OpenForms.OfType<SettingsForm>().Any())
            {
                return;
            }

            ReloadMainForm();
        }

        private void ReloadMainForm()
        {
            var previous = MainForm;
            if (previous is null || previous.IsDisposed)
            {
                _reloadPending = false;
                return;
            }

            _reloading = true;
            _reloadPending = false;
            try
            {
                var location = previous.Location;
                var wasVisible = previous.Visible;

                // Detach ApplicationContext from the old main form before closing it,
                // so closing the form does not terminate the message loop.
                MainForm = null;
                previous.Hide();
                previous.Close();
                previous.Dispose();

                var replacement = CreateMainForm(startup: null);
                replacement.StartPosition = FormStartPosition.Manual;
                replacement.Location = location;
                ShowMainForm(replacement, initial: false, show: wasVisible);
            }
            finally
            {
                _reloading = false;
            }
        }

        private void ShowMainForm(MainForm form, bool initial, bool show = true)
        {
            MainForm = form;
            form.Shown += (_, _) =>
            {
                form.BeginInvoke((Action)(() => form.ActiveControl = null));
                if (initial && _startup is not null && !_startup.IsDisposed)
                {
                    _startup.UpdateProgress(100, UiText.T("Ready", "Готово"));
                    _startup.Close();
                    _startup = null;
                }
            };

            if (_startupSmoke)
            {
                _smokeTimer?.Dispose();
                _smokeTimer = new System.Windows.Forms.Timer
                {
                    Interval = 500,
                };
                _smokeTimer.Tick += (_, _) =>
                {
                    _smokeTimer!.Stop();
                    if (MainForm is not null && !MainForm.IsDisposed)
                    {
                        MainForm.Close();
                    }
                };
                form.Shown += (_, _) => _smokeTimer.Start();
            }

            if (show)
            {
                form.Show();
            }
        }
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
            Console.Out.WriteLine(DeadlimitScriptsService.WriteScript(args[1]));
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
