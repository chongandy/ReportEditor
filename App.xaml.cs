using System.Windows;
using ReportEditor.Services;
using ReportEditor.ViewModels;

namespace ReportEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new ProjectStore();
        var window = new MainWindow
        {
            DataContext = new MainViewModel(store),
        };
        window.Show();
    }
}
