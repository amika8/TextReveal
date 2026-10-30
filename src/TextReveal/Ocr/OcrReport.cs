using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace TextReveal.Ocr;

/// <summary>
/// Diagnostic for the OCR passes: runs the real pipeline on an image file, writes what every pass
/// added next to it, and answers the searches given on the command line the way the overlay would.
/// It exists so a missing match can be traced to a pass instead of guessed at. The application
/// itself never writes an image or a recognised word to disk; this only runs on an explicit switch.
/// </summary>
internal static class OcrReport
{
    /// <summary>Runs the pipeline on <paramref name="imagePath"/> and writes <c>*.ocr.txt</c>.</summary>
    /// <param name="imagePath">A screen capture, as a PNG or a JPEG.</param>
    /// <param name="queries">Searches to try against what was read, as the overlay would.</param>
    /// <param name="languageTag">The recognizer the user configured, so the report matches the app.</param>
    public static void Write(string imagePath, IReadOnlyList<string> queries, string? languageTag)
    {
        // On a worker thread on purpose: the recognition awaits WinRT operations, and blocking the
        // UI thread while it does would deadlock on the dispatcher.
        Task.Run(() => WriteAsync(imagePath, queries, languageTag)).GetAwaiter().GetResult();
    }

    private static async Task WriteAsync(string imagePath, IReadOnlyList<string> queries, string? languageTag)
    {
        var image = Load(imagePath);
        var service = new OcrService();
        service.Configure(languageTag);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"image: {Path.GetFileName(imagePath)} {image.PixelWidth}x{image.PixelHeight}");
        report.AppendLine(CultureInfo.InvariantCulture, $"language: {service.LanguageTag ?? "none"}");

        var passes = 0;
        var known = 0;
        var watch = Stopwatch.StartNew();

        void Progress(IReadOnlyList<RecognizedWord> current)
        {
            passes++;
            var added = current.Skip(known).ToArray();
            known = current.Count;

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"pass {passes}: +{added.Length} words, {known} in total, {watch.ElapsedMilliseconds} ms");
            AppendWords(report, added);

            watch.Restart();
        }

        var words = await service.RecognizeAsync(image, Progress, CancellationToken.None);

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"merged: {words.Count} words");

        foreach (var query in queries)
        {
            var matches = WordMatcher.Find(words, query);
            var hits = matches.Count == 0 ? 0 : matches[^1].Hit + 1;
            var texts = string.Join(", ", matches.Select(match => match.Word.Text).Distinct(StringComparer.Ordinal));

            report.AppendLine(CultureInfo.InvariantCulture, $"search \"{query}\": {hits} hit(s){(texts.Length == 0 ? string.Empty : $" -> {texts}")}");
        }

        File.WriteAllText(Path.ChangeExtension(imagePath, ".ocr.txt"), report.ToString());
    }

    /// <summary>Writes the words of one pass in reading order, one OCR line per line of text.</summary>
    private static void AppendWords(StringBuilder report, IEnumerable<RecognizedWord> words)
    {
        foreach (var line in SelectionTextBuilder.Build(words).Split(Environment.NewLine))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"    {line}");
        }
    }

    private static BitmapImage Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }
}
