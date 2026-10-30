using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextReveal.Capture;
using TextReveal.Interop;
using TextReveal.Ocr;
using TextReveal.Services;
using TextReveal.Shared;
using TextReveal.Tray;
using TextReveal.Ui;

namespace TextReveal;

/// <summary>
/// Composition root of the application: one global shortcut, one tray icon, one overlay and one
/// settings window - a single process, a single executable.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WPF Application disposes its services in OnExit: that is the framework's disposal point.")]
public partial class App : Application
{
    /// <summary>Command-line switch run by the "apps and features" uninstall entry.</summary>
    private const string UninstallSwitch = "--uninstall";

    /// <summary>Silent install with the defaults of the install page, for scripts.</summary>
    private const string InstallSwitch = "--install";

    private static readonly HotKeyDefinition[] FallbackHotKeys =
    [
        new(ModifierKeys.Control | ModifierKeys.Shift, Key.F),
        new(ModifierKeys.Alt | ModifierKeys.Shift, Key.F),
        new(ModifierKeys.Windows, Key.F),
    ];

    private readonly OcrService _ocr = new();

    private SingleInstance? _instance;
    private HotKeyManager? _hotKeys;
    private TrayIcon? _tray;
    private ConfigWatcher? _configWatcher;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settings;
    private AppConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A resident tray app must survive a bad OCR pass or a busy clipboard: log, tell the
        // user, and keep the shortcut alive instead of vanishing without a trace.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Uninstalling must work even while another copy sits in the notification area: no window,
        // no shortcut, no state to keep.
        if (IsSwitch(e.Args, UninstallSwitch))
        {
            Uninstall();
            Shutdown();
            return;
        }

        // Same for a scripted install: no window, the defaults of the install page, then exit.
        if (IsSwitch(e.Args, InstallSwitch))
        {
            InstallUnattended();
            Shutdown();
            return;
        }

        if (SingleInstance.TryAcquire() is not { } instance)
        {
            // Already running: ask that instance to show its window, then step aside.
            SingleInstance.SignalRunningInstance();
            Shutdown();
            return;
        }

        _instance = instance;

        // Without config.json there is no known shortcut yet: show the window on the first run.
        var isFirstRun = !File.Exists(ConfigStore.FilePath);

        _config = ConfigStore.Load();
        _ocr.Configure(_config.OcrLanguage);

        // A downloaded copy asks once whether it should move into the user profile and create its
        // shortcuts. Only an actual install counts as an answer: cancelling installs nothing, so
        // the page comes back on the next start.
        if (!_config.InstallPromptHandled && !Installer.IsRunningInstalled)
        {
            var dialog = new InstallWindow();
            dialog.ShowDialog();

            if (dialog.Installed)
            {
                _config = _config with { InstallPromptHandled = true };
                ConfigStore.TrySave(_config);
            }

            if (dialog.RestartRequired)
            {
                // The installed copy takes over from here.
                RelaunchInstalledCopy();
                return;
            }
        }

        _hotKeys = new HotKeyManager();
        _hotKeys.Pressed += (_, _) => OpenOverlay();

        _tray = new TrayIcon(OpenOverlay, OpenSettings, OpenInstallWindow, ExitApplication);

        RegisterConfiguredHotKey(_config.HotKey);

        _configWatcher = new ConfigWatcher();
        _configWatcher.ConfigurationChanged += OnConfigurationChanged;

        // Starting the executable again must do something visible instead of nothing.
        instance.ListenForActivation(
            () => Dispatcher.BeginInvoke(OpenSettings),
            () => Dispatcher.BeginInvoke(ExitApplication));

        if (isFirstRun)
        {
            OpenSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _configWatcher?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>Logs an exception raised on the UI thread and keeps the application alive.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception);
        e.Handled = true;

        // Windows are the only stateful pieces: drop them rather than leave a half-built UI.
        CloseOverlay();

        if (_settings is { } settings)
        {
            _settings = null;
            TryClose(settings);
        }

        _tray?.ShowError(
            "TextReveal recovered from an unexpected error and keeps running. " +
            $"Details: {ErrorLog.FilePath}");
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        ErrorLog.Write(e.ExceptionObject as Exception);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception);
        e.SetObserved();
    }

    private static void TryClose(Window window)
    {
        try
        {
            window.Close();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Closing a window in a broken state must not raise another failure.
        }
    }

    /// <summary>Freezes the screen, shows the overlay and starts OCR.</summary>
    private void OpenOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.Activate();
            return;
        }

        var snapshot = ScreenCapture.CaptureAtCursor();
        if (snapshot is null)
        {
            _tray?.ShowError("Screen capture failed.");
            return;
        }

        var overlay = new OverlayWindow(snapshot, _ocr);
        overlay.Closed += (_, _) => _overlay = null;
        _overlay = overlay;

        overlay.Show();
        overlay.FocusSearch();
        overlay.StartRecognition();
    }

    private void RegisterConfiguredHotKey(HotKeyDefinition hotKey)
    {
        if (_hotKeys is null)
        {
            return;
        }

        if (_hotKeys.TryRegister(hotKey))
        {
            _tray?.SetHotKeyLabel(hotKey.DisplayText);
            return;
        }

        foreach (var fallback in FallbackHotKeys)
        {
            if (fallback == hotKey || !_hotKeys.TryRegister(fallback))
            {
                continue;
            }

            _tray?.SetHotKeyLabel(fallback.DisplayText);
            _tray?.ShowError(
                $"Shortcut {hotKey.DisplayText} is already taken by Windows or another application. " +
                $"Fallback for this session: {fallback.DisplayText}. Change it in the settings window.");
            return;
        }

        _tray?.SetHotKeyLabel("unavailable");
        _tray?.ShowError(
            "No global shortcut could be registered. " +
            "Pick another combination in the settings window.");
    }

    private void OnConfigurationChanged(object? sender, AppConfig config)
    {
        var previous = _config;
        _config = config;

        if (config.HotKey != previous.HotKey && _hotKeys is not null)
        {
            var active = _hotKeys.Current;

            if (_hotKeys.TryRegister(config.HotKey))
            {
                _tray?.SetHotKeyLabel(config.HotKey.DisplayText);
            }
            else
            {
                // Keep the shortcut that still works for this session.
                if (active is { } fallback)
                {
                    _hotKeys.TryRegister(fallback);
                }

                _tray?.ShowError(
                    $"Shortcut {config.HotKey.DisplayText} was not registered (already in use?). " +
                    $"TextReveal keeps {_hotKeys.Current?.DisplayText ?? "the previous shortcut"}.");

                // Stay coherent with what is really registered, so an unrelated settings change
                // never retries the refused combination.
                _config = config with { HotKey = _hotKeys.Current ?? previous.HotKey };
            }
        }

        if (!string.Equals(config.OcrLanguage, previous.OcrLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _ocr.Configure(config.OcrLanguage);
        }
    }

    /// <summary>Shows the settings window, creating it on first use.</summary>
    private void OpenSettings()
    {
        if (_settings is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        // A settings dialog makes no sense on top of a frozen screen.
        CloseOverlay();

        var window = new SettingsWindow();
        window.Closed += (_, _) => _settings = null;
        _settings = window;

        window.Show();
        window.Activate();
    }

    /// <summary>Closes the overlay if one is open; never throws.</summary>
    private void CloseOverlay()
    {
        if (_overlay is not { } overlay)
        {
            return;
        }

        _overlay = null;
        TryClose(overlay);
    }

    private void ExitApplication()
    {
        _overlay?.Close();
        Shutdown();
    }

    /// <summary>
    /// Opens the install window from the tray: shortcuts can always be recreated, and installing
    /// later is one click away for someone who cancelled the page.
    /// </summary>
    private void OpenInstallWindow()
    {
        // An install window behind a frozen screen would be invisible.
        CloseOverlay();

        var dialog = new InstallWindow();
        dialog.ShowDialog();

        if (dialog.Installed)
        {
            // Same rule as the first-run page: an install answers the question for good.
            _config = _config with { InstallPromptHandled = true };
            ConfigStore.TrySave(_config);
        }

        if (dialog.RestartRequired)
        {
            RelaunchInstalledCopy();
        }
    }

    /// <summary>
    /// Removes the installed copy: shortcuts, startup entry, uninstall entry and folder. Run by
    /// <c>TextReveal.exe --uninstall</c>, which is what Windows settings starts.
    /// </summary>
    private static void Uninstall()
    {
        // The copy in the notification area owns the executable: it has to go first.
        SingleInstance.SignalRunningInstanceQuit();

        if (!Installer.TryUninstall(out var error))
        {
            MessageBox.Show(
                $"{error}{Environment.NewLine}{Environment.NewLine}" +
                "Close TextReveal and try again.",
                "TextReveal",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Installs without a window, with the defaults of the install page: folder, both shortcuts,
    /// startup entry. Meant for scripts, and for a user who already answered once.
    /// </summary>
    private static void InstallUnattended()
    {
        // A copy started from the install folder may still hold the executable.
        SingleInstance.SignalRunningInstanceQuit();
        Thread.Sleep(500);

        var options = new InstallOptions(DesktopShortcut: true, StartMenuShortcut: true, StartWithWindows: true);

        if (!Installer.TryInstall(options, out var report))
        {
            MessageBox.Show(report, "TextReveal", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (report.Length > 0)
        {
            MessageBox.Show(report, "TextReveal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Hands the session over to the freshly installed copy.</summary>
    private void RelaunchInstalledCopy()
    {
        // Release the instance lock first: the installed copy has to become the single instance.
        var instance = _instance;
        _instance = null;
        instance?.Dispose();

        _configWatcher?.Dispose();
        _configWatcher = null;
        _hotKeys?.Dispose();
        _hotKeys = null;
        _tray?.Dispose();
        _tray = null;

        if (!Installer.TryStartInstalled(out var error))
        {
            MessageBox.Show(error, "TextReveal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Shutdown();
    }

    private static bool IsSwitch(IEnumerable<string> arguments, string expected) =>
        arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
}

