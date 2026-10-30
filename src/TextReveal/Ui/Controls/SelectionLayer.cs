using System.Windows;
using System.Windows.Media;

namespace TextReveal.Ui.Controls;

/// <summary>
/// Paints the drag selection: the box itself plus every word it contains, so the user sees
/// exactly what is about to be copied. Like <see cref="HighlightLayer"/>, everything is drawn
/// in a single <see cref="OnRender"/> pass with no visual per word.
/// </summary>
internal sealed class SelectionLayer : FrameworkElement
{
    private const double WordRadius = 2.5d;

    private static readonly Brush AreaFill = CreateBrush(Color.FromArgb(0x26, 0x4C, 0x8D, 0xFF));
    private static readonly Pen AreaStroke = CreatePen(Color.FromArgb(0xFF, 0x4C, 0x8D, 0xFF), 1.5d);
    private static readonly Brush WordFill = CreateBrush(Color.FromArgb(0x59, 0x4C, 0x8D, 0xFF));
    private static readonly Pen WordStroke = CreatePen(Color.FromArgb(0xB4, 0x9C, 0xC4, 0xFF), 1d);

    private Rect? _area;
    private IReadOnlyList<Rect> _words = [];

    /// <summary>Shows <paramref name="area"/> and the overlay rectangles of the selected words.</summary>
    public void SetSelection(Rect? area, IReadOnlyList<Rect>? words = null)
    {
        _area = area;
        _words = words ?? [];
        InvalidateVisual();
    }

    /// <summary>Removes the selection from the screen.</summary>
    public void Clear() => SetSelection(null);

    protected override void OnRender(DrawingContext drawingContext)
    {
        foreach (var word in _words)
        {
            drawingContext.DrawRoundedRectangle(WordFill, WordStroke, word, WordRadius, WordRadius);
        }

        if (_area is { } area)
        {
            drawingContext.DrawRectangle(AreaFill, AreaStroke, area);
        }
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(CreateBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
