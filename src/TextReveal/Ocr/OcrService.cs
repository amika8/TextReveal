using System.Windows.Media.Imaging;
using Windows.Media.Ocr;
using WpfRect = System.Windows.Rect;

namespace TextReveal.Ocr;

/// <summary>
/// Thin, local-only wrapper around the native Windows OCR engine
/// (<c>Windows.Media.Ocr</c>). Nothing leaves the machine.
/// </summary>
internal sealed class OcrService
{
    /// <summary>
    /// The recognizer for the configured language. Written by the UI thread when the settings
    /// change, read by the background recognition thread: the field is volatile so a swap is
    /// always visible and never torn.
    /// </summary>
    private volatile OcrEngine? _engine;

    /// <summary>True when a recognizer is ready for the current language.</summary>
    public bool IsAvailable => _engine is not null;

    /// <summary>
    /// Selects the recognizer. A <c>null</c> or unknown tag falls back to the Windows profile languages.
    /// </summary>
    public void Configure(string? languageTag)
    {
        _engine = CreateEngine(languageTag);
    }

    /// <summary>Recognizes every word of the frozen screen, in physical pixel coordinates.</summary>
    public async Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken)
    {
        if (_engine is not { } engine)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = BitmapConverter.ToSoftwareBitmap(image);
        var result = await engine.RecognizeAsync(bitmap);

        cancellationToken.ThrowIfCancellationRequested();

        return Map(result);
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

    private static List<RecognizedWord> Map(OcrResult result)
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
                    new WpfRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    lineIndex));
            }

            lineIndex++;
        }

        return words;
    }
}
