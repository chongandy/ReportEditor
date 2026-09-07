using ReportEditor.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ReportEditor.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;
using QColors = QuestPDF.Helpers.Colors;

namespace ReportEditor.Services;

public sealed class ExportBlock
{
    public string? Text { get; init; }
    public byte[]? ImageBytes { get; init; }
    public double ImageWidthPx { get; init; }
    public double ImageHeightPx { get; init; }
}

public sealed class ExportReport
{
    public string Title { get; init; } = "";
    public List<ExportBlock> Blocks { get; init; } = [];
}

public sealed class ExportMilestone
{
    public string Title { get; init; } = "";
    public List<ExportReport> Reports { get; init; } = [];
}

public sealed class ExportProjectDocument
{
    public string ProjectName { get; init; } = "";
    public string ChargeNumber { get; init; } = "";
    public string MaterialNumber { get; init; } = "";
    public string TravelNumber { get; init; } = "";
    public string Customer { get; init; } = "";
    public string DueDate { get; init; } = "";
    public List<ExportMilestone> Milestones { get; init; } = [];
}

public static class ProjectExportService
{
    static ProjectExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static ExportProjectDocument Build(Project project)
    {
        var doc = new ExportProjectDocument
        {
            ProjectName = string.IsNullOrWhiteSpace(project.Name) ? "Untitled project" : project.Name,
            ChargeNumber = project.ChargeNumber,
            MaterialNumber = project.MaterialNumber,
            TravelNumber = project.TravelNumber,
            Customer = project.Customer,
            DueDate = project.DueDate?.ToString("d") ?? "",
        };

        foreach (var milestone in project.Milestones.OrderBy(m => m.No))
        {
            var ms = new ExportMilestone
            {
                Title = string.IsNullOrWhiteSpace(milestone.Name)
                    ? $"Milestone {milestone.No}"
                    : $"{milestone.No}. {milestone.Name}",
            };

            foreach (var report in milestone.Reports.Where(r => string.IsNullOrWhiteSpace(r.RelatedFollowUpId)))
            {
                ms.Reports.Add(ToExportReport(report));
                foreach (var followUp in report.FollowUps)
                {
                    var work = milestone.Reports.FirstOrDefault(w =>
                        w.Id == followUp.WorkReportId || w.RelatedFollowUpId == followUp.Id);
                    if (work is not null)
                        ms.Reports.Add(ToExportReport(work));
                }
            }

            doc.Milestones.Add(ms);
        }

        return doc;
    }

    private static ExportReport ToExportReport(Report report) => new()
    {
        Title = string.IsNullOrWhiteSpace(report.Title) ? "Report" : report.Title,
        Blocks = ExtractBlocks(report.BodyPackageBase64),
    };

    public static List<ExportBlock> ExtractBlocks(string? packageBase64)
    {
        var result = new List<ExportBlock>();
        var flow = DocumentCodec.CreateDocument();
        DocumentCodec.Import(flow, packageBase64);
        foreach (var block in flow.Blocks)
            CollectBlocks(block, result);
        if (result.Count == 0)
            result.Add(new ExportBlock { Text = "" });
        return result;
    }

    private static void CollectBlocks(System.Windows.Documents.Block block, List<ExportBlock> result)
    {
        switch (block)
        {
            case System.Windows.Documents.Paragraph paragraph:
                CollectInlines(paragraph.Inlines, result);
                break;
            case System.Windows.Documents.List list:
                foreach (var item in list.ListItems)
                foreach (var child in item.Blocks)
                    CollectBlocks(child, result);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    CollectBlocks(child, result);
                break;
        }
    }

    private static void CollectInlines(InlineCollection inlines, List<ExportBlock> result)
    {
        var text = new System.Text.StringBuilder();
        void FlushText()
        {
            if (text.Length == 0) return;
            result.Add(new ExportBlock { Text = text.ToString() });
            text.Clear();
        }

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case System.Windows.Documents.Run run:
                    text.Append(run.Text);
                    break;
                case LineBreak:
                    text.AppendLine();
                    break;
                case Span span:
                    CollectInlines(span.Inlines, result);
                    break;
                case InlineUIContainer { Child: FrameworkElement fe }:
                    FlushText();
                    if (TryGetImageBytes(fe, out var bytes, out var w, out var h))
                        result.Add(new ExportBlock { ImageBytes = bytes, ImageWidthPx = w, ImageHeightPx = h });
                    break;
            }
        }
        FlushText();
    }

    private static bool TryGetImageBytes(FrameworkElement element, out byte[] bytes, out double width, out double height)
    {
        bytes = [];
        width = height = 0;
        ImageSource? source = null;
        if (element is System.Windows.Controls.Image img)
        {
            source = img.Source;
            width = img.ActualWidth > 1 ? img.ActualWidth : img.Width;
            height = img.ActualHeight > 1 ? img.ActualHeight : img.Height;
        }
        else if (element is ResizableMedia media)
        {
            source = media.Source;
            width = media.ActualWidth > 1 ? media.ActualWidth : media.Width;
            height = media.ActualHeight > 1 ? media.ActualHeight : media.Height;
        }

        if (source is not BitmapSource bitmap) return false;
        if (width < 1) width = bitmap.PixelWidth;
        if (height < 1) height = bitmap.PixelHeight;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        bytes = ms.ToArray();
        return bytes.Length > 0;
    }

    public static void ExportPdf(ExportProjectDocument model, string path)
    {
        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));
                page.Header().Text(model.ProjectName).SemiBold().FontSize(20);
                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text($"Charge Number: {model.ChargeNumber}");
                    col.Item().Text($"Material Number: {model.MaterialNumber}");
                    col.Item().Text($"Travel Number: {model.TravelNumber}");
                    col.Item().Text($"Customer: {model.Customer}");
                    col.Item().Text($"Due date: {model.DueDate}");
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(QColors.Grey.Lighten2);

                    foreach (var milestone in model.Milestones)
                    {
                        col.Item().PaddingTop(12).Text(milestone.Title).SemiBold().FontSize(16);
                        if (milestone.Reports.Count == 0)
                        {
                            col.Item().Text("No reports.").Italic().FontColor(QColors.Grey.Medium);
                            continue;
                        }

                        foreach (var report in milestone.Reports)
                        {
                            col.Item().PaddingTop(8).Text(report.Title).SemiBold().FontSize(13);
                            foreach (var block in report.Blocks)
                            {
                                if (!string.IsNullOrWhiteSpace(block.Text))
                                    col.Item().Text(block.Text);
                                if (block.ImageBytes is { Length: > 0 })
                                {
                                    var maxWidth = 450f;
                                    var w = (float)(block.ImageWidthPx > 1 ? block.ImageWidthPx : 240);
                                    var h = (float)(block.ImageHeightPx > 1 ? block.ImageHeightPx : 180);
                                    if (w > maxWidth)
                                    {
                                        h *= maxWidth / w;
                                        w = maxWidth;
                                    }
                                    col.Item().Width(w).Height(h).Image(block.ImageBytes);
                                }
                            }
                        }
                    }
                });
            });
        }).GeneratePdf(path);
    }

    public static void ExportDocx(ExportProjectDocument model, string path)
    {
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = word.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        var body = main.Document.Body!;

        body.Append(CreateParagraph(model.ProjectName, "Heading1"));
        body.Append(CreateParagraph($"Charge Number: {model.ChargeNumber}"));
        body.Append(CreateParagraph($"Material Number: {model.MaterialNumber}"));
        body.Append(CreateParagraph($"Travel Number: {model.TravelNumber}"));
        body.Append(CreateParagraph($"Customer: {model.Customer}"));
        body.Append(CreateParagraph($"Due date: {model.DueDate}"));
        body.Append(CreateParagraph(""));

        foreach (var milestone in model.Milestones)
        {
            body.Append(CreateParagraph(milestone.Title, "Heading2"));
            if (milestone.Reports.Count == 0)
            {
                body.Append(CreateParagraph("No reports."));
                continue;
            }

            foreach (var report in milestone.Reports)
            {
                body.Append(CreateParagraph(report.Title, "Heading3"));
                foreach (var block in report.Blocks)
                {
                    if (!string.IsNullOrWhiteSpace(block.Text))
                        body.Append(CreateParagraph(block.Text));
                    if (block.ImageBytes is { Length: > 0 })
                        AppendImage(main, body, block.ImageBytes, block.ImageWidthPx, block.ImageHeightPx);
                }
            }
        }

        main.Document.Save();
    }

    private static W.Paragraph CreateParagraph(string text, string? styleId = null)
    {
        var runProps = new W.RunProperties();
        var paraProps = new W.ParagraphProperties();

        switch (styleId)
        {
            case "Heading1":
                runProps.Bold = new W.Bold();
                runProps.FontSize = new W.FontSize { Val = "32" };
                paraProps.SpacingBetweenLines = new W.SpacingBetweenLines { After = "200" };
                break;
            case "Heading2":
                runProps.Bold = new W.Bold();
                runProps.FontSize = new W.FontSize { Val = "28" };
                paraProps.SpacingBetweenLines = new W.SpacingBetweenLines { Before = "240", After = "120" };
                break;
            case "Heading3":
                runProps.Bold = new W.Bold();
                runProps.FontSize = new W.FontSize { Val = "24" };
                paraProps.SpacingBetweenLines = new W.SpacingBetweenLines { Before = "160", After = "80" };
                break;
        }

        var run = new W.Run(runProps, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var para = new W.Paragraph(paraProps, run);
        return para;
    }

    private static void AppendImage(MainDocumentPart main, W.Body body, byte[] pngBytes, double widthPx, double heightPx)
    {
        var part = main.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(pngBytes))
            part.FeedData(stream);

        var relId = main.GetIdOfPart(part);
        var cx = (long)(Math.Max(widthPx, 1) * 9525);
        var cy = (long)(Math.Max(heightPx, 1) * 9525);
        var maxCx = 5486400L; // ~6 inches
        if (cx > maxCx)
        {
            cy = cy * maxCx / cx;
            cx = maxCx;
        }

        var element =
            new W.Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cx, Cy = cy },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = 1U, Name = "Picture" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = "image.png" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = 0U,
                    DistanceFromBottom = 0U,
                    DistanceFromLeft = 0U,
                    DistanceFromRight = 0U,
                });

        body.Append(new W.Paragraph(new W.Run(element)));
    }
}
