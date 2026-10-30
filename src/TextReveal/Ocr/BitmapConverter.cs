using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace TextReveal.Ocr;

/// <summary>Converts captured pixels to the WinRT bitmap expected by <c>Windows.Media.Ocr</c>.</summary>
internal static class BitmapConverter
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Copies the image into a <see cref="SoftwareBitmap"/> in Bgra8/Premultiplied,
    /// the only format accepted by <see cref="Windows.Media.Ocr.OcrEngine"/>.
    /// No encoding, no temporary file: a single memory copy.
    /// </summary>
    public static SoftwareBitmap ToSoftwareBitmap(BitmapSource source)
    {
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * BytesPerPixel;
        var pixels = new byte[stride * height];

        bitmap.CopyPixels(pixels, stride, offset: 0);

        var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }
}
