using System.IO;
using System.Windows.Threading;
using TextReveal.Shared;

namespace TextReveal.Services;

/// <summary>
/// Watches <c>config.json</c> so a shortcut change made in the settings window
/// applies immediately, without restarting the application.
/// </summary>
internal sealed class ConfigWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _debounceTimer;
    private readonly FileSystemWatcher? _watcher;
    private bool _disposed;

    public ConfigWatcher()
    {
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebounceDelay };
        _debounceTimer.Tick += OnDebounceElapsed;

        _watcher = TryCreateWatcher();
    }

    /// <summary>Raised on the UI thread with the freshly loaded configuration.</summary>
    public event EventHandler<AppConfig>? ConfigurationChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceElapsed;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
        }
    }

    /// <summary>
    /// Creates the watcher once the configuration folder exists. A missing or read-only
    /// <c>%LOCALAPPDATA%</c> returns <c>null</c>: the live reload is skipped, never the startup.
    /// </summary>
    private FileSystemWatcher? TryCreateWatcher()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DirectoryPath);

            var watcher = new FileSystemWatcher(ConfigStore.DirectoryPath)
            {
                Filter = Path.GetFileName(ConfigStore.FilePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = false,
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.EnableRaisingEvents = true;

            return watcher;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Several events arrive for one write: restart the timer from the UI thread.
        _debounceTimer.Dispatcher.InvokeAsync(
            () =>
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            },
            DispatcherPriority.Background);
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        ConfigurationChanged?.Invoke(this, ConfigStore.Load());
    }
}
