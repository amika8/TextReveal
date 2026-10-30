using System.IO;

namespace TextReveal.Services;

/// <summary>
/// Keeps a single instance in memory - a second one would only fight for the shortcut - and lets a
/// second launch ask the running instance to show its window instead of vanishing silently.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\TextReveal.Instance";
    private const string ActivationEventName = @"Local\TextReveal.Activate";
    private const string QuitEventName = @"Local\TextReveal.Quit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _quit;
    private readonly CancellationTokenSource _stopping = new();

    private Thread? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activation, EventWaitHandle quit)
    {
        _mutex = mutex;
        _activation = activation;
        _quit = quit;
    }

    /// <summary>Acquires the instance lock, or returns <c>null</c> when TextReveal already runs.</summary>
    public static SingleInstance? TryAcquire()
    {
        Mutex mutex;
        EventWaitHandle activation;
        EventWaitHandle quit;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName);
            activation = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivationEventName, out _);
            quit = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, QuitEventName, out _);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // The lock cannot be created (restricted session): better to stay quiet than to crash.
            return null;
        }

        try
        {
            // WaitOne(0) instead of the constructor flag: when a previous instance died without
            // releasing the lock, the wait reports it as abandoned and hands ownership to us.
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                activation.Dispose();
                quit.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed: the lock is ours now.
        }

        return new SingleInstance(mutex, activation, quit);
    }

    /// <summary>
    /// Called by the process that lost the lock: double-clicking TextReveal.exe a second time must
    /// do something visible, so the running instance is asked to show its settings window.
    /// </summary>
    public static void SignalRunningInstance() => Signal(ActivationEventName);

    /// <summary>
    /// Asks the running instance to quit, before the installed copy is replaced or removed:
    /// Windows refuses to overwrite or delete an executable that is still open.
    /// </summary>
    public static void SignalRunningInstanceQuit() => Signal(QuitEventName);

    private static void Signal(string eventName)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or WaitHandleCannotBeOpenedException)
        {
            // Nothing to wake up (the other instance is already exiting): stay silent.
        }
    }

    /// <summary>
    /// Waits on a background thread for a second launch or for a quit request.
    /// <paramref name="onActivated"/> and <paramref name="onQuit"/> run on that thread: the caller
    /// marshals them to the UI thread.
    /// </summary>
    public void ListenForActivation(Action onActivated, Action onQuit)
    {
        ArgumentNullException.ThrowIfNull(onActivated);
        ArgumentNullException.ThrowIfNull(onQuit);

        WaitHandle[] signals = [_activation, _quit];

        _listener = new Thread(() =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                var signalled = WaitHandle.WaitAny(signals, TimeSpan.FromMilliseconds(250));
                if (signalled == WaitHandle.WaitTimeout || _stopping.IsCancellationRequested)
                {
                    continue;
                }

                if (signalled == 1)
                {
                    onQuit();
                    continue;
                }

                onActivated();
            }
        })
        {
            IsBackground = true,
            Name = "TextReveal activation",
        };

        _listener.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _stopping.Cancel();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _stopping.Dispose();
        _activation.Dispose();
        _quit.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The mutex was already released (e.g. abandoned thread): nothing left to do.
        }

        _mutex.Dispose();
    }
}
