using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportEditor.Models;
using ReportEditor.Services;

namespace ReportEditor.ViewModels;

public partial class CreateProjectViewModel : ObservableObject
{
    private readonly ProjectStore _store;
    private readonly Action _goOverview;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _chargeNumber = "";
    [ObservableProperty] private string _materialNumber = "";
    [ObservableProperty] private string _travelNumber = "";
    [ObservableProperty] private string _customer = "";
    [ObservableProperty] private DateTime? _dueDate;

    public CreateProjectViewModel(ProjectStore store, Action goOverview)
    {
        _store = store;
        _goOverview = goOverview;
    }

    [RelayCommand]
    private void Create()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return;

        _store.AddProject(new Project
        {
            Name = Name.Trim(),
            ChargeNumber = ChargeNumber.Trim(),
            MaterialNumber = MaterialNumber.Trim(),
            TravelNumber = TravelNumber.Trim(),
            Customer = Customer.Trim(),
            DueDate = DueDate,
        });
        _goOverview();
    }

    [RelayCommand]
    private void ViewProjects() => _goOverview();

    [RelayCommand]
    private void CreateWorkspace()
    {
        if (!WorkspaceFiles.Create(_store)) return;
        _goOverview();
    }

    [RelayCommand]
    private void OpenWorkspace()
    {
        if (!WorkspaceFiles.Open(_store)) return;
        _goOverview();
    }

    [RelayCommand]
    private void SaveWorkspaceAs() => WorkspaceFiles.SaveAs(_store);
}
