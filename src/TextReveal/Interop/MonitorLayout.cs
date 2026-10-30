using System.Windows;

namespace TextReveal.Interop;

/// <summary>Bounds (physical pixels) and scale factor of the monitor under the cursor.</summary>
internal readonly record struct MonitorTarget(Int32Rect Bounds, double DpiScale);

/// <summary>Resolves the monitor to freeze, in raw screen pixels.</summary>
internal static class MonitorLayout
{
    private const double DefaultDpi = 96d;

    /// <summary>Returns the monitor containing the mouse cursor.</summary>
    public static MonitorTarget? FromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return null;
        }

        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var bounds = new Int32Rect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Width,
            info.Monitor.Height);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        return new MonitorTarget(bounds, GetScale(monitor));
    }

    private static double GetScale(nint monitor)
    {
        // GetDpiForMonitor is HRESULT based: S_OK (0) means the values are valid.
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
        {
            return dpiX / DefaultDpi;
        }

        return 1d;
    }
}
