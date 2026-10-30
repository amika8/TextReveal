using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using TextReveal.Interop;

namespace TextReveal.Capture;

/// <summary>Grabs the monitor under the cursor with <see cref="Graphics.CopyFromScreen"/>.</summary>
internal static class ScreenCapture
{
    private const double DefaultDpi = 96d;

    /// <summary>Captures the monitor under the mouse cursor, or <c>null</c> on failure.</summary>
    public static ScreenSnapshot? CaptureAtCursor()
    {
        if (MonitorLayout.FromCursor() is not { } target)
        {
            return null;
        }

        var bounds = target.Bounds;

        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.X,
                    bounds.Y,
                    0,
                    0,
                    new Size(bounds.Width, bounds.Height),
                    CopyPixelOperation.SourceCopy);
            }

            var dpi = DefaultDpi * target.DpiScale;
            return new ScreenSnapshot(ToBitmapSource(bitmap, dpi), bounds, target.DpiScale);
        }
        catch (Exception exception) when (exception is ExternalException or OutOfMemoryException or ArgumentException)
        {
            return null;
        }
    }

    // Bgr32 on purpose: GDI leaves the alpha channel at zero, Bgr32 tells WPF to ignore it.
    private static BitmapSource ToBitmapSource(Bitmap bitmap, double dpi)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

        try
        {
            var source = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                dpi,
                dpi,
                System.Windows.Media.PixelFormats.Bgr32,
                palette: null,
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);

            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
