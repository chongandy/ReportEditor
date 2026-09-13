using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ReportEditor.Views;

public partial class PdfPageSurface : UserControl
{
    private Point _start;
    private bool _dragging;
    private Rectangle? _draft;

    public PdfPageSurface()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshOverlays();
        Loaded += (_, _) => RefreshOverlays();
    }

    private PdfViewerPanel? Host => FindHost();

    private PdfPageView? Page => DataContext as PdfPageView;

    private void PageHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Host is not { HasDocument: true } host) return;
        if (host.ToolMode is PdfToolMode.View or PdfToolMode.Pan) return;
        PageHost.Cursor = Cursors.Cross;
        _start = e.GetPosition(PageHost);
        _dragging = true;
        PageHost.CaptureMouse();
        EnsureDraft(host);
        UpdateDraft(_start);
        e.Handled = true;
    }

    private void PageHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Host is { } host && !_dragging)
            PageHost.Cursor = host.ToolMode is PdfToolMode.Pan or PdfToolMode.View ? Cursors.SizeAll : Cursors.Cross;
        if (!_dragging || Host is null) return;
        UpdateDraft(e.GetPosition(PageHost));
        e.Handled = true;
    }

    private void PageHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || Host is not { } host || Page is null) return;
        _dragging = false;
        PageHost.ReleaseMouseCapture();
        var end = e.GetPosition(PageHost);
        ClearDraft();
        var rect = Normalize(_start, end);
        if (rect.Width < 4 || rect.Height < 4)
        {
            e.Handled = true;
            return;
        }

        var bounds = GetImageBounds();
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            e.Handled = true;
            return;
        }

        var clipped = Rect.Intersect(rect, bounds);
        if (clipped.Width < 4 || clipped.Height < 4)
        {
            e.Handled = true;
            return;
        }

        var highlight = new PdfHighlight
        {
            PageIndex = Page.PageIndex,
            X = (clipped.X - bounds.X) / bounds.Width,
            Y = (clipped.Y - bounds.Y) / bounds.Height,
            Width = clipped.Width / bounds.Width,
            Height = clipped.Height / bounds.Height,
            ColorHex = host.HighlightColor,
        };

        if (host.ToolMode == PdfToolMode.Highlight)
            host.AddHighlight(highlight);
        else if (host.ToolMode == PdfToolMode.Copy)
            host.CopyRegion(Page, highlight);

        e.Handled = true;
    }

    private void PageHost_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragging) return;
        ClearDraft();
    }

    private void PageHost_OnSizeChanged(object sender, SizeChangedEventArgs e) => RefreshOverlays();

    public void RefreshOverlays()
    {
        if (Overlay is null) return;
        Overlay.Children.Clear();
        if (Page is null) return;
        var bounds = GetImageBounds();
        if (bounds.Width < 1 || bounds.Height < 1) return;

        foreach (var mark in Page.Highlights)
        {
            Overlay.Children.Add(new Rectangle
            {
                Width = mark.Width * bounds.Width,
                Height = mark.Height * bounds.Height,
                Fill = HighlightBrush(mark.ColorHex),
                IsHitTestVisible = false,
            });
            Canvas.SetLeft(Overlay.Children[^1], bounds.X + mark.X * bounds.Width);
            Canvas.SetTop(Overlay.Children[^1], bounds.Y + mark.Y * bounds.Height);
        }
    }

    private void EnsureDraft(PdfViewerPanel host)
    {
        ClearDraft();
        _draft = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)),
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
            Fill = host.ToolMode == PdfToolMode.Highlight
                ? HighlightBrush(host.HighlightColor)
                : new SolidColorBrush(Color.FromArgb(40, 31, 78, 121)),
            IsHitTestVisible = false,
        };
        Overlay.Children.Add(_draft);
    }

    private void UpdateDraft(Point current)
    {
        if (_draft is null) return;
        var rect = Normalize(_start, current);
        _draft.Width = Math.Max(0, rect.Width);
        _draft.Height = Math.Max(0, rect.Height);
        Canvas.SetLeft(_draft, rect.X);
        Canvas.SetTop(_draft, rect.Y);
    }

    private void ClearDraft()
    {
        if (_draft is null) return;
        Overlay.Children.Remove(_draft);
        _draft = null;
    }

    private Rect GetImageBounds()
    {
        if (PageImage?.Source is not { } source) return new Rect(0, 0, PageHost.ActualWidth, PageHost.ActualHeight);
        var available = new Size(PageHost.ActualWidth, PageHost.ActualHeight);
        if (available.Width < 1 || available.Height < 1) return Rect.Empty;
        var imageSize = new Size(source.Width, source.Height);
        if (imageSize.Width < 1 || imageSize.Height < 1) return Rect.Empty;
        var scale = Math.Min(available.Width / imageSize.Width, available.Height / imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        return new Rect((available.Width - width) / 2, (available.Height - height) / 2, width, height);
    }

    private static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Brush HighlightBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private PdfViewerPanel? FindHost()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is PdfViewerPanel panel) return panel;
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
