using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfMath.Controls;

namespace ReportEditor.Services;

public static class DocumentCodec
{
    public static string Export(FlowDocument document)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.XamlPackage);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static readonly FontFamily BodyFont = new("Segoe UI");
    public const double BodyFontSize = 14;
    public static readonly Brush BodyForeground = CreateBodyForeground();

    private static Brush CreateBodyForeground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        brush.Freeze();
        return brush;
    }

    public static FlowDocument CreateDocument()
    {
        var doc = new FlowDocument(new Paragraph(new Run()))
        {
            FontFamily = BodyFont,
            FontSize = BodyFontSize,
            Foreground = BodyForeground,
            Background = Brushes.Transparent,
            PagePadding = new Thickness(0),
        };
        doc.ClearValue(FlowDocument.PageWidthProperty);
        return doc;
    }

    public static void Import(FlowDocument document, string? packageBase64)
    {
        new TextRange(document.ContentStart, document.ContentEnd).Text = "";
        if (!string.IsNullOrWhiteSpace(packageBase64))
        {
            var bytes = Convert.FromBase64String(packageBase64);
            using var ms = new MemoryStream(bytes);
            new TextRange(document.ContentStart, document.ContentEnd).Load(ms, DataFormats.XamlPackage);
        }

        RestoreTypography(document);
        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph(new Run()));
    }

    public static void RestoreTypography(FlowDocument document)
    {
        document.FontFamily = BodyFont;
        document.FontSize = BodyFontSize;
        document.Foreground = BodyForeground;
        document.Background = Brushes.Transparent;
        document.PagePadding = new Thickness(0);
        document.ClearValue(FlowDocument.PageWidthProperty);
        document.ClearValue(FlowDocument.ColumnWidthProperty);

        foreach (var block in document.Blocks.ToList())
            RestoreElement(block);
    }

    private static void RestoreElement(TextElement element)
    {
        if (double.IsNaN(element.FontSize) || element.FontSize < 8)
            element.ClearValue(TextElement.FontSizeProperty);

        if (element.FontFamily is null || string.IsNullOrWhiteSpace(element.FontFamily.Source))
            element.ClearValue(TextElement.FontFamilyProperty);

        if (element.Foreground is not SolidColorBrush brush || brush.Color.A == 0 || IsNearWhite(brush.Color))
            element.ClearValue(TextElement.ForegroundProperty);

        if (element is Block block)
        {
            if (double.IsNaN(block.LineHeight) || block.LineHeight < 1)
                block.ClearValue(Block.LineHeightProperty);

            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var inline in paragraph.Inlines.ToList())
                        RestoreElement(inline);
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                    {
                        RestoreElement(item);
                        foreach (var child in item.Blocks.ToList())
                            RestoreElement(child);
                    }
                    break;
                case Section section:
                    foreach (var child in section.Blocks.ToList())
                        RestoreElement(child);
                    break;
            }
        }
        else if (element is Span span)
        {
            foreach (var inline in span.Inlines.ToList())
                RestoreElement(inline);
        }
    }

    private static bool IsNearWhite(Color color) =>
        color.R > 245 && color.G > 245 && color.B > 245;

    public static BitmapSource RenderLatex(string latex, double fontSize = 20)
    {
        var control = new FormulaControl
        {
            Formula = latex,
            FontSize = fontSize,
            SnapsToDevicePixels = true,
        };
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = control.DesiredSize;
        if (size.Width < 1 || size.Height < 1)
            size = new Size(Math.Max(1, size.Width), Math.Max(1, size.Height));
        control.Arrange(new Rect(size));
        var bmp = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(control.ActualWidth > 0 ? control.ActualWidth : size.Width)),
            Math.Max(1, (int)Math.Ceiling(control.ActualHeight > 0 ? control.ActualHeight : size.Height)),
            96, 96, PixelFormats.Pbgra32);
        bmp.Render(control);
        bmp.Freeze();
        return bmp;
    }
}
