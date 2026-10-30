using System.Windows.Interop;
using TextReveal.Shared;

namespace TextReveal.Interop;

/// <summary>
/// Registers the global shortcut on a hidden window and raises <see cref="Pressed"/>.
/// </summary>
internal sealed class HotKeyManager : IDisposable
{
    private const int HotKeyId = 0x5452; // 'TR'

    private readonly HwndSource _sink;
    private bool _registered;
    private bool _disposed;

    public HotKeyManager()
    {
        var parameters = new HwndSourceParameters("TextReveal.HotKeySink")
        {
            WindowStyle = NativeMethods.WsPopup,
            ExtendedWindowStyle = NativeMethods.WsExToolWindow,
            Width = 0,
            Height = 0,
        };

        _sink = new HwndSource(parameters);
        _sink.AddHook(OnWindowMessage);
    }

    /// <summary>Raised on the UI thread each time the shortcut is triggered.</summary>
    public event EventHandler? Pressed;

    /// <summary>The shortcut currently owned by the application, if any.</summary>
    public HotKeyDefinition? Current { get; private set; }

    /// <summary>Replaces the current shortcut. Returns <c>false</c> when Windows refuses it.</summary>
    public bool TryRegister(HotKeyDefinition hotKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unregister();

        if (!hotKey.IsValid)
        {
            return false;
        }

        if (!NativeMethods.RegisterHotKey(_sink.Handle, HotKeyId, hotKey.Win32Modifiers, hotKey.VirtualKey))
        {
            return false;
        }

        _registered = true;
        Current = hotKey;
        return true;
    }

    /// <summary>Releases the shortcut so another process can take it.</summary>
    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_sink.Handle, HotKeyId);
        _registered = false;
        Current = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _sink.RemoveHook(OnWindowMessage);
        _sink.Dispose();
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotKey && wParam == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }
}
