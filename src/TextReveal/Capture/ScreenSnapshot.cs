using System.Windows;
using System.Windows.Media.Imaging;

namespace TextReveal.Capture;

/// <summary>
/// A frozen copy of the screen: the pixels shown to the user plus the geometry
/// needed to position the overlay and map OCR coordinates.
/// </summary>
/// <param name="Image">Frozen bitmap of the whole monitor (physical pixels).</param>
/// <param name="Bounds">Monitor bounds in physical screen pixels.</param>
/// <param name="DpiScale">Monitor scale factor (1.0 = 96 DPI, 1.5 = 144 DPI).</param>
internal sealed record ScreenSnapshot(BitmapSource Image, Int32Rect Bounds, double DpiScale)
{
    public int PixelWidth => Image.PixelWidth;

    public int PixelHeight => Image.PixelHeight;
}
