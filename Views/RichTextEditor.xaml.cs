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

    public RichTextEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Editor is null) return;
            Editor.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Editor_OnPreviewMouseLeftButtonDown), true);
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
        return payload;
    }

    public void Load(string? package)
    {
        if (Editor is null) return;
        Editor.Document = DocumentCodec.CreateDocument();
        DocumentCodec.Import(Editor.Document, package);
        WrapMedia(Editor.Document);
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
