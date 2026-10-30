using System.Runtime.InteropServices;

namespace TextReveal.Interop;

/// <summary>
/// The handful of Win32 entry points TextReveal needs. Everything else is WPF/WinRT.
/// </summary>
internal static partial class NativeMethods
{
    internal const int MonitorDefaultToNearest = 0x00000002;

    internal const int WmHotKey = 0x0312;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;

    internal const int MdtEffectiveDpi = 0;

    /// <summary>WS_POPUP, used for the invisible hotkey sink window.</summary>
    internal const int WsPopup = unchecked((int)0x80000000);

    /// <summary>WS_EX_TOOLWINDOW, keeps the sink window out of Alt+Tab.</summary>
    internal const int WsExToolWindow = 0x00000080;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out Point32 point);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint MonitorFromPoint(Point32 point, int flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point32
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect32
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal Rect32 Monitor;
        internal Rect32 Work;
        internal uint Flags;
    }
}
