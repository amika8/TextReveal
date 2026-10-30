using System.Windows;

namespace TextReveal.Ocr;

/// <summary>
/// Merges the readings of the OCR passes into one word list.
/// </summary>
/// <remarks>
/// The passes see the same screen through different filters, so each of them reads something the
/// others miss: the plain pass is exact on ordinary text, the masks pull words out of thumbnails
/// and posters. The first pass is authoritative where it read something, later ones only add what is
/// still unknown - the same place seen twice with the same word is dropped, while a genuinely
/// different reading on the same place is kept, because a wrong word never hides a right one for
/// someone typing what they see.
/// </remarks>
internal static class OcrWordMerger
{
    /// <summary>Share of the smaller box that two boxes must cover to be the same place on screen.</summary>
    private const double SamePlaceThreshold = 0.5d;

    /// <summary>
    /// Adds the words of one pass to <paramref name="accepted"/>, and returns the first free OCR
    /// line index for the next pass.
    /// </summary>
    /// <param name="keepOwnLines">
    /// True for the first pass: its words already know their line, since the engine segmented them.
    /// </param>
    public static int Merge(
        List<RecognizedWord> accepted,
        List<RecognizedWord> candidates,
        int minimumLength,
        int nextLine,
        bool keepOwnLines)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.NormalizedText.Length < minimumLength)
            {
                continue;
            }

            // A mask turns a photo into noise: only words that could be typed are worth keeping.
            if (minimumLength > 1 && !HasText(candidate.NormalizedText))
            {
                continue;
            }

            if (IsKnown(candidate, accepted))
            {
                continue;
            }

            var line = keepOwnLines ? candidate.LineIndex : FindLine(candidate, accepted);
            if (line < 0)
            {
                line = nextLine;
                nextLine++;
            }

            nextLine = Math.Max(nextLine, line + 1);
            accepted.Add(candidate with { LineIndex = line });
        }

        return nextLine;
    }

    /// <summary>True when the same place already holds the same word.</summary>
    private static bool IsKnown(RecognizedWord candidate, List<RecognizedWord> accepted)
    {
        foreach (var word in accepted)
        {
            if (Overlap(candidate.Bounds, word.Bounds) < SamePlaceThreshold)
            {
                continue;
            }

            if (IsSameWord(candidate.NormalizedText, word.NormalizedText))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Share of the smaller box that both boxes cover: 1 means the very same rectangle, 0 means
    /// they do not touch.
    /// </summary>
    private static double Overlap(Rect left, Rect right)
    {
        var intersection = Rect.Intersect(left, right);
        if (intersection.IsEmpty)
        {
            return 0d;
        }

        var smaller = Math.Min(left.Width * left.Height, right.Width * right.Height);
        return smaller <= 0d ? 0d : intersection.Width * intersection.Height / smaller;
    }

    /// <summary>
    /// True when two readings are the same word up to what OCR gets wrong: a missing space, an
    /// accent, one letter. A completely different word is a second opinion, not a duplicate.
    /// </summary>
    private static bool IsSameWord(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        var shortest = Math.Min(left.Length, right.Length);
        if (shortest == 0)
        {
            return false;
        }

        var tolerance = Math.Max(1, shortest / 4);
        return Math.Abs(left.Length - right.Length) <= tolerance
            && Distance(left, right, tolerance) <= tolerance;
    }

    /// <summary>Levenshtein distance, given up as soon as it exceeds <paramref name="limit"/>.</summary>
    private static int Distance(string left, string right, int limit)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var best = current[0];

            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);

                best = Math.Min(best, current[column]);
            }

            if (best > limit)
            {
                return best;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>
    /// The OCR line a late word belongs to, so a typed phrase still spans passes, or -1 when the
    /// word opens a line of its own.
    /// </summary>
    private static int FindLine(RecognizedWord candidate, List<RecognizedWord> accepted)
    {
        var box = candidate.Bounds;
        var center = box.Left + (box.Width / 2d);
        var line = -1;
        var best = double.MaxValue;

        foreach (var word in accepted)
        {
            var other = word.Bounds;

            var vertical = Math.Min(box.Bottom, other.Bottom) - Math.Max(box.Top, other.Top);
            if (vertical < box.Height * 0.5d || vertical < other.Height * 0.5d)
            {
                continue;
            }

            // A gap wider than two lines is another column, not the next word.
            var gap = Math.Max(0d, Math.Max(box.Left - other.Right, other.Left - box.Right));
            if (gap > box.Height * 2d)
            {
                continue;
            }

            var distance = Math.Abs(center - (other.Left + (other.Width / 2d)));
            if (distance < best)
            {
                best = distance;
                line = word.LineIndex;
            }
        }

        return line;
    }

    private static bool HasText(string normalized)
    {
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                return true;
            }
        }

        return false;
    }
}
