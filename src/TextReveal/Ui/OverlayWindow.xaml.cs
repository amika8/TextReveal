using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TextReveal.Capture;
using TextReveal.Interop;
using TextReveal.Ocr;
using TextReveal.Services;

namespace TextReveal.Ui;

/// <summary>
/// The frozen-screen overlay: search field, highlight layer and drag-to-copy selection.
/// Opened and closed without any animation, so it feels instantaneous.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WPF Window disposes its fields in OnClosed: that is the framework's disposal point.")]
public partial class OverlayWindow : Window
{
    private const double DragThreshold = 4d;
    private const double CopyPanelGap = 10d;
    private const double CopyPanelMargin = 12d;
    private const string CopyGlyph = "\uE8C8";
    private const string CopiedGlyph = "\uE73E";

    private static readonly nint HwndTopMost = new(-1);

    private readonly ScreenSnapshot _snapshot;
    private readonly OcrService _ocr;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _copyFeedback = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private IReadOnlyList<RecognizedWord> _words = [];
    private List<WordMatch> _matches = [];
    private int _hitCount;
    private List<RecognizedWord> _selectedWords = [];
    private Rect? _selectionArea;
    private bool _recognitionCompleted;
    private bool _isDragging;
    private bool _hadSelection;
    private Point _dragOrigin;
    private bool _isClosed;

    internal OverlayWindow(ScreenSnapshot snapshot, OcrService ocr)
    {
        _snapshot = snapshot;
        _ocr = ocr;

        InitializeComponent();

        _copyFeedback.Tick += OnCopyFeedbackElapsed;
        SnapshotImage.Source = snapshot.Image;
        ApplyPixelBounds(snapshot.Bounds);
    }

    /// <summary>Launches the OCR pass in the background, right after the screen is frozen.</summary>
    public void StartRecognition() => _ = RecognizeAsync();

    /// <summary>Gives the keyboard to the search field.</summary>
    public void FocusSearch()
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                Activate();
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            },
            DispatcherPriority.Input);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Force the exact monitor rectangle in physical pixels: WPF DIPs never drift here.
        var handle = new WindowInteropHelper(this).Handle;
        var bounds = _snapshot.Bounds;

        NativeMethods.SetWindowPos(
            handle,
            HwndTopMost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        NativeMethods.SetForegroundWindow(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _copyFeedback.Stop();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private double CapturePixelToDip => RootGrid.ActualWidth > 0d
        ? RootGrid.ActualWidth / _snapshot.PixelWidth
        : 1d;

    /// <summary>
    /// Overlay units (dip) to capture pixels: OCR boxes live in the frozen bitmap, so every
    /// selection has to be converted with this factor, whatever the monitor scale factor is.
    /// </summary>
    private double DipToCapturePixel => RootGrid.ActualWidth > 0d
        ? _snapshot.PixelWidth / RootGrid.ActualWidth
        : _snapshot.DpiScale > 0d ? _snapshot.DpiScale : 1d;

    private void ApplyPixelBounds(Int32Rect bounds)
    {
        var scale = _snapshot.DpiScale > 0d ? _snapshot.DpiScale : 1d;

        Left = bounds.X / scale;
        Top = bounds.Y / scale;
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
    }

    private async Task RecognizeAsync()
    {
        var cancellationToken = _lifetime.Token;

        if (!_ocr.IsAvailable)
        {
            _recognitionCompleted = true;
            UpdateMatchCount();
            return;
        }

        try
        {
            // Bitmap copy + OCR run off the UI thread: the overlay stays perfectly responsive.
            _words = await Task.Run(() => _ocr.RecognizeAsync(_snapshot.Image, cancellationToken), cancellationToken);
            _recognitionCompleted = true;
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Overlay closed before the OCR finished: nothing to display.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _recognitionCompleted = true;
            ShowMatchCount("OCR unavailable", found: false);
        }
    }

    private void ApplyFilter()
    {
        _matches = WordMatcher.Find(_words, SearchBox.Text);
        _hitCount = CountHits(_matches);
        HighlightLayer.SetMatches(_matches, CapturePixelToDip);
        UpdateMatchCount();
    }

    /// <summary>
    /// Counts the matches themselves, not the rectangles: a phrase spread over three words is one
    /// result, exactly like a browser find bar.
    /// </summary>
    private static int CountHits(List<WordMatch> matches)
    {
        var hits = 0;
        var previous = -1;

        foreach (var match in matches)
        {
            if (match.Hit == previous)
            {
                continue;
            }

            previous = match.Hit;
            hits++;
        }

        return hits;
    }

    private void UpdateMatchCount()
    {
        if (!_recognitionCompleted)
        {
            ShowMatchCount(string.Empty, found: false);
            return;
        }

        if (!_ocr.IsAvailable)
        {
            ShowMatchCount("OCR unavailable", found: false);
            return;
        }

        if (_words.Count == 0)
        {
            ShowMatchCount("No text detected", found: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            // Nothing typed yet: "0 matches" would be noise rather than information.
            ShowMatchCount(string.Empty, found: false);
            return;
        }

        // The number the user came for: the occurrences of the query, not the rectangles drawn.
        ShowMatchCount(
            _hitCount switch
            {
                0 => "No match",
                1 => "1 match",
                _ => $"{_hitCount} matches",
            },
            found: _hitCount > 0);
    }

    /// <summary>
    /// Writes the counter badge and shows it, in the accent colour as soon as one match is on screen.
    /// An empty text hides the badge, so the search panel stays quiet before the first keystroke.
    /// </summary>
    private void ShowMatchCount(string text, bool found)
    {
        MatchCountText.Text = text;
        MatchCountText.SetResourceReference(
            TextBlock.ForegroundProperty,
            found ? "AccentBrush" : "SecondaryTextBrush");

        MatchCountBadge.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnOverlaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        HighlightLayer.SetMatches(_matches, CapturePixelToDip);

        if (_selectionArea is { } area)
        {
            SelectionLayer.SetSelection(area, ToOverlayRects(_selectedWords));
            CopyPanel.Margin = PlaceCopyPanel(area);
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => CloseOverlay();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            // Esc and Enter both end the search: looking is the whole point, not editing text.
            e.Handled = true;
            CloseOverlay();
            return;
        }

        // Ctrl+C copies the drag selection; the search field keeps its own copy when it has one.
        if (e.Key == Key.C &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            _selectedWords.Count > 0 &&
            SearchBox.SelectedText.Length == 0)
        {
            e.Handled = true;
            CopySelection();
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinSearchPanel(e.OriginalSource) || IsWithinCopyPanel(e.OriginalSource))
        {
            return;
        }

        _hadSelection = _selectedWords.Count > 0;
        ClearSelection();

        _isDragging = true;
        _dragOrigin = e.GetPosition(RootGrid);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(RootGrid);
        if (!IsPastDragThreshold(_dragOrigin, current))
        {
            return;
        }

        UpdateSelection(_dragOrigin, current);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        var end = e.GetPosition(RootGrid);

        // A drag keeps the overlay open: the selection stays highlighted and asks to be copied.
        if (IsPastDragThreshold(_dragOrigin, end))
        {
            UpdateSelection(_dragOrigin, end);
            ShowCopyPanel();
            return;
        }

        // A plain click drops an existing selection, and closes the overlay when there is none.
        if (!_hadSelection)
        {
            CloseOverlay();
        }
    }

    private void OnCopyButtonClick(object sender, RoutedEventArgs e) => CopySelection();

    /// <summary>Draws the live selection and highlights every word it contains.</summary>
    private void UpdateSelection(Point origin, Point current)
    {
        var area = new Rect(origin, current);
        if (area.IsEmpty)
        {
            return;
        }

        _selectionArea = area;
        _selectedWords = FindWords(area);
        SelectionLayer.SetSelection(area, ToOverlayRects(_selectedWords));
    }

    /// <summary>
    /// Keeps the selection on screen once the mouse is released and shows the copy button
    /// next to it, so the user checks what will be copied before copying it.
    /// </summary>
    private void ShowCopyPanel()
    {
        if (_selectionArea is not { } area || _selectedWords.Count == 0)
        {
            return;
        }

        CopyPanel.Visibility = Visibility.Visible;
        CopyPanel.UpdateLayout();
        CopyPanel.Margin = PlaceCopyPanel(area);
    }

    private void ClearSelection()
    {
        _selectionArea = null;
        _selectedWords = [];
        SelectionLayer.Clear();
        CopyPanel.Visibility = Visibility.Collapsed;
    }

    private void CopySelection()
    {
        if (_selectedWords.Count == 0)
        {
            return;
        }

        var text = SelectionTextBuilder.Build(_selectedWords);
        if (!ClipboardService.TrySetText(text))
        {
            // Another process is holding the clipboard open: keep the selection on screen so the
            // click or Ctrl+C can simply be tried again, instead of closing on a silent failure.
            ShowMatchCount("Clipboard busy", found: false);
            ToolTipService.SetToolTip(CopyButton, "Clipboard busy - try again");
            return;
        }

        CopyIcon.Text = CopiedGlyph;
        ToolTipService.SetToolTip(CopyButton, "Copied");
        _copyFeedback.Start();
    }

    private void OnCopyFeedbackElapsed(object? sender, EventArgs e)
    {
        _copyFeedback.Stop();
        CopyIcon.Text = CopyGlyph;
        ToolTipService.SetToolTip(CopyButton, "Copy the selected text (Ctrl+C)");
        CloseOverlay();
    }

    /// <summary>
    /// Words whose line is covered by the drag box and whose box touches it. The box is first
    /// converted from overlay units to capture pixels, so the result never depends on the monitor
    /// scale factor.
    /// </summary>
    private List<RecognizedWord> FindWords(Rect area)
    {
        if (_words.Count == 0)
        {
            return [];
        }

        var scale = DipToCapturePixel;
        var selection = new Rect(
            Math.Min(area.Left, area.Right) * scale,
            Math.Min(area.Top, area.Bottom) * scale,
            area.Width * scale,
            area.Height * scale);

        var selected = new List<RecognizedWord>();
        foreach (var word in _words)
        {
            if (IsSelected(word.Bounds, selection))
            {
                selected.Add(word);
            }
        }

        return selected;
    }

    private static bool IsSelected(Rect word, Rect selection)
    {
        var centerY = word.Y + (word.Height / 2d);
        var sameLine = centerY >= selection.Top && centerY <= selection.Bottom;
        var overlaps = word.Right >= selection.Left && word.X <= selection.Right;
        return sameLine && overlaps;
    }

    /// <summary>Turns capture-pixel word boxes into overlay rectangles.</summary>
    private List<Rect> ToOverlayRects(List<RecognizedWord> words)
    {
        var scale = CapturePixelToDip;
        var rectangles = new List<Rect>(words.Count);

        foreach (var word in words)
        {
            var bounds = word.Bounds;
            rectangles.Add(new Rect(
                bounds.X * scale,
                bounds.Y * scale,
                Math.Max(bounds.Width * scale, 1d),
                Math.Max(bounds.Height * scale, 1d)));
        }

        return rectangles;
    }

    /// <summary>
    /// Puts the copy button just under the selection, to its left when there is no room on the
    /// right, and always inside the overlay.
    /// </summary>
    private Thickness PlaceCopyPanel(Rect area)
    {
        var width = CopyPanel.ActualWidth;
        var height = CopyPanel.ActualHeight;
        var available = RootGrid.RenderSize;

        var left = area.Right + CopyPanelGap;
        if (left + width > available.Width - CopyPanelMargin)
        {
            left = area.Left - CopyPanelGap - width;
        }

        left = Math.Clamp(left, CopyPanelMargin, Math.Max(CopyPanelMargin, available.Width - width - CopyPanelMargin));

        var top = area.Bottom + CopyPanelGap;
        if (top + height > available.Height - CopyPanelMargin)
        {
            top = area.Top - CopyPanelGap - height;
        }

        top = Math.Clamp(top, CopyPanelMargin, Math.Max(CopyPanelMargin, available.Height - height - CopyPanelMargin));

        return new Thickness(left, top, 0d, 0d);
    }

    private bool IsWithinSearchPanel(object? source) =>
        source is DependencyObject element &&
        (ReferenceEquals(element, SearchPanel) || SearchPanel.IsAncestorOf(element));

    private bool IsWithinCopyPanel(object? source) =>
        source is DependencyObject element &&
        (ReferenceEquals(element, CopyPanel) || CopyPanel.IsAncestorOf(element));

    private static bool IsPastDragThreshold(Point origin, Point current) =>
        Math.Abs(current.X - origin.X) > DragThreshold || Math.Abs(current.Y - origin.Y) > DragThreshold;

    private void CloseOverlay()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        _lifetime.Cancel();
        Close();
    }
}
