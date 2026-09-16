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
    public const double DefaultLineSpacing = 1.5;
    // MarkerOffset = gap between marker and text.
    // Top-level uses Padding (not Margin) for marker room so "1." lines up with body text.
    // Nested lists use Margin.Left as the indent step under the parent item.
    public const double ListMarkerOffset = 12;
    public const double ListTopLevelLeftMargin = 0;
    public const double ListTopLevelContentPadding = 24;
    public const double ListNestedLeftMargin = 20;
    public const double ListContentPadding = 0;
    public static readonly Brush BodyForeground = CreateBodyForeground();

    private static Brush CreateBodyForeground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        brush.Freeze();
        return brush;
    }

    public static FlowDocument CreateDocument()
    {
        var doc = new FlowDocument(CreateBodyParagraph())
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

    public static Paragraph CreateBodyParagraph(string text = "")
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontFamily = BodyFont,
            FontSize = BodyFontSize,
            Foreground = BodyForeground,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = BodyFontSize * DefaultLineSpacing,
        };
        return paragraph;
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
            document.Blocks.Add(CreateBodyParagraph());
        EnsureDefaultLineSpacing(document);
    }

    public static void EnsureDefaultLineSpacing(FlowDocument document)
    {
        foreach (var block in document.Blocks.ToList())
            EnsureDefaultLineSpacing(block);
    }

    private static void EnsureDefaultLineSpacing(Block block, bool isNestedList = false)
    {
        switch (block)
        {
            case Paragraph paragraph:
            {
                var fontSize = double.IsNaN(paragraph.FontSize) || paragraph.FontSize < 1
                    ? BodyFontSize
                    : paragraph.FontSize;
                if (double.IsNaN(paragraph.LineHeight) || paragraph.LineHeight < 1)
                {
                    paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    paragraph.LineHeight = fontSize * DefaultLineSpacing;
                }
                var gap = Math.Max(0, fontSize * (DefaultLineSpacing - 1));
                paragraph.Margin = new Thickness(paragraph.Margin.Left, 0, paragraph.Margin.Right, gap);
                break;
            }
            case List list:
            {
                var between = Math.Max(0, BodyFontSize * (DefaultLineSpacing - 1));
                // Top-level: Margin=0 so markers align with body text; Padding holds marker room.
                // Nested: Margin.Left is the indent step under the parent item.
                list.Margin = new Thickness(
                    isNestedList ? ListNestedLeftMargin : ListTopLevelLeftMargin,
                    isNestedList ? between : 0,
                    0,
                    0);
                list.Padding = new Thickness(
                    isNestedList ? ListContentPadding : ListTopLevelContentPadding,
                    0,
                    0,
                    0);
                list.MarkerOffset = ListMarkerOffset;
                var items = list.ListItems.ToList();
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    item.Margin = new Thickness(0, 0, 0, i < items.Count - 1 ? between : 0);
                    item.Padding = new Thickness(0);
                    var children = item.Blocks.ToList();
                    for (var c = 0; c < children.Count; c++)
                    {
                        var child = children[c];
                        if (child is Paragraph paragraph)
                        {
                            var fontSize = double.IsNaN(paragraph.FontSize) || paragraph.FontSize < 1
                                ? BodyFontSize
                                : paragraph.FontSize;
                            if (double.IsNaN(paragraph.LineHeight) || paragraph.LineHeight < 1)
                            {
                                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                                paragraph.LineHeight = fontSize * DefaultLineSpacing;
                            }
                            var nextIsList = c + 1 < children.Count && children[c + 1] is List;
                            var gap = nextIsList ? 0 : Math.Max(0, fontSize * (DefaultLineSpacing - 1));
                            paragraph.Margin = new Thickness(paragraph.Margin.Left, 0, paragraph.Margin.Right, gap);
                        }
                        else
                        {
                            EnsureDefaultLineSpacing(child, isNestedList: child is List);
                        }
                    }
                }
                break;
            }
            case Section section:
                foreach (var child in section.Blocks)
                    EnsureDefaultLineSpacing(child);
                break;
        }
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
                    list.Margin = new Thickness(ListTopLevelLeftMargin, list.Margin.Top, 0, list.Margin.Bottom);
                    list.Padding = new Thickness(ListTopLevelContentPadding, 0, 0, 0);
                    list.MarkerOffset = ListMarkerOffset;
                    foreach (var item in list.ListItems)
                    {
                        item.Margin = new Thickness(0);
                        item.Padding = new Thickness(0);
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
