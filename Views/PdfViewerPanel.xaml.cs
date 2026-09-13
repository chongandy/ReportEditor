using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ReportEditor.Services;
using SkiaSharp;

namespace ReportEditor.Views;

public enum PdfToolMode
{
    View,
    Highlight,
    Copy,
}

public sealed class PdfHighlight
{
    public int PageIndex { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string ColorHex { get; init; } = "#F6E05E";
}

public sealed class PdfPageView
{
    public int PageIndex { get; init; }
    public string Label { get; init; } = "";
    public BitmapSource? Image { get; init; }
    public ObservableCollection<PdfHighlight> Highlights { get; init; } = [];
}

public partial class PdfViewerPanel : UserControl, INotifyPropertyChanged
{
    private readonly ObservableCollection<PdfPageView> _pages = [];
    private readonly List<PdfHighlight> _highlights = [];
    private byte[]? _pdfBytes;
    private string _sourcePath = "";
    private string _fileName = "No PDF opened";
    private string _highlightColor = "#F6E05E";
    private PdfToolMode _toolMode = PdfToolMode.Highlight;
    private int _currentPage;
    private int _pageCount;
    private bool _showAllPages = true;
    private bool _usesEdgeViewer;
    private bool _edgeReady;

    public PdfViewerPanel()
    {
        InitializeComponent();
        Pages = _pages;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PdfPageView> Pages { get; }

    public string FileName
    {
        get => _fileName;
        private set => SetField(ref _fileName, value);
    }

    public string HighlightColor
    {
        get => _highlightColor;
        private set
        {
            SetField(ref _highlightColor, value);
            OnPropertyChanged(nameof(ToolStatus));
        }
    }

    public PdfToolMode ToolMode
    {
        get => _toolMode;
        private set
        {
            SetField(ref _toolMode, value);
            OnPropertyChanged(nameof(ToolStatus));
        }
    }

    public bool HasDocument => _pdfBytes is { Length: > 0 };

    public bool CanGoPrevious => HasDocument && !UsesEdgeViewer && !_showAllPages && _currentPage > 0;

    public bool CanGoNext => HasDocument && !UsesEdgeViewer && (_showAllPages || _currentPage < _pageCount - 1);

    public bool UsesEdgeViewer
    {
        get => _usesEdgeViewer;
        private set
        {
            if (!SetField(ref _usesEdgeViewer, value)) return;
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(ToolStatus));
        }
    }

    public string ToolStatus
    {
        get
        {
            if (!HasDocument) return "";
            if (UsesEdgeViewer)
                return "Microsoft Office / Purview protected PDF · shown with Edge. Sign in to Edge with your work account if prompted.";
            var pages = _showAllPages ? $"All pages · {_pageCount}" : $"Page {_currentPage + 1} of {_pageCount}";
            var tool = ToolMode switch
            {
                PdfToolMode.Highlight => "Highlight: drag on the page",
                PdfToolMode.Copy => "Copy image: drag to copy words or equations",
                _ => "View",
            };
            return $"{pages}  ·  {tool}";
        }
    }

    public string PageStatus => ToolStatus;

    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open PDF",
            Filter = "PDF files|*.pdf|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await LoadPdfAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open PDF:\n{ex.Message}", "PDF viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenInEdge_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourcePath) || !File.Exists(_sourcePath)) return;
        try
        {
            ProtectedPdf.OpenInMicrosoftEdge(_sourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open in Edge:\n{ex.Message}", "PDF viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfBytes is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Save PDF",
            Filter = "PDF files|*.pdf",
            FileName = string.IsNullOrWhiteSpace(_sourcePath)
                ? "annotated.pdf"
                : Path.GetFileNameWithoutExtension(_sourcePath) + "-annotated.pdf",
            InitialDirectory = string.IsNullOrWhiteSpace(_sourcePath) ? "" : Path.GetDirectoryName(_sourcePath),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (UsesEdgeViewer)
                File.Copy(_sourcePath, dlg.FileName, overwrite: true);
            else
                SaveAnnotatedPdf(dlg.FileName);
            MessageBox.Show($"Saved to:\n{dlg.FileName}", "PDF viewer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save PDF:\n{ex.Message}", "PDF viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClosePdf_Click(object sender, RoutedEventArgs e) => Clear();

    private void HighlightTool_Click(object sender, RoutedEventArgs e) => ToolMode = PdfToolMode.Highlight;

    private void CopyTool_Click(object sender, RoutedEventArgs e) => ToolMode = PdfToolMode.Copy;

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        HighlightColor = hex;
        ToolMode = PdfToolMode.Highlight;
    }

    private void UndoHighlight_Click(object sender, RoutedEventArgs e)
    {
        if (_highlights.Count == 0) return;
        var last = _highlights[^1];
        _highlights.RemoveAt(_highlights.Count - 1);
        var page = _pages.FirstOrDefault(p => p.PageIndex == last.PageIndex);
        page?.Highlights.Remove(last);
        RefreshSurfaces();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (!HasDocument) return;
        if (_showAllPages)
        {
            _showAllPages = false;
            _currentPage = 0;
        }
        else if (_currentPage > 0)
        {
            _currentPage--;
        }
        else return;
        RenderVisiblePages();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (!HasDocument) return;
        if (_showAllPages)
        {
            _showAllPages = false;
            _currentPage = 0;
        }
        else if (_currentPage < _pageCount - 1)
        {
            _currentPage++;
        }
        else return;
        RenderVisiblePages();
    }

    private void AllPages_Click(object sender, RoutedEventArgs e)
    {
        if (!HasDocument) return;
        _showAllPages = true;
        RenderVisiblePages();
    }

    public async Task LoadPdfAsync(string path)
    {
        var bytes = File.ReadAllBytes(path);
        _pdfBytes = bytes;
        _sourcePath = path;
        _highlights.Clear();
        _pages.Clear();
        _currentPage = 0;
        _showAllPages = true;
        FileName = Path.GetFileName(path);
        ToolMode = PdfToolMode.Highlight;

        var protectedByOffice = ProtectedPdf.LooksMicrosoftProtected(bytes);
        if (!protectedByOffice && TryRasterize(bytes, out var pageCount, out _))
        {
            UsesEdgeViewer = false;
            _pageCount = pageCount;
            RenderVisiblePages();
            RaiseDocumentState();
            return;
        }

        UsesEdgeViewer = true;
        _pageCount = 1;
        RaiseDocumentState();
        await ShowInEdgeViewerAsync(path);
    }

    private async Task ShowInEdgeViewerAsync(string path)
    {
        try
        {
            if (!_edgeReady)
            {
                var userData = Path.Combine(Path.GetTempPath(), "ReportEditor", "WebView2");
                Directory.CreateDirectory(userData);
                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await EdgePdf.EnsureCoreWebView2Async(env);
                _edgeReady = true;
            }

            EdgePdf.CoreWebView2.Navigate(new Uri(Path.GetFullPath(path)).AbsoluteUri);
        }
        catch (Exception ex)
        {
            ProtectedPdf.OpenInMicrosoftEdge(path);
            MessageBox.Show(
                "Opened this Microsoft-protected PDF in Microsoft Edge, which can decrypt Office / Purview files when you are signed in with a work account.\n\n" + ex.Message,
                "PDF viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static bool TryRasterize(byte[] bytes, out int pageCount, out string error)
    {
        pageCount = 0;
        error = "";
        try
        {
            pageCount = Conversion.GetPageCount(bytes);
            if (pageCount < 1)
            {
                error = "The PDF has no pages.";
                return false;
            }

            using var probe = Conversion.ToImage(bytes, page: 0, options: new PDFtoImage.RenderOptions { Dpi = 72 });
            return probe is { Width: > 0 };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Clear()
    {
        _pdfBytes = null;
        _sourcePath = "";
        _pageCount = 0;
        _currentPage = 0;
        _highlights.Clear();
        FileName = "No PDF opened";
        _pages.Clear();
        UsesEdgeViewer = false;
        if (_edgeReady && EdgePdf?.CoreWebView2 is not null)
            EdgePdf.CoreWebView2.Navigate("about:blank");
        RaiseDocumentState();
    }

    public void AddHighlight(PdfHighlight highlight)
    {
        _highlights.Add(highlight);
        var page = _pages.FirstOrDefault(p => p.PageIndex == highlight.PageIndex);
        page?.Highlights.Add(highlight);
        RefreshSurfaces();
    }

    public void CopyRegion(PdfPageView page, PdfHighlight region)
    {
        if (page.Image is null) return;
        var composed = ComposePage(page);
        var x = (int)Math.Floor(region.X * composed.PixelWidth);
        var y = (int)Math.Floor(region.Y * composed.PixelHeight);
        var width = Math.Max(1, (int)Math.Ceiling(region.Width * composed.PixelWidth));
        var height = Math.Max(1, (int)Math.Ceiling(region.Height * composed.PixelHeight));
        if (x + width > composed.PixelWidth) width = composed.PixelWidth - x;
        if (y + height > composed.PixelHeight) height = composed.PixelHeight - y;
        if (width < 1 || height < 1) return;

        var cropped = new CroppedBitmap(composed, new System.Windows.Int32Rect(x, y, width, height));
        cropped.Freeze();
        CopyImage(cropped);
    }

    private void RenderVisiblePages()
    {
        _pages.Clear();
        if (_pdfBytes is null || _pageCount < 1) return;

        if (_showAllPages)
        {
            for (var i = 0; i < _pageCount; i++)
                _pages.Add(RenderPage(i));
        }
        else
        {
            _pages.Add(RenderPage(_currentPage));
        }

        RaiseDocumentState();
        PageScroller?.ScrollToHome();
    }

    private PdfPageView RenderPage(int index)
    {
        using var skBitmap = Conversion.ToImage(_pdfBytes!, page: index, options: new PDFtoImage.RenderOptions
        {
            Dpi = 144,
        });
        return new PdfPageView
        {
            PageIndex = index,
            Label = $"Page {index + 1}",
            Image = ToBitmapSource(skBitmap),
            Highlights = new ObservableCollection<PdfHighlight>(_highlights.Where(h => h.PageIndex == index)),
        };
    }

    private void SaveAnnotatedPdf(string path)
    {
        if (_pdfBytes is null) return;
        var images = new List<(byte[] Bytes, float Width, float Height)>();
        for (var i = 0; i < _pageCount; i++)
        {
            var page = RenderPage(i);
            var composed = ComposePage(page);
            images.Add((EncodePng(composed), composed.PixelWidth, composed.PixelHeight));
        }

        QuestPDF.Fluent.Document.Create(container =>
        {
            foreach (var image in images)
            {
                container.Page(page =>
                {
                    page.Size(image.Width * 72f / 144f, image.Height * 72f / 144f, Unit.Point);
                    page.Margin(0);
                    page.Content().Image(image.Bytes);
                });
            }
        }).GeneratePdf(path);
        ProjectExportService.NormalizeIfNeeded(path);
    }

    private static BitmapSource ComposePage(PdfPageView page)
    {
        if (page.Image is null) throw new InvalidOperationException("Page image is missing.");
        var width = page.Image.PixelWidth;
        var height = page.Image.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(page.Image, new Rect(0, 0, width, height));
            foreach (var mark in page.Highlights)
            {
                var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(mark.ColorHex);
                var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, color.R, color.G, color.B));
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(
                    mark.X * width,
                    mark.Y * height,
                    mark.Width * width,
                    mark.Height * height));
            }
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void CopyImage(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", EncodePng(image), false);
        Clipboard.SetDataObject(data, true);
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream(data.ToArray());
        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    private void RefreshSurfaces()
    {
        if (PageScroller is null) return;
        foreach (var surface in FindSurfaces(PageScroller))
            surface.RefreshOverlays();
    }

    private static IEnumerable<PdfPageSurface> FindSurfaces(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is PdfPageSurface surface)
                yield return surface;
            foreach (var nested in FindSurfaces(child))
                yield return nested;
        }
    }

    private void RaiseDocumentState()
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageStatus));
        OnPropertyChanged(nameof(ToolStatus));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
