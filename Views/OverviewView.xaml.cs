using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ReportEditor.ViewModels;

namespace ReportEditor.Views;

public partial class OverviewView
{
    public OverviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private OverviewViewModel? _vm;

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
