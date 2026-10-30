using Windows.Media.Ocr;

namespace TextReveal.Ocr;

/// <summary>
/// Lists the OCR languages installed on this Windows installation.
/// Read-only query of the local Windows OCR engine: nothing is downloaded.
/// </summary>
internal static class OcrLanguageCatalog
{
    /// <summary>Automatic mode first, then every installed recognizer sorted by name.</summary>
    public static IReadOnlyList<OcrLanguageOption> Load()
    {
        var options = new List<OcrLanguageOption>
        {
            new(null, "Automatic (Windows languages)"),
        };

        try
        {
            foreach (var language in OcrEngine.AvailableRecognizerLanguages.OrderBy(
                         static candidate => candidate.DisplayName,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                options.Add(new OcrLanguageOption(
                    language.LanguageTag,
                    $"{language.DisplayName} - {language.LanguageTag}"));
            }
        }
        catch (Exception)
        {
            // The OCR engine may be unavailable (missing language pack): keep automatic mode only.
        }

        return options;
    }
}
