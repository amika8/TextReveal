using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WpfRect = System.Windows.Rect;

namespace TextReveal.Ocr;

/// <summary>
/// Thin, local-only wrapper around the native Windows OCR engine
/// (<c>Windows.Media.Ocr</c>). Nothing leaves the machine.
/// </summary>
/// <remarks>
/// The engine reads print typography well and display typography badly: heavy, outlined lettering
/// laid over a photo - a video thumbnail, a poster, a game banner - is exactly what it misses, and so
/// is anything whose glyphs are shorter than about ten pixels. The screen is therefore read four
/// times, and the readings are merged: as it is (exact on ordinary interface text), three times
/// bigger and contrast-stretched (small labels and captions), reduced to its bright lettering, then
/// reduced to its warm lettering. Each pass costs a few hundred milliseconds on a full screen, so the
/// results are published as they come instead of at the very end.
/// </remarks>
internal sealed class OcrService
{
    /// <summary>Largest side the engine accepts: a bigger image is refused outright.</summary>
    private const int MaxImageDimension = 10_000;

    /// <summary>
    /// Pixel budget of an enlarged pass. Small interface text - a 9 px label, a caption on a
    /// thumbnail - is below the height the engine segments reliably, so the screen is enlarged as
    /// much as this allows: three times on a 1080p or a 1600p screen, twice on a 4K one where the
    /// side limit is already close.
    /// </summary>
    private const long EnlargedPixelBudget = 40_000_000;

    /// <summary>Brightness from which a pixel counts as bright lettering.</summary>
    private const byte BrightLettering = 215;

    /// <summary>Colour from which a pixel counts as yellow or white lettering.</summary>
    private const byte WarmRed = 190;
    private const byte WarmGreen = 170;

    /// <summary>
    /// The recognizer for the configured language. Written by the UI thread when the settings
    /// change, read by the background recognition thread: the field is volatile so a swap is
    /// always visible and never torn.
    /// </summary>
    private volatile OcrEngine? _engine;

    /// <summary>True when a recognizer is ready for the current language.</summary>
    public bool IsAvailable => _engine is not null;

    /// <summary>BCP-47 tag of the recognizer in use, for the diagnostic report.</summary>
    public string? LanguageTag => _engine?.RecognizerLanguage.LanguageTag;

    /// <summary>
    /// Selects the recognizer. A <c>null</c> or unknown tag falls back to the Windows profile languages.
    /// </summary>
    public void Configure(string? languageTag)
    {
        _engine = CreateEngine(languageTag);
    }

    /// <summary>Recognizes every word of the frozen screen, in physical pixel coordinates.</summary>
    /// <param name="image">The frozen screen, at capture size.</param>
    /// <param name="progress">
    /// Called after every pass with everything read so far, so the overlay can already highlight
    /// matches while the passes for display lettering are still running. Called on the recognition
    /// thread.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the overlay closes.</param>
    public async Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(
        BitmapSource image,
        Action<IReadOnlyList<RecognizedWord>>? progress,
        CancellationToken cancellationToken)
    {
        if (_engine is not { } engine)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        var frame = OcrImageEnhancer.Read(image);
        var luma = OcrImageEnhancer.Luma(frame);
        var factor = EnlargementFactor(frame.Width, frame.Height);

        var accepted = new List<RecognizedWord>();
        var nextLine = 0;

        // 1. The screen as it is: the only pass whose boxes match the frozen pixels exactly, and the
        //    most accurate one on the ordinary text of an interface, a document or a code editor.
        nextLine = await AddPassAsync(
            () => RecognizeOnceAsync(engine, image, scale: 1d, cancellationToken),
            minimumLength: 1,
            nextLine,
            keepOwnLines: true);

        // 2. The same screen, three times bigger and stretched. This is the pass small text comes
        //    from: below roughly ten pixels of glyph height the engine segments nothing and above it
        //    only fragments, while three times bigger the same strokes read as words.
        if (factor > 1)
        {
            nextLine = await AddPassAsync(
                () =>
                {
                    var enlarged = OcrImageEnhancer.Upscale(luma, frame.Width, frame.Height, factor);
                    return RecognizeOnceAsync(
                        engine,
                        OcrImageEnhancer.Contrast(enlarged),
                        frame.Width * factor,
                        frame.Height * factor,
                        factor,
                        cancellationToken);
                },
                minimumLength: 1,
                nextLine,
                keepOwnLines: false);
        }

        // 3. Bright lettering only: removes the photo behind it, and the dark outline the engine
        //    would otherwise read as a second glyph. At capture size: what it is meant to read -
        //    display lettering - is large by nature.
        nextLine = await AddPassAsync(
            () => RecognizeOnceAsync(engine, OcrImageEnhancer.BrightMask(luma, BrightLettering), frame.Width, frame.Height, scale: 1d, cancellationToken),
            minimumLength: 2,
            nextLine,
            keepOwnLines: false);

        // 4. Yellow and white lettering only: keeps whole words together where the brightness mask
        //    breaks them apart.
        nextLine = await AddPassAsync(
            () => RecognizeOnceAsync(engine, OcrImageEnhancer.ColorMask(frame, WarmRed, WarmGreen), frame.Width, frame.Height, scale: 1d, cancellationToken),
            minimumLength: 2,
            nextLine,
            keepOwnLines: false);

        return accepted;

        async Task<int> AddPassAsync(
            Func<Task<List<RecognizedWord>>> recognize,
            int minimumLength,
            int line,
            bool keepOwnLines)
        {
            var words = await recognize();
            var updated = OcrWordMerger.Merge(accepted, words, minimumLength, line, keepOwnLines);

            // Everything read so far: a first match shows up long before the last pass is done.
            progress?.Invoke(accepted.ToArray());
            return updated;
        }
    }

    /// <summary>
    /// How much the screen is enlarged for the pass that needs taller glyphs: three times when the
    /// side limit and the pixel budget both allow it, twice when only the budget does, once on a
    /// screen too large for either.
    /// </summary>
    private static int EnlargementFactor(int width, int height)
    {
        var pixels = (long)width * height;
        var longest = Math.Max(width, height);

        for (var factor = 3; factor > 1; factor--)
        {
            if ((long)factor * longest <= MaxImageDimension && pixels * factor * factor <= EnlargedPixelBudget)
            {
                return factor;
            }
        }

        return 1;
    }

    /// <summary>Runs one image through the engine, in capture pixel coordinates.</summary>
    private static async Task<List<RecognizedWord>> RecognizeOnceAsync(
        OcrEngine engine,
        BitmapSource image,
        double scale,
        CancellationToken cancellationToken)
    {
        using var bitmap = BitmapConverter.ToSoftwareBitmap(image);
        return await RecognizeAsync(engine, bitmap, scale, cancellationToken);
    }

    /// <summary>Runs one grey-level image through the engine, in capture pixel coordinates.</summary>
    private static async Task<List<RecognizedWord>> RecognizeOnceAsync(
        OcrEngine engine,
        byte[] grey,
        int width,
        int height,
        double scale,
        CancellationToken cancellationToken)
    {
        using var bitmap = BitmapConverter.ToSoftwareBitmap(grey, width, height);
        return await RecognizeAsync(engine, bitmap, scale, cancellationToken);
    }

    private static async Task<List<RecognizedWord>> RecognizeAsync(
        OcrEngine engine,
        SoftwareBitmap bitmap,
        double scale,
        CancellationToken cancellationToken)
    {
        var result = await engine.RecognizeAsync(bitmap);

        cancellationToken.ThrowIfCancellationRequested();

        return Map(result, scale);
    }

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(
                candidate => string.Equals(candidate.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase));

            if (language is not null && OcrEngine.TryCreateFromLanguage(language) is { } configuredEngine)
            {
                return configuredEngine;
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>
    /// Turns the engine result into words, dividing the boxes of a pass that ran on an enlarged
    /// image back to capture pixels so every pass speaks the same coordinates.
    /// </summary>
    private static List<RecognizedWord> Map(OcrResult result, double scale)
    {
        var words = new List<RecognizedWord>();

        var lineIndex = 0;
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                var bounds = word.BoundingRect;
                if (bounds.Width <= 0f || bounds.Height <= 0f || string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                words.Add(new RecognizedWord(
                    word.Text,
                    new WpfRect(bounds.X / scale, bounds.Y / scale, bounds.Width / scale, bounds.Height / scale),
                    lineIndex));
            }

            lineIndex++;
        }

        return words;
    }
}