using System.Windows;

namespace TextReveal.Ocr;

/// <summary>
/// One word returned by the OCR engine. <see cref="Bounds"/> is expressed in
/// physical screen pixels, exactly like the captured bitmap.
/// </summary>
internal sealed record RecognizedWord
{
    private readonly NormalizedText _normalized;

    public RecognizedWord(string text, Rect bounds, int lineIndex)
    {
        Text = text;
        Bounds = bounds;
        LineIndex = lineIndex;
        _normalized = TextNormalizer.NormalizeWithMap(text);
        FoldedText = TextNormalizer.Fold(_normalized.Value);
    }

    public string Text { get; }

    /// <summary>Pre-computed normalized form, so filtering never allocates per keystroke.</summary>
    public string NormalizedText => _normalized.Value;

    /// <summary>
    /// Normalized form with the characters OCR confuses folded onto one class: this is what the
    /// search compares, so a read of <c>OOO</c> answers a search for <c>000</c>.
    /// </summary>
    public string FoldedText { get; }

    /// <summary>Source index of every normalized character, used to highlight typed letters.</summary>
    public int[] NormalizedOrigins => _normalized.Origins;

    public Rect Bounds { get; }

    /// <summary>OCR line index, used to rebuild paragraphs when copying a selection.</summary>
    public int LineIndex { get; init; }
}
