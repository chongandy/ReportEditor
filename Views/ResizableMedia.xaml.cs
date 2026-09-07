using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReportEditor.Views;

public partial class ResizableMedia : UserControl
{
    private bool _dragging;
    private double _startMouseX;
    private double _startWidth;
    private double _aspect = 1;
    private bool _isSelected;

    public ResizableMedia()
    {
        InitializeComponent();

        // RichTextBox marks mouse events Handled for caret/selection; listen anyway.
        ResizeThumb.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnThumbDown), handledEventsToo: true);
        ResizeThumb.AddHandler(PreviewMouseMoveEvent, new MouseEventHandler(OnThumbMove), handledEventsToo: true);
        ResizeThumb.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnThumbUp), handledEventsToo: true);
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMediaDown), handledEventsToo: true);
        LostMouseCapture += (_, _) => _dragging = false;
    }

    public event Action<ResizableMedia>? RequestSelect;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            var vis = value ? Visibility.Visible : Visibility.Collapsed;
            ResizeThumb.Visibility = vis;
            SelectionChrome.Visibility = vis;
        }
    }

    public ImageSource? Source
    {
        get => PartImage.Source;
        set
        {
            PartImage.Source = value;
            _aspect = GetAspect();
            ApplyAspectHeight();
        }
    }

    private void OnMediaDown(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) return;
        if (e.OriginalSource is DependencyObject d && IsUnderThumb(d))
            return;
        RequestSelect?.Invoke(this);
    }

    private bool IsUnderThumb(DependencyObject? start)
    {
        while (start is not null)
        {
            if (ReferenceEquals(start, ResizeThumb)) return true;
            start = start is Visual
                ? VisualTreeHelper.GetParent(start)
                : LogicalTreeHelper.GetParent(start);
        }
        return false;
    }

    private void OnThumbDown(object sender, MouseButtonEventArgs e)
    {
        IsSelected = true;
        RequestSelect?.Invoke(this);

        _aspect = GetAspect();
        _startWidth = ActualWidth > 1 ? ActualWidth : (double.IsNaN(Width) || Width < 1 ? 120 : Width);
        if (double.IsNaN(Width) || Width < 1)
            Width = _startWidth;
        if (double.IsNaN(Height) || Height < 1)
            Height = _startWidth / Math.Max(_aspect, 0.05);

        _startMouseX = GetRootX(e);
        _dragging = true;
        ResizeThumb.CaptureMouse();
        e.Handled = true;
    }

    private void OnThumbMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

        var delta = GetRootX(e) - _startMouseX;
        var nextWidth = Math.Clamp(_startWidth + delta, 40, 2400);
        Width = nextWidth;
        Height = nextWidth / Math.Max(_aspect, 0.05);
        InvalidateMeasure();
        InvalidateArrange();
        if (Parent is UIElement parent)
        {
            parent.InvalidateMeasure();
            parent.InvalidateArrange();
        }
        e.Handled = true;
    }

    private void OnThumbUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (ResizeThumb.IsMouseCaptured)
            ResizeThumb.ReleaseMouseCapture();
        ForceDocumentRelayout();
        e.Handled = true;
    }

    private static double GetRootX(MouseEventArgs e) => e.GetPosition(null).X;

    private double GetAspect()
    {
        if (Source is BitmapSource bmp && bmp.PixelHeight > 0)
            return Math.Max(0.05, (double)bmp.PixelWidth / bmp.PixelHeight);
        if (ActualHeight > 1 && ActualWidth > 1)
            return ActualWidth / ActualHeight;
        if (!double.IsNaN(Width) && !double.IsNaN(Height) && Height > 1)
            return Width / Height;
        return 1;
    }

    private void ApplyAspectHeight()
    {
        if (double.IsNaN(Width) || Width < 1) return;
        Height = Width / Math.Max(GetAspect(), 0.05);
    }

    private void ForceDocumentRelayout()
    {
        InvalidateMeasure();
        InvalidateArrange();

        if (Parent is InlineUIContainer container)
        {
            var child = container.Child;
            container.Child = null;
            container.Child = child;
        }

        UpdateLayout();
    }
}
