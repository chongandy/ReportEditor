using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ReportEditor.Models;

namespace ReportEditor.ViewModels;

public partial class TreeItemViewModel : ObservableObject
{
    public TreeItemViewModel(string kind, string id, string title, object model)
    {
        Kind = kind;
        Id = id;
        Title = title;
        Model = model;
    }

    public string Kind { get; }
    public string Id { get; }
    public object Model { get; }

    [ObservableProperty]
    private string _title = "";

    public bool CanRemove => Kind is "project" or "report" or "followup";
    public bool IsProject => Kind == "project";

    public ObservableCollection<TreeItemViewModel> Children { get; } = [];
}

public partial class MilestoneRow : ObservableObject
{
    public MilestoneRow(Milestone model) => Model = model;

    public Milestone Model { get; }

    [ObservableProperty] private int _no;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private DateTime? _due;
}

public partial class FollowUpRow : ObservableObject
{
    public FollowUpRow() { }

    public FollowUpRow(FollowUpItem item)
    {
        Id = item.Id;
        No = item.No;
        Task = item.Task;
        Owner = item.Owner;
        Due = item.Due;
        Status = NormalizeStatus(item.Status);
        SourceReportId = item.SourceReportId;
        SourceMilestoneId = item.SourceMilestoneId;
        SourceReportTitle = item.SourceReportTitle;
        WorkReportId = item.WorkReportId;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private int _no;
    [ObservableProperty] private string _task = "";
    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private DateTime? _due;
    [ObservableProperty] private string _status = "Open";
    public string SourceReportId { get; set; } = "";
    public string SourceMilestoneId { get; set; } = "";
    [ObservableProperty] private string _sourceReportTitle = "";
    public string WorkReportId { get; set; } = "";

    public FollowUpItem ToModel() => new()
    {
        Id = Id,
        No = No,
        Task = Task,
        Owner = Owner,
        Due = Due,
        Status = NormalizeStatus(Status),
        SourceReportId = SourceReportId,
        SourceMilestoneId = SourceMilestoneId,
        SourceReportTitle = SourceReportTitle,
        WorkReportId = WorkReportId,
    };

    private static string NormalizeStatus(string? status) => status switch
    {
        "In Progress" or "In-Progress" => "In-Progress",
        "Done" or "Completed" => "Completed",
        _ => "Open",
    };
}

public sealed class ProjectOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    public override string ToString() => Name;
}

public sealed class TodoTaskOption
{
    public string Id { get; init; } = "";
    public string Task { get; init; } = "";

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Task) ? "(untitled task)" : Task;
}

public partial class TodoRow : ObservableObject
{
    public TodoRow() { }

    public TodoRow(TodoItem item)
    {
        Id = item.Id;
        Task = item.Task;
        ProjectId = item.ProjectId;
        Priority = NormalizePriority(item.Priority);
        DueDate = item.DueDate;
        Status = NormalizeTodoStatus(item.Status);
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _task = "";
    [ObservableProperty] private string _projectId = "";
    [ObservableProperty] private string _priority = "Medium";
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private string _status = "Open";

    public TodoItem ToModel() => new()
    {
        Id = Id,
        Task = Task,
        ProjectId = ProjectId,
        Priority = NormalizePriority(Priority),
        DueDate = DueDate,
        Status = NormalizeTodoStatus(Status),
    };

    private static string NormalizePriority(string? priority) => priority switch
    {
        "High" => "High",
        "Low" => "Low",
        _ => "Medium",
    };

    private static string NormalizeTodoStatus(string? status) => status switch
    {
        "In-Progress" or "In Progress" => "In Progress",
        "Pending" => "Pending",
        "Completed" or "Done" => "Completed",
        _ => "Open",
    };
}
