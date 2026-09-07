using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ReportEditor.Services;

namespace ReportEditor.Views;

public partial class RichTextEditor : UserControl
{
    private bool _syncingToolbar;
    private ResizableMedia? _selectedMedia;
    private bool _keepMediaSelection;
    private Point _pressPoint;
    private bool _openingLink;

    public RichTextEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Editor is null) return;
            Editor.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Editor_OnPreviewMouseLeftButtonDown), true);
            Editor.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Editor_OnPreviewMouseLeftButtonUp), true);
            Editor.AddHandler(Hyperlink.RequestNavigateEvent, new System.Windows.Navigation.RequestNavigateEventHandler(Editor_OnRequestNavigate));
        };
    }

    public bool IsReadOnly
    {
        get => Editor is { IsReadOnly: true };
        set
        {
            if (Editor is null || ToolBar is null) return;
            Editor.IsReadOnly = value;
            ToolBar.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public void Clear()
    {
        if (Editor is null) return;
        Editor.Document = DocumentCodec.CreateDocument();
    }

    public void Append(string text)
    {
        if (Editor is null) return;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Editor.Document.Blocks.Add(new Paragraph(new Run(line)
            {
                FontFamily = DocumentCodec.BodyFont,
                FontSize = DocumentCodec.BodyFontSize,
                Foreground = DocumentCodec.BodyForeground,
            }));
        }
        WrapMedia(Editor.Document);
        Editor.CaretPosition = Editor.Document.ContentEnd;
        Editor.Focus();
    }

    public string Export()
    {
        if (Editor is null) return "";
        FlattenMedia(Editor.Document);
        var payload = DocumentCodec.Export(Editor.Document);
        WrapMedia(Editor.Document);
        StyleHyperlinks(Editor.Document);
        return payload;
    }

    public void Load(string? package)
    {
        if (Editor is null) return;
        Editor.Document = DocumentCodec.CreateDocument();
        DocumentCodec.Import(Editor.Document, package);
        WrapMedia(Editor.Document);
        StyleHyperlinks(Editor.Document);
        Editor.CaretPosition = Editor.Document.ContentEnd;
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
    private void Italic_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
    private void Underline_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Editor);
    private void AlignLeft_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignLeft.Execute(null, Editor);
    private void AlignCenter_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignCenter.Execute(null, Editor);
    private void AlignRight_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignRight.Execute(null, Editor);
    private void Bullets_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, Editor);
    private void Numbering_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, Editor);
    private void Undo_Click(object sender, RoutedEventArgs e) => ApplicationCommands.Undo.Execute(null, Editor);
    private void Redo_Click(object sender, RoutedEventArgs e) => ApplicationCommands.Redo.Execute(null, Editor);

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is null) return;
        var existing = FindHyperlink(Editor.Selection.Start) ?? FindHyperlink(Editor.CaretPosition);
        var display = Editor.Selection.IsEmpty
            ? existing is null ? "" : new TextRange(existing.ContentStart, existing.ContentEnd).Text
            : Editor.Selection.Text.Replace("\r\n", " ").Trim();
        var target = existing is null ? "" : ShellLink.FromHyperlink(existing);
        if (string.IsNullOrWhiteSpace(target))
            target = ClipboardLink();
        if (string.IsNullOrWhiteSpace(display) && !string.IsNullOrWhiteSpace(target))
            display = ShellLink.DisplayName(target);
        if (!PromptLink(ref display, ref target, existing is not null, out var remove))
            return;

        if (remove)
        {
            if (existing is not null)
                RemoveHyperlink(existing);
            return;
        }

        if (string.IsNullOrWhiteSpace(target)) return;
        if (string.IsNullOrWhiteSpace(display))
            display = ShellLink.DisplayName(target);

        if (existing is not null && Editor.Selection.IsEmpty)
        {
            ApplyHyperlink(existing, display, target);
            return;
        }

        InsertHyperlink(display, target);
    }

    private void FontSizeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingToolbar || Editor is null || FontSizeBox.SelectedItem is not ComboBoxItem item) return;
        if (!double.TryParse(item.Content?.ToString(), out var size)) return;
        Editor.Focus();
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
    }

    private void FontColorBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingToolbar || Editor is null || FontColorBox.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string hex) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Editor.Focus();
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        Editor.CaretBrush = color.R > 240 && color.G > 240 && color.B > 240
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30))
            : brush;
    }

    private void Editor_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox box || FontSizeBox is null || FontColorBox is null) return;
        SyncMediaSelectionFromCaret(box);
        _syncingToolbar = true;
        try
        {
            if (box.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size)
            {
                foreach (ComboBoxItem item in FontSizeBox.Items)
                {
                    if (item.Content?.ToString() == ((int)Math.Round(size)).ToString())
                    {
                        FontSizeBox.SelectedItem = item;
                        break;
                    }
                }
            }

            if (box.Selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush brush)
            {
                foreach (ComboBoxItem item in FontColorBox.Items)
                {
                    if (item.Tag is string tag &&
                        (Color)ColorConverter.ConvertFromString(tag) == brush.Color)
                    {
                        FontColorBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        finally
        {
            _syncingToolbar = false;
        }
    }

    private void Editor_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(Editor);
        var media = FindResizableMedia(e.OriginalSource as DependencyObject);
        if (media is not null)
        {
            _keepMediaSelection = true;
            SelectMedia(media);
            return;
        }

        _keepMediaSelection = false;
        ClearMediaSelection();
    }

    private void SelectMedia(ResizableMedia media)
    {
        if (ReferenceEquals(_selectedMedia, media))
        {
            media.IsSelected = true;
            return;
        }

        if (_selectedMedia is not null)
            _selectedMedia.IsSelected = false;
        _selectedMedia = media;
        media.IsSelected = true;
    }

    private void ClearMediaSelection()
    {
        if (_selectedMedia is null) return;
        _selectedMedia.IsSelected = false;
        _selectedMedia = null;
    }

    private void SyncMediaSelectionFromCaret(RichTextBox box)
    {
        if (box.Document is null) return;
        var hit = FindSelectedMedia(box);
        if (hit is not null)
        {
            _keepMediaSelection = false;
            SelectMedia(hit);
            return;
        }

        // Keep handle after clicking the image/equation when caret lands beside it.
        if (_keepMediaSelection && _selectedMedia is not null)
        {
            _keepMediaSelection = false;
            _selectedMedia.IsSelected = true;
            return;
        }

        ClearMediaSelection();
    }

    private static ResizableMedia? FindSelectedMedia(RichTextBox box)
    {
        foreach (var container in FindContainers(box.Document))
        {
            if (container.Child is not ResizableMedia media) continue;
            if (box.Selection.Contains(container.ContentStart) ||
                box.Selection.Contains(container.ElementStart) ||
                IsCaretOnContainer(box.CaretPosition, container))
                return media;
        }
        return null;
    }

    private static bool IsCaretOnContainer(TextPointer caret, InlineUIContainer container)
    {
        if (caret is null) return false;
        if (caret.CompareTo(container.ElementStart) >= 0 && caret.CompareTo(container.ElementEnd) <= 0)
            return true;
        if (caret.GetAdjacentElement(LogicalDirection.Forward) == container) return true;
        if (caret.GetAdjacentElement(LogicalDirection.Backward) == container) return true;
        return false;
    }

    private static ResizableMedia? FindResizableMedia(DependencyObject? start)
    {
        while (start is not null)
        {
            if (start is ResizableMedia media) return media;
            start = start is Visual
                ? VisualTreeHelper.GetParent(start)
                : LogicalTreeHelper.GetParent(start);
        }
        return null;
    }

    private void WireMedia(ResizableMedia media)
    {
        media.RequestSelect -= OnMediaRequestSelect;
        media.RequestSelect += OnMediaRequestSelect;
    }

    private void OnMediaRequestSelect(ResizableMedia media) => SelectMedia(media);

    private void Image_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
        };
        if (dlg.ShowDialog() != true) return;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(dlg.FileName);
        bitmap.EndInit();
        bitmap.Freeze();
        InsertMedia(bitmap, Math.Min(480, bitmap.PixelWidth));
    }

    private void Latex_Click(object sender, RoutedEventArgs e)
    {
        var latex = Prompt("Enter a LaTeX equation", "E = mc^2");
        if (string.IsNullOrWhiteSpace(latex)) return;
        try
        {
            var bitmap = DocumentCodec.RenderLatex(latex, 28);
            InsertMedia(bitmap, Math.Max(80, bitmap.PixelWidth));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not render LaTeX: {ex.Message}", "LaTeX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InsertMedia(ImageSource source, double width)
    {
        Editor.Focus();
        var aspect = 1d;
        if (source is BitmapSource bmp && bmp.PixelHeight > 0)
            aspect = (double)bmp.PixelWidth / bmp.PixelHeight;
        var media = new ResizableMedia
        {
            Source = source,
            Width = width,
            Height = width / Math.Max(aspect, 0.05),
        };
        WireMedia(media);
        _ = new InlineUIContainer(media, Editor.CaretPosition) { BaselineAlignment = BaselineAlignment.Bottom };
        SelectMedia(media);
    }

    private void WrapMedia(FlowDocument document)
    {
        foreach (var container in FindContainers(document))
        {
            switch (container.Child)
            {
                case ResizableMedia existing:
                    WireMedia(existing);
                    break;
                case Image image:
                    var width = image.Width;
                    if (double.IsNaN(width) || width < 1)
                        width = image.ActualWidth > 1 ? image.ActualWidth : 240;
                    var height = image.Height;
                    var media = new ResizableMedia { Source = image.Source, Width = width };
                    if (!double.IsNaN(height) && height > 1)
                        media.Height = height;
                    WireMedia(media);
                    container.Child = media;
                    break;
            }
        }
    }

    private static void FlattenMedia(FlowDocument document)
    {
        foreach (var container in FindContainers(document))
        {
            if (container.Child is not ResizableMedia media) continue;
            container.Child = new Image
            {
                Source = media.Source,
                Width = media.ActualWidth > 1 ? media.ActualWidth : media.Width,
                Height = media.ActualHeight > 1 ? media.ActualHeight : media.Height,
                Stretch = Stretch.Fill,
            };
        }
    }

    private static List<InlineUIContainer> FindContainers(FlowDocument document)
    {
        var list = new List<InlineUIContainer>();
        foreach (var block in document.Blocks)
            Collect(block, list);
        return list;
    }

    private static void Collect(Block block, List<InlineUIContainer> list)
    {
        switch (block)
        {
            case Paragraph paragraph:
                Collect(paragraph.Inlines, list);
                break;
            case List lst:
                foreach (var item in lst.ListItems)
                foreach (var child in item.Blocks)
                    Collect(child, list);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    Collect(child, list);
                break;
        }
    }

    private static void Collect(InlineCollection inlines, List<InlineUIContainer> list)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer container:
                    list.Add(container);
                    break;
                case Span span:
                    Collect(span.Inlines, list);
                    break;
            }
        }
    }

    private void Editor_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Editor is null || e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
        if ((e.GetPosition(Editor) - _pressPoint).Length > 4) return;
        if (FindResizableMedia(e.OriginalSource as DependencyObject) is not null) return;

        var pointer = Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: true);
        var link = FindHyperlink(pointer)
            ?? FindHyperlink(pointer?.GetNextInsertionPosition(LogicalDirection.Backward))
            ?? FindHyperlink(pointer?.GetNextInsertionPosition(LogicalDirection.Forward));
        if (link is null) return;

        e.Handled = true;
        OpenHyperlink(link);
    }

    private void Editor_OnRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is Hyperlink link)
            OpenHyperlink(link);
        else if (e.Uri is not null)
            ShellLink.Open(e.Uri.ToString());
    }

    private void InsertHyperlink(string display, string target)
    {
        Editor.Focus();
        var paragraph = FindParagraph(Editor.CaretPosition);
        if (!Editor.Selection.IsEmpty)
        {
            var start = Editor.Selection.Start;
            Editor.Selection.Text = string.Empty;
            paragraph = FindParagraph(start) ?? Editor.CaretPosition.Paragraph ?? paragraph;
        }

        paragraph = EnsureParagraph(paragraph);
        var link = CreateHyperlink(display, target);
        InsertInlineAtCaret(paragraph, link);

        if (link.Parent is null)
            paragraph.Inlines.Add(link);

        EnsureLinkText(link, display);
        PlaceCaretAfter(link);
    }

    private Paragraph EnsureParagraph(Paragraph? paragraph)
    {
        if (paragraph is not null && IsAttached(paragraph))
            return paragraph;

        paragraph = FindParagraph(Editor.CaretPosition);
        if (paragraph is not null)
            return paragraph;

        paragraph = new Paragraph();
        Editor.Document.Blocks.Add(paragraph);
        return paragraph;
    }

    private static Paragraph? FindParagraph(TextPointer? pointer)
    {
        if (pointer is null) return null;
        if (pointer.Paragraph is not null) return pointer.Paragraph;

        DependencyObject? parent = pointer.Parent as DependencyObject;
        while (parent is not null)
        {
            switch (parent)
            {
                case Paragraph paragraph:
                    return paragraph;
                case ListItem item when item.Blocks.FirstBlock is Paragraph first:
                    return first;
            }

            parent = parent is TextElement text ? text.Parent : LogicalTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static bool IsAttached(TextElement element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is FlowDocument) return true;
            current = current is TextElement text ? text.Parent : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static void EnsureLinkText(Hyperlink link, string display)
    {
        if (!string.IsNullOrEmpty(new TextRange(link.ContentStart, link.ContentEnd).Text))
            return;
        link.Inlines.Clear();
        link.Inlines.Add(new Run(display));
        StyleHyperlink(link);
    }

    private static Hyperlink CreateHyperlink(string display, string target)
    {
        var link = new Hyperlink(new Run(display));
        ShellLink.Apply(link, target);
        StyleHyperlink(link);
        return link;
    }

    private void InsertInlineAtCaret(Paragraph paragraph, Inline inline)
    {
        var caret = Editor.CaretPosition;
        if (caret.Paragraph != paragraph)
        {
            paragraph.Inlines.Add(inline);
            return;
        }

        if (caret.Parent is Run run && paragraph.Inlines.Contains(run))
        {
            var text = run.Text ?? "";
            var offset = run.ContentStart.GetOffsetToPosition(caret);
            if (offset < 0) offset = 0;
            if (offset > text.Length) offset = text.Length;

            var before = text[..offset];
            var after = text[offset..];
            if (before.Length == 0 && after.Length == 0)
            {
                // Keep the list item's existing run. Removing it hides the new link.
                paragraph.Inlines.InsertAfter(run, inline);
                return;
            }

            run.Text = before;
            if (before.Length == 0)
                paragraph.Inlines.InsertBefore(run, inline);
            else
                paragraph.Inlines.InsertAfter(run, inline);

            if (after.Length > 0)
                paragraph.Inlines.InsertAfter(inline, new Run(after));
            else
                paragraph.Inlines.InsertAfter(inline, new Run(" "));
            return;
        }

        Inline? next = null;
        foreach (Inline existing in paragraph.Inlines)
        {
            if (existing.ContentStart.CompareTo(caret) >= 0)
            {
                next = existing;
                break;
            }
        }

        if (next is null)
            paragraph.Inlines.Add(inline);
        else
            paragraph.Inlines.InsertBefore(next, inline);

        if (paragraph.Inlines.LastInline == inline)
            paragraph.Inlines.Add(new Run(" "));
    }

    private void PlaceCaretAfter(Inline inline)
    {
        if (inline.Parent is not Paragraph paragraph) return;
        var next = inline.NextInline;
        if (next is null)
        {
            next = new Run(" ");
            paragraph.Inlines.InsertAfter(inline, next);
        }

        try
        {
            Editor.CaretPosition = next.ContentStart;
        }
        catch (ArgumentException)
        {
            // Leave the caret where the editor placed it.
        }
    }

    private static void ApplyHyperlink(Hyperlink link, string display, string target)
    {
        var current = new TextRange(link.ContentStart, link.ContentEnd).Text;
        if (!string.Equals(current, display, StringComparison.Ordinal))
        {
            var range = new TextRange(link.ContentStart, link.ContentEnd);
            range.Text = display;
        }

        ShellLink.Apply(link, target);
        StyleHyperlink(link);
        EnsureLinkText(link, display);
    }

    private static void RemoveHyperlink(Hyperlink link)
    {
        var text = new TextRange(link.ContentStart, link.ContentEnd).Text;
        if (link.Parent is not Paragraph paragraph) return;
        if (!string.IsNullOrEmpty(text))
            paragraph.Inlines.InsertBefore(link, new Run(text));
        paragraph.Inlines.Remove(link);
    }

    private static void StyleHyperlinks(FlowDocument document)
    {
        foreach (var block in document.Blocks)
            StyleHyperlinks(block);
    }

    private static void StyleHyperlinks(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                StyleHyperlinks(paragraph.Inlines);
                break;
            case List list:
                foreach (var item in list.ListItems)
                foreach (var child in item.Blocks)
                    StyleHyperlinks(child);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    StyleHyperlinks(child);
                break;
        }
    }

    private static void StyleHyperlinks(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Hyperlink link)
                StyleHyperlink(link);
            if (inline is Span span)
                StyleHyperlinks(span.Inlines);
        }
    }

    private static void StyleHyperlink(Hyperlink link)
    {
        link.Cursor = Cursors.Hand;
        link.TextDecorations = TextDecorations.Underline;
        var brush = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79));
        brush.Freeze();
        link.Foreground = brush;
        foreach (var inline in link.Inlines)
        {
            if (inline is not Run run) continue;
            run.Foreground = brush;
            run.TextDecorations = TextDecorations.Underline;
        }
        if (string.IsNullOrWhiteSpace(link.ToolTip as string))
            link.ToolTip = ShellLink.FromHyperlink(link);
    }

    private static string ClipboardLink()
    {
        try
        {
            if (!Clipboard.ContainsText()) return "";
            var text = Clipboard.GetText().Trim().Trim('"');
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(@"\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(text))
                return text;
        }
        catch
        {
            // Clipboard can be locked by another process.
        }

        return "";
    }

    private static Hyperlink? FindHyperlink(TextPointer? pointer)
    {
        if (pointer is null) return null;
        if (pointer.Parent is Hyperlink direct) return direct;

        DependencyObject? parent = pointer.Parent as DependencyObject;
        while (parent is not null)
        {
            if (parent is Hyperlink link) return link;
            parent = parent is TextElement text ? text.Parent : LogicalTreeHelper.GetParent(parent);
        }

        if (pointer.GetAdjacentElement(LogicalDirection.Forward) is Hyperlink forward) return forward;
        if (pointer.GetAdjacentElement(LogicalDirection.Backward) is Hyperlink backward) return backward;
        return null;
    }

    private void OpenHyperlink(Hyperlink link)
    {
        if (_openingLink) return;
        _openingLink = true;
        try
        {
            ShellLink.Open(ShellLink.FromHyperlink(link));
        }
        finally
        {
            _openingLink = false;
        }
    }

    private static bool PromptLink(ref string display, ref string target, bool editing, out bool remove)
    {
        remove = false;
        var displayBox = new TextBox { Text = display, Margin = new Thickness(0, 0, 0, 8) };
        var targetBox = new TextBox { Text = target, Margin = new Thickness(0, 0, 0, 8) };
        var fileButton = new Button { Content = "File...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var folderButton = new Button { Content = "Folder...", Padding = new Thickness(10, 4, 10, 4) };
        var browse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        browse.Children.Add(fileButton);
        browse.Children.Add(folderButton);

        var removeFlag = false;
        var ok = new Button { Content = editing ? "Update" : "Insert", IsDefault = true, MinWidth = 88, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, Padding = new Thickness(12, 4, 12, 4) };
        var removeButton = new Button { Content = "Remove", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Visibility = editing ? Visibility.Visible : Visibility.Collapsed };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(removeButton);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = "Display text", Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x67, 0x75)) });
        form.Children.Add(displayBox);
        form.Children.Add(new TextBlock { Text = "Address", Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x67, 0x75)) });
        form.Children.Add(targetBox);
        form.Children.Add(browse);
        form.Children.Add(new TextBlock
        {
            Text = "Website, local file or folder, or a network path.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x67, 0x75)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        form.Children.Add(buttons);

        var win = new Window
        {
            Title = "Hyperlink",
            Content = form,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        if (Application.Current.MainWindow is { IsLoaded: true } owner)
            win.Owner = owner;

        fileButton.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "All files|*.*", Title = "Select a file" };
            if (dlg.ShowDialog(win) != true) return;
            targetBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(displayBox.Text))
                displayBox.Text = Path.GetFileName(dlg.FileName);
        };
        folderButton.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "Select a folder" };
            if (dlg.ShowDialog(win) != true) return;
            targetBox.Text = dlg.FolderName;
            if (string.IsNullOrWhiteSpace(displayBox.Text))
                displayBox.Text = Path.GetFileName(dlg.FolderName.TrimEnd('\\', '/'));
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(targetBox.Text))
            {
                MessageBox.Show(win, "Enter a website, file, or folder address.", "Hyperlink", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            win.DialogResult = true;
        };
        removeButton.Click += (_, _) =>
        {
            removeFlag = true;
            win.DialogResult = true;
        };

        if (win.ShowDialog() != true) return false;
        remove = removeFlag;
        display = displayBox.Text.Trim();
        target = targetBox.Text.Trim();
        return true;
    }

    private static string? Prompt(string title, string defaultValue)
    {
        var input = new TextBox { Text = defaultValue, Margin = new Thickness(12), MinWidth = 360 };
        var ok = new Button { Content = "Insert", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(input);
        var win = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        string? result = null;
        ok.Click += (_, _) =>
        {
            result = input.Text;
            win.DialogResult = true;
        };
        if (Application.Current.MainWindow is { IsLoaded: true } owner)
            win.Owner = owner;
        return win.ShowDialog() == true ? result : null;
    }
}
