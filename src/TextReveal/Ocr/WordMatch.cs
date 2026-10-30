namespace TextReveal.Ocr;

/// <summary>
/// One occurrence of the query inside a word. <see cref="Start"/> and <see cref="Length"/> are
/// character indexes in <see cref="RecognizedWord.Text"/>, so the highlighter can paint only the
/// letters that were typed and let the highlight grow with them, exactly like a browser find bar.
/// <see cref="Hit"/> groups the words of a single match: a phrase of three words is one hit spread
/// over three rectangles, and the search panel counts hits, not rectangles.
/// </summary>
internal readonly record struct WordMatch(RecognizedWord Word, int Start, int Length, int Hit)
{
    /// <summary>
    /// Horizontal span of the match, as a fraction of the word rectangle. OCR only reports one
    /// rectangle per word, so the letters are measured with <see cref="GlyphWidths"/>.
    /// </summary>
    public (double Start, double End) Fractions => GlyphWidths.MeasureSpan(Word.Text, Start, Length);
}
