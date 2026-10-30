using System.Text;

namespace TextReveal.Ocr;

/// <summary>In-memory word search: no I/O, no allocation of a full index, plain linear scan.</summary>
internal static class WordMatcher
{
    /// <summary>
    /// Returns every occurrence of <paramref name="query"/> in reading order, together with its
    /// character span. Several occurrences inside the same word are reported separately, like a
    /// browser find bar, and a partially typed query only covers the letters it already matches.
    /// </summary>
    public static List<WordMatch> Find(IReadOnlyList<RecognizedWord> words, string? query)
    {
        var needle = CollapseSpaces(TextNormalizer.Normalize(query));
        if (needle.Length == 0)
        {
            return [];
        }

        // No space typed: the query stays inside single words, which is the common case and the
        // only one where the highlight can follow the typed letters exactly.
        return needle.Contains(' ')
            ? FindPhrase(words, needle)
            : FindInWords(words, needle);
    }

    /// <summary>
    /// Single-word query. Several occurrences inside the same word are reported separately, like a
    /// browser find bar, and a partially typed query only covers the letters it already matches.
    /// </summary>
    private static List<WordMatch> FindInWords(IReadOnlyList<RecognizedWord> words, string needle)
    {
        var matches = new List<WordMatch>(Math.Min(words.Count, 64));
        var hit = 0;

        foreach (var word in words)
        {
            var normalized = word.NormalizedText;
            var offset = normalized.IndexOf(needle, StringComparison.Ordinal);

            while (offset >= 0)
            {
                matches.Add(CreateMatch(word, offset, needle.Length, hit));
                hit++;
                offset = normalized.IndexOf(needle, offset + needle.Length, StringComparison.Ordinal);
            }
        }

        return matches;
    }

    /// <summary>
    /// Query containing spaces. The words of each OCR line are joined with a single space and the
    /// whole line is searched, so a phrase is found as easily as a single word. Every word the
    /// occurrence covers gets its own rectangle, all under the same hit number.
    /// </summary>
    private static List<WordMatch> FindPhrase(IReadOnlyList<RecognizedWord> words, string needle)
    {
        var matches = new List<WordMatch>();
        var hit = 0;

        foreach (var line in ReadLines(words))
        {
            var text = JoinLine(line, out var starts);
            var offset = text.IndexOf(needle, StringComparison.Ordinal);

            while (offset >= 0)
            {
                AddPhraseMatches(line, starts, offset, needle.Length, hit, matches);
                hit++;
                offset = text.IndexOf(needle, offset + needle.Length, StringComparison.Ordinal);
            }
        }

        return matches;
    }

    /// <summary>Groups the recognized words by OCR line, in reading order from left to right.</summary>
    private static IEnumerable<List<RecognizedWord>> ReadLines(IReadOnlyList<RecognizedWord> words) =>
        words
            .GroupBy(static word => word.LineIndex)
            .OrderBy(static line => line.Key)
            .Select(static line => line.OrderBy(static word => word.Bounds.X).ToList());

    /// <summary>Joins the normalized words of one line, remembering where each of them starts.</summary>
    private static string JoinLine(List<RecognizedWord> line, out int[] starts)
    {
        var builder = new StringBuilder();
        starts = new int[line.Count];

        for (var index = 0; index < line.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            starts[index] = builder.Length;
            builder.Append(line[index].NormalizedText);
        }

        return builder.ToString();
    }

    /// <summary>Adds one rectangle for every word that the occurrence covers.</summary>
    private static void AddPhraseMatches(
        List<RecognizedWord> line,
        int[] starts,
        int offset,
        int length,
        int hit,
        List<WordMatch> matches)
    {
        var end = offset + length;

        for (var index = 0; index < line.Count; index++)
        {
            var word = line[index];
            var wordStart = starts[index];
            var wordEnd = wordStart + word.NormalizedText.Length;

            var from = Math.Max(offset, wordStart);
            var to = Math.Min(end, wordEnd);

            if (to <= from)
            {
                continue;
            }

            matches.Add(CreateMatch(word, from - wordStart, to - from, hit));
        }
    }

    /// <summary>
    /// Turns the query into a comparable needle: trimmed, and with every run of whitespace reduced
    /// to one space, so a double space or a tab still matches the joined words of a line.
    /// </summary>
    private static string CollapseSpaces(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Translates a span of the normalized word back to indexes in the original word.</summary>
    private static WordMatch CreateMatch(RecognizedWord word, int normalizedOffset, int normalizedLength, int hit)
    {
        var origins = word.NormalizedOrigins;
        var start = normalizedOffset < origins.Length ? origins[normalizedOffset] : 0;

        // The character that follows the match already starts after it, which handles the
        // diacritics that normalization removed ("zürich" keeps its three code points).
        var end = normalizedOffset + normalizedLength < origins.Length
            ? origins[normalizedOffset + normalizedLength]
            : word.Text.Length;

        if (end <= start)
        {
            end = Math.Min(word.Text.Length, start + normalizedLength);
        }

        return new WordMatch(word, start, Math.Max(end - start, 1), hit);
    }
}

/// <summary>Rebuilds readable text from a set of selected words.</summary>
internal static class SelectionTextBuilder
{
    /// <summary>Joints words with spaces, and lines with new lines.</summary>
    public static string Build(IEnumerable<RecognizedWord> words)
    {
        var builder = new StringBuilder();
        var previousLine = -1;

        foreach (var line in words.GroupBy(static word => word.LineIndex).OrderBy(static group => group.Key))
        {
            if (previousLine >= 0)
            {
                builder.AppendLine();
            }

            builder.AppendJoin(' ', line.OrderBy(static word => word.Bounds.X).Select(static word => word.Text));
            previousLine = line.Key;
        }

        return builder.ToString();
    }
}
