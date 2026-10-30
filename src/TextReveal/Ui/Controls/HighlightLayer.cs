using System.Windows;
using System.Windows.Media;
using TextReveal.Ocr;

namespace TextReveal.Ui.Controls;

/// <summary>
/// Paints every match over the frozen screenshot. All rectangles are drawn in a
/// single visual through <see cref="OnRender"/>, which keeps thousands of hits
/// instantaneous and allocation free.
/// </summary>
internal sealed class HighlightLayer : FrameworkElement
{
    private const double Spacing = 1.5d;
    private const double CornerRadius = 2.5d;
    private const double MinimumWidthRatio = 0.35d;

    private static readonly Brush HighlightFill = CreateFill();
    private static readonly Pen HighlightStroke = CreateStroke();

    private IReadOnlyList<WordMatch> _matches = [];
    private double _pixelToDipScale = 1d;

    /// <summary>Replaces the highlighted matches; <paramref name="pixelToDipScale"/> maps capture pixels to WPF units.</summary>
    public void SetMatches(IReadOnlyList<WordMatch> matches, double pixelToDipScale)
    {
        _matches = matches;
        _pixelToDipScale = pixelToDipScale > 0d ? pixelToDipScale : 1d;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        var scale = _pixelToDipScale;

        foreach (var match in _matches)
        {
            var bounds = match.Word.Bounds;
            var (startFraction, endFraction) = match.Fractions;

            var left = bounds.X + (bounds.Width * startFraction);
            var width = bounds.Width * (endFraction - startFraction);

            // A single narrow letter ("i") would otherwise be almost invisible.
            width = Math.Max(width, bounds.Height * MinimumWidthRatio);

            var rectangle = new Rect(
                (left * scale) - Spacing,
                (bounds.Y * scale) - Spacing,
                (width * scale) + (Spacing * 2d),
                (bounds.Height * scale) + (Spacing * 2d));

            drawingContext.DrawRoundedRectangle(HighlightFill, HighlightStroke, rectangle, CornerRadius, CornerRadius);
        }
    }

    private static SolidColorBrush CreateFill()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xD5, 0x4F));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateStroke()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xC4, 0x00)), 1d);
        pen.Freeze();
        return pen;
    }
}
