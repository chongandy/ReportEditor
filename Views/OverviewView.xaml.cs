using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ReportEditor.ViewModels;

namespace ReportEditor.Views;

public partial class OverviewView
{
    public OverviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ApplyTodoExpandedHeight();
    }

    private OverviewViewModel? _vm;
    private double _pdfColumnWidth = 360;
    private double _todoExpandedHeight = 220;
    private bool _todoCollapsedBySplitter;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.RequestClearEditor -= ClearEditor;
            _vm.RequestLoadReport -= LoadReport;
            _vm.RequestExportEditor -= ExportEditor;
            _vm.RequestAppendEditor -= AppendEditor;
        }

        _vm = e.NewValue as OverviewViewModel;
        if (_vm is null) return;
        _vm.RequestClearEditor += ClearEditor;
        _vm.RequestLoadReport += LoadReport;
        _vm.RequestExportEditor += ExportEditor;
        _vm.RequestAppendEditor += AppendEditor;
    }

    private void ClearEditor()
    {
        if (Editor is null) return;
        Dispatcher.Invoke(() => Editor.Clear());
    }

    private void AppendEditor(string text)
    {
        if (Editor is null) return;
        Dispatcher.Invoke(() => Editor.Append(text));
    }

    private void LoadReport(string? package)
    {
        if (Editor is null) return;
        Dispatcher.Invoke(() => Editor.Load(package));
    }

    private string ExportEditor() => Editor is null ? "" : Editor.Export();

    private void PdfExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (PdfColumn.ActualWidth > 48)
            _pdfColumnWidth = PdfColumn.ActualWidth;
        PdfColumn.MinWidth = 32;
        PdfColumn.Width = GridLength.Auto;
    }

    private void PdfExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        PdfColumn.MinWidth = 220;
        PdfColumn.Width = new GridLength(Math.Max(220, _pdfColumnWidth));
    }

    private void TodoSplitter_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_todoCollapsedBySplitter || TodoExpander is { IsExpanded: false })
        {
            TodoExpander.IsExpanded = true;
            ApplyTodoExpandedHeight();
            e.Handled = true;
            return;
        }

        RememberTodoHeight();
        TodoExpander.IsExpanded = false;
        CollapseTodoToHeader();
        e.Handled = true;
    }

    private void TodoExpander_OnCollapsed(object sender, RoutedEventArgs e) => CollapseTodoToHeader();

    private void TodoExpander_OnExpanded(object sender, RoutedEventArgs e) => ApplyTodoExpandedHeight();

    private void CollapseTodoToHeader()
    {
        RememberTodoHeight();
        _todoCollapsedBySplitter = true;
        TodoRow.MinHeight = 36;
        TodoRow.Height = GridLength.Auto;
    }

    private void ApplyTodoExpandedHeight()
    {
        if (TodoExpander is { IsExpanded: false }) return;
        var parentHeight = CenterPanelGrid?.ActualHeight ?? 0;
        if (parentHeight < 1)
            parentHeight = ActualHeight;
        var target = parentHeight > 1
            ? Math.Max(120, parentHeight * 0.30)
            : Math.Max(120, _todoExpandedHeight);
        _todoExpandedHeight = target;
        _todoCollapsedBySplitter = false;
        TodoRow.MinHeight = 100;
        TodoRow.Height = new GridLength(target);
    }

    private void RememberTodoHeight()
    {
        if (TodoRow.ActualHeight > 80 && TodoExpander is { IsExpanded: true })
            _todoExpandedHeight = TodoRow.ActualHeight;
    }

    private void ProjectTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is OverviewViewModel vm && e.NewValue is TreeItemViewModel node)
            vm.SelectNode(node);
    }

    private void MilestoneGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is OverviewViewModel vm)
            Dispatcher.BeginInvoke(vm.PersistMilestones);
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
