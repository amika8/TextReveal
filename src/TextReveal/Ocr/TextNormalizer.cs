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

