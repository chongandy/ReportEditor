using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportEditor.Models;
using ReportEditor.Services;

namespace ReportEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProjectStore _store;

    [ObservableProperty]
    private object _currentViewModel;

    public MainViewModel(ProjectStore store)
    {
        _store = store;
        CurrentViewModel = new CreateProjectViewModel(store, ShowOverview);
    }

    public void ShowOverview()
    {
        CurrentViewModel = new OverviewViewModel(_store, ShowCreate);
    }

    public void ShowCreate()
    {
        CurrentViewModel = new CreateProjectViewModel(_store, ShowOverview);
    }
}
