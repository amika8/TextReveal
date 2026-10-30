using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TextReveal.Ocr;

/// <summary>
/// The pixel work every OCR pass needs. Windows OCR is trained on print fonts: heavy poster or
/// thumbnail lettering, outlined and sitting on a photo, and glyphs only a few pixels tall are what
/// it misses first. The same screen is therefore also handed to it upscaled, contrast-stretched, and
/// reduced to its bright lettering only, and the results are merged. Everything here is plain managed
/// code on the recognition thread - no encoder, no temporary file, no dependency - and every buffer is
/// one byte per pixel: at three times a full screen the passes would otherwise cost gigabytes.
/// </summary>
internal static class OcrImageEnhancer
{
    private const int BytesPerPixel = 4;

    /// <summary>The screen, copied once into Bgra32, which is the layout every helper expects.</summary>
    public readonly record struct Frame(byte[] Pixels, int Width, int Height)
    {
        public int PixelCount => Width * Height;
    }

    /// <summary>Copies a captured bitmap into plain pixels.</summary>
    public static Frame Read(BitmapSource source)
    {
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * BytesPerPixel;
        var pixels = new byte[stride * height];

        bitmap.CopyPixels(pixels, stride, offset: 0);

        return new Frame(pixels, width, height);
    }

    /// <summary>Luminance of every pixel, one byte each: the engine only needs the ink, not the colour.</summary>
    public static byte[] Luma(Frame frame)
    {
        var pixels = frame.Pixels;
        var luma = new byte[frame.PixelCount];

        for (var index = 0; index < luma.Length; index++)
        {
            var offset = index * BytesPerPixel;

            // ITU-R BT.601 weights in integers: the inner loop stays integer-only.
            luma[index] = (byte)((pixels[offset] * 114 + pixels[offset + 1] * 587 + pixels[offset + 2] * 299) / 1000);
        }

        return luma;
    }

    /// <summary>
    /// Enlarges a luminance map with a Catmull-Rom kernel. Small lettering is where the engine
    /// gains the most: at three times its size, counters and outlined strokes become separable.
    /// </summary>
    /// <remarks>
    /// The horizontal samples are kept in bytes rather than floats: at three times a full screen the
    /// float buffer alone would be several tens of megabytes, for a difference no recognizer sees.
    /// </remarks>
    public static byte[] Upscale(byte[] luma, int width, int height, int factor)
    {
        if (factor <= 1)
        {
            return luma;
        }

        var targetWidth = width * factor;
        var targetHeight = height * factor;
        var horizontal = new byte[targetWidth * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;

            for (var x = 0; x < targetWidth; x++)
            {
                // Destination pixel centre, expressed in source coordinates.
                var position = ((x + 0.5f) / factor) - 0.5f;
                var left = (int)MathF.Floor(position);
                var fraction = position - left;

                var weight0 = Cubic(1f + fraction);
                var weight1 = Cubic(fraction);
                var weight2 = Cubic(1f - fraction);
                var weight3 = Cubic(2f - fraction);
                var total = weight0 + weight1 + weight2 + weight3;

                var value = ((weight0 * SampleRow(luma, row, left - 1, width))
                    + (weight1 * SampleRow(luma, row, left, width))
                    + (weight2 * SampleRow(luma, row, left + 1, width))
                    + (weight3 * SampleRow(luma, row, left + 2, width))) / total;

                horizontal[(y * targetWidth) + x] = (byte)Math.Clamp(value, 0f, 255f);
            }
        }

        var target = new byte[targetWidth * targetHeight];

        for (var y = 0; y < targetHeight; y++)
        {
            var position = ((y + 0.5f) / factor) - 0.5f;
            var top = (int)MathF.Floor(position);
            var fraction = position - top;

            var weight0 = Cubic(1f + fraction);
            var weight1 = Cubic(fraction);
            var weight2 = Cubic(1f - fraction);
            var weight3 = Cubic(2f - fraction);
            var total = weight0 + weight1 + weight2 + weight3;

            for (var x = 0; x < targetWidth; x++)
            {
                var value = ((weight0 * SampleColumn(horizontal, top - 1, x, targetWidth, height))
                    + (weight1 * SampleColumn(horizontal, top, x, targetWidth, height))
                    + (weight2 * SampleColumn(horizontal, top + 1, x, targetWidth, height))
                    + (weight3 * SampleColumn(horizontal, top + 2, x, targetWidth, height))) / total;

                target[(y * targetWidth) + x] = (byte)Math.Clamp(value, 0f, 255f);
            }
        }

        return target;
    }

    /// <summary>One sample of a row, clamped at both ends so no border handling is needed.</summary>
    private static float SampleRow(byte[] source, int row, int column, int stride)
    {
        return source[row + Math.Clamp(column, 0, stride - 1)];
    }

    /// <summary>One sample of a column, clamped at both ends.</summary>
    private static float SampleColumn(byte[] source, int row, int column, int stride, int rows)
    {
        return source[(Math.Clamp(row, 0, rows - 1) * stride) + Math.Clamp(column, 0, stride - 1)];
    }

    /// <summary>Catmull-Rom kernel, the usual choice for a sharp two-times enlargement of text.</summary>
    private static float Cubic(float distance) => distance < 1f
        ? (((1.5f * distance) - 2.5f) * distance * distance) + 1f
        : ((((-0.5f * distance) + 2.5f) * distance) - 4f) * distance + 2f;

    /// <summary>
    /// Grey with a 2% - 98% stretch: a photo behind the text has a narrow histogram, and the engine
    /// reads strokes better once the ink spans the whole range.
    /// </summary>
    public static byte[] Contrast(byte[] luma)
    {
        var histogram = new int[256];
        foreach (var value in luma)
        {
            histogram[value]++;
        }

        var low = Percentile(histogram, luma.Length, 0.02d);
        var high = Percentile(histogram, luma.Length, 0.98d);

        if (high <= low + 8)
        {
            // An almost flat image has nothing to stretch: leave it alone rather than amplify noise.
            low = 0;
            high = 255;
        }

        var span = high - low;
        var stretched = new byte[luma.Length];

        for (var index = 0; index < luma.Length; index++)
        {
            stretched[index] = (byte)Math.Clamp(((luma[index] - low) * 255) / span, 0, 255);
        }

        return stretched;
    }

    /// <summary>
    /// Keeps the bright ink only, as black strokes on white: white and yellow lettering with a dark
    /// outline becomes plain text again, and the outline - which the engine reads as a second
    /// glyph - disappears with the background.
    /// </summary>
    public static byte[] BrightMask(byte[] luma, byte threshold)
    {
        var mask = new byte[luma.Length];

        for (var index = 0; index < luma.Length; index++)
        {
            mask[index] = luma[index] >= threshold ? (byte)0 : (byte)255;
        }

        return mask;
    }

    /// <summary>
    /// Same idea on the colour channels: yellow and white lettering is far brighter than the grass,
    /// the sky or a face it is written over, which keeps whole words the grey mask breaks apart.
    /// </summary>
    public static byte[] ColorMask(Frame frame, byte redMinimum, byte greenMinimum)
    {
        var pixels = frame.Pixels;
        var mask = new byte[frame.PixelCount];

        for (var index = 0; index < frame.PixelCount; index++)
        {
            var offset = index * BytesPerPixel;
            mask[index] = pixels[offset + 2] >= redMinimum && pixels[offset + 1] >= greenMinimum
                ? (byte)0
                : (byte)255;
        }

        return mask;
    }

    /// <summary>Brightness under which the given share of the pixels sits.</summary>
    private static int Percentile(int[] histogram, int total, double share)
    {
        var target = total * share;
        var seen = 0d;

        for (var value = 0; value < histogram.Length; value++)
        {
            seen += histogram[value];

            if (seen >= target)
            {
                return value;
            }
        }

        return 255;
    }
}
