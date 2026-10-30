using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TextReveal.Ui;

/// <summary>
/// Loads the window icon from the embedded <c>.ico</c>. It is read in code rather than with an
/// <c>Icon</c> attribute because the file holds nine frames: the decoder picks the right one for the
/// title bar, while the XAML type converter gives up on a multi-frame icon.
/// </summary>
internal static class AppIcon
{
    /// <summary>
    /// Size of the frame handed to the windows: big enough for a 150 % title bar, small enough to
    /// stay crisp instead of being an upscaled 16 px drawing.
    /// </summary>
    private const int PreferredFrameSize = 48;

    /// <summary>The application icon, or <c>null</c> when the resource cannot be decoded.</summary>
    public static BitmapFrame? Load()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("Assets/TextReveal.ico", UriKind.Relative));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;

            var decoder = new IconBitmapDecoder(
                stream,
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            return PickFrame(decoder.Frames);
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or ArgumentException
            or FormatException)
        {
            // An icon-less window is better than a window that never opens.
            return null;
        }
    }

    /// <summary>
    /// The smallest frame at least <see cref="PreferredFrameSize"/> pixels wide, or the largest frame
    /// of the file when every frame is smaller than that.
    /// </summary>
    private static BitmapFrame? PickFrame(IEnumerable<BitmapFrame> frames)
    {
        BitmapFrame? preferred = null;
        BitmapFrame? largest = null;

        foreach (var frame in frames)
        {
            if (largest is null || frame.PixelWidth > largest.PixelWidth)
            {
                largest = frame;
            }

            if (frame.PixelWidth >= PreferredFrameSize
                && (preferred is null || frame.PixelWidth < preferred.PixelWidth))
            {
                preferred = frame;
            }
        }

        return preferred ?? largest;
    }
}
