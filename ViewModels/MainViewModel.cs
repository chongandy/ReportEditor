using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        _store.FilePathChanged += (_, _) => OnPropertyChanged(nameof(WorkspaceTitle));
        CurrentViewModel = new CreateProjectViewModel(store, ShowOverview);
    }

    public string WorkspaceTitle => $"Report Editor — {_store.FileName}";

    public void ShowOverview()
    {
        CurrentViewModel = new OverviewViewModel(_store, ShowCreate);
    }

    public void ShowCreate()
    {
        CurrentViewModel = new CreateProjectViewModel(_store, ShowOverview);
    }

    [RelayCommand]
    private void CreateWorkspace()
    {
        if (!WorkspaceFiles.Create(_store)) return;
        ShowOverview();
    }

    [RelayCommand]
    private void OpenWorkspace()
    {
        if (!WorkspaceFiles.Open(_store)) return;
        ShowOverview();
    }

    [RelayCommand]
    private void SaveWorkspaceAs()
    {
        WorkspaceFiles.SaveAs(_store);
    }
}
