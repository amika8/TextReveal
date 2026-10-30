namespace TextReveal.Ocr;

/// <summary>
/// Relative glyph widths used to place a match inside its word rectangle. Windows OCR reports a
/// single rectangle per word, so a partial match ("zur" inside "Zürich") is positioned by summing
/// letter widths: exact on monospace text, accurate to a fraction of a character on proportional
/// text, and completely allocation free.
/// </summary>
internal static class GlyphWidths
{
    private const double Narrow = 0.30d;
    private const double Slim = 0.45d;
    private const double Regular = 0.62d;
    private const double Wide = 1.05d;

    /// <summary>Returns the match span as <c>[start, end]</c> fractions of the word width.</summary>
    public static (double Start, double End) MeasureSpan(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return (0d, 0d);
        }

        var from = Math.Clamp(start, 0, text.Length);
        var to = Math.Clamp(start + length, from, text.Length);

        var total = 0d;
        var before = 0d;
        var matched = 0d;

        for (var index = 0; index < text.Length; index++)
        {
            var width = WidthOf(text[index]);
            total += width;

            if (index < from)
            {
                before += width;
            }
            else if (index < to)
            {
                matched += width;
            }
        }

        return total <= 0d
            ? (0d, 1d)
            : (before / total, (before + matched) / total);
    }

    private static double WidthOf(char character) => character switch
    {
        ' ' or '\u00A0' => Narrow,
        'i' or 'l' or 'j' or 'I' or '.' or ',' or ';' or ':' or '!' or '|' or '\'' or '`' => Narrow,
        'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' or '"' => Slim,
        'm' or 'w' or 'M' or 'W' or '@' => Wide,
        _ => Regular,
    };
}
