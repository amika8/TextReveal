using System.Globalization;
using System.Text;

namespace TextReveal.Ocr;

/// <summary>Case and accent insensitive normalization used by the search.</summary>
internal static class TextNormalizer
{
    /// <summary>Lowercases and strips diacritics (<c>Zürich</c> becomes <c>zurich</c>).</summary>
    public static string Normalize(string? value) => NormalizeWithMap(value).Value;

    /// <summary>
    /// Same as <see cref="Normalize"/>, but also reports the source index of every character
    /// it produced. The highlighter needs that map to paint only the letters the user typed
    /// instead of the whole word.
    /// </summary>
    public static NormalizedText NormalizeWithMap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NormalizedText.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var origins = new List<int>(value.Length);
        var offset = 0;

        // Runes keep surrogate pairs intact; each rune is decomposed on its own so a
        // stripped diacritic never shifts the mapping of the following characters.
        foreach (var rune in value.EnumerateRunes())
        {
            foreach (var character in rune.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                var lowered = char.ToLowerInvariant(character);
                builder.Append(lowered);
                origins.Add(offset);

                if (char.IsSurrogate(lowered))
                {
                    origins.Add(offset);
                }
            }

            offset += rune.Utf16SequenceLength;
        }

        return new NormalizedText(builder.ToString(), [.. origins]);
    }

    /// <summary>
    /// Maps the characters OCR confuses onto one class, so a search finds what the eye sees:
    /// <c>$456,000</c> matches a read of <c>$456,OOO</c>, <c>SQULD</c>, <c>1O</c> or <c>5URVIVED</c>.
    /// One character in, one character out, so the source map of a word stays valid.
    /// </summary>
    public static string Fold(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(FoldCharacter(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The confusions that actually happen on screen: heavy or outlined lettering, small pixel
    /// fonts, and the shapes themselves - zero and O, one and l or I, five and S, eight and B,
    /// two and Z, six and G. Digits and letters share a class, which no amount of zooming changes.
    /// </summary>
    private static char FoldCharacter(char character) => character switch
    {
        'o' or '0' => '0',
        'i' or 'l' or '1' or '|' or '\u0131' => '1',
        's' or '5' => '5',
        'b' or '8' => '8',
        'z' or '2' => '2',
        'g' or '6' => '6',
        _ => character,
    };
}

/// <summary>A normalized string plus the source index of each of its characters.</summary>
internal sealed class NormalizedText
{
    public static readonly NormalizedText Empty = new(string.Empty, []);

    public NormalizedText(string value, int[] origins)
    {
        Value = value;
        Origins = origins;
    }

    public string Value { get; }

    /// <summary>For every character of <see cref="Value"/>, its index in the source string.</summary>
    public int[] Origins { get; }
}

