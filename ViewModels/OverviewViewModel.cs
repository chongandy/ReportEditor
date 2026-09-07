using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportEditor.Models;
using ReportEditor.Services;
using System.Windows;

namespace ReportEditor.ViewModels;

public partial class OverviewViewModel : ObservableObject
{
    private readonly ProjectStore _store;
    private readonly Action _goCreate;
    private string? _editingMilestoneId;
    private FollowUpRow? _workSeed;
    private string? _pendingRelatedFollowUpId;
    private bool _syncingTitle;

    [ObservableProperty] private ObservableCollection<TreeItemViewModel> _tree = [];
    [ObservableProperty] private TreeItemViewModel? _selectedNode;
    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private Milestone? _selectedMilestone;
    [ObservableProperty] private Report? _selectedReport;
    [ObservableProperty] private ObservableCollection<MilestoneRow> _milestones = [];
    [ObservableProperty] private ObservableCollection<FollowUpRow> _projectFollowUps = [];
    [ObservableProperty] private ObservableCollection<FollowUpRow> _draftFollowUps = [];
    [ObservableProperty] private string _draftTitle = "";
    [ObservableProperty] private bool _showProjectPanel;
    [ObservableProperty] private bool _showEditorPanel;
    [ObservableProperty] private bool _showDraftFollowUps;

    public OverviewViewModel(ProjectStore store, Action goCreate)
    {
        _store = store;
        _goCreate = goCreate;
        RebuildTree();
        if (Tree.Count > 0)
            SelectNode(Tree[0]);
    }

    public IReadOnlyList<string> StatusOptions { get; } = ["Open", "In-Progress", "Completed"];

    [RelayCommand]
    private void NewProject() => _goCreate();

    public void SelectNode(TreeItemViewModel? node)
    {
        SelectedNode = node;
        ShowProjectPanel = ShowEditorPanel = false;
        ShowDraftFollowUps = false;
        SelectedProject = null;
        SelectedMilestone = null;
        SelectedReport = null;

        if (node is null) return;

        switch (node.Kind)
        {
            case "project":
                ShowProject(node);
                break;
            case "milestone":
                BeginEditor(node);
                break;
            case "report":
                ShowReport(node);
                break;
            case "followup":
                OpenFollowUp((FollowUpItem)node.Model);
                break;
        }
    }

    private void ShowProject(TreeItemViewModel node)
    {
        var project = (Project)node.Model;
        SelectedProject = project;
        ShowProjectPanel = true;
        Milestones = new ObservableCollection<MilestoneRow>(
            project.Milestones.Select(m => new MilestoneRow(m)
            {
                No = m.No,
                Name = m.Name,
                Due = m.Due,
            }));
        var followUps = project.Milestones
            .SelectMany(m => m.Reports.SelectMany(r => r.FollowUps.Select(f =>
            {
                var row = new FollowUpRow(f);
                if (string.IsNullOrWhiteSpace(row.SourceReportId))
                    row.SourceReportId = r.Id;
                if (string.IsNullOrWhiteSpace(row.SourceMilestoneId))
                    row.SourceMilestoneId = m.Id;
                if (string.IsNullOrWhiteSpace(row.SourceReportTitle))
                    row.SourceReportTitle = r.Title;
                if (string.IsNullOrWhiteSpace(row.WorkReportId))
                    row.WorkReportId = f.WorkReportId;
                return row;
            })))
            .ToList();
        for (var i = 0; i < followUps.Count; i++)
            followUps[i].No = i + 1;
        ProjectFollowUps = new ObservableCollection<FollowUpRow>(followUps);
        _editingMilestoneId = null;
    }

    private void BeginEditor(TreeItemViewModel node)
    {
        var milestone = (Milestone)node.Model;
        var project = FindProject(milestone.Id);
        SelectedProject = project;
        SelectedMilestone = milestone;
        ShowEditorPanel = true;
        if (_workSeed is not null)
        {
            var seed = _workSeed;
            _workSeed = null;
            _editingMilestoneId = milestone.Id;
            _pendingRelatedFollowUpId = seed.Id;
            _syncingTitle = true;
            DraftTitle = string.IsNullOrWhiteSpace(seed.Task) ? "Untitled task" : seed.Task;
            _syncingTitle = false;
            DraftFollowUps = [];
            ShowDraftFollowUps = false;
            RequestClearEditor?.Invoke();
            RequestAppendEditor?.Invoke(
                $"Follow-up work for: {(string.IsNullOrWhiteSpace(seed.Task) ? "(untitled task)" : seed.Task)}\n" +
                $"Owner: {seed.Owner}\n" +
                $"Due: {(seed.Due is { } d ? d.ToString("d") : "")}\n" +
                $"Source report: {seed.SourceReportTitle}\n\n" +
                "Work notes:\n");
            return;
        }

        if (_editingMilestoneId != milestone.Id)
        {
            _editingMilestoneId = milestone.Id;
            DraftTitle = $"{DateTime.Now:yyyy-MM-dd} — {(string.IsNullOrWhiteSpace(milestone.Name) ? "Report" : milestone.Name)}";
            DraftFollowUps = [];
            _pendingRelatedFollowUpId = null;
            ShowDraftFollowUps = true;
            RequestClearEditor?.Invoke();
        }
    }

    private void ShowReport(TreeItemViewModel node)
    {
        var report = (Report)node.Model;
        var located = LocateReport(report.Id);
        if (located is null) return;

        SelectedProject = located.Value.Project;
        SelectedMilestone = located.Value.Milestone;
        SelectedReport = report;
        ShowEditorPanel = true;
        _editingMilestoneId = null;
        _pendingRelatedFollowUpId = report.RelatedFollowUpId;
        _syncingTitle = true;
        DraftTitle = report.Title;
        _syncingTitle = false;
        var isWorkReport = !string.IsNullOrWhiteSpace(report.RelatedFollowUpId);
        ShowDraftFollowUps = !isWorkReport;
        DraftFollowUps = isWorkReport
            ? []
            : new ObservableCollection<FollowUpRow>(report.FollowUps.Select(f => new FollowUpRow(f)));
        RequestLoadReport?.Invoke(report.BodyPackageBase64);
    }

    public event Action? RequestClearEditor;
    public event Action<string>? RequestAppendEditor;
    public event Action<string?>? RequestLoadReport;
    public event Func<string>? RequestExportEditor;

    partial void OnDraftTitleChanged(string value)
    {
        if (_syncingTitle || SelectedReport is null) return;
        ApplyReportTitle(SelectedReport.Id, value);
        if (!string.IsNullOrWhiteSpace(SelectedReport.RelatedFollowUpId))
            SyncFollowUpTaskAndWorkTitle(SelectedReport.RelatedFollowUpId, value);
    }

    partial void OnDraftFollowUpsChanged(ObservableCollection<FollowUpRow>? oldValue, ObservableCollection<FollowUpRow> newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CollectionChanged -= DraftFollowUpsOnCollectionChanged;
            foreach (var row in oldValue)
                row.PropertyChanged -= OnDraftFollowUpPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.CollectionChanged += DraftFollowUpsOnCollectionChanged;
            foreach (var row in newValue)
                row.PropertyChanged += OnDraftFollowUpPropertyChanged;
        }
    }

    private void DraftFollowUpsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FollowUpRow row in e.OldItems)
                row.PropertyChanged -= OnDraftFollowUpPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (FollowUpRow row in e.NewItems)
                row.PropertyChanged += OnDraftFollowUpPropertyChanged;
        }
    }

    private void OnDraftFollowUpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingTitle || sender is not FollowUpRow row) return;
        if (e.PropertyName != nameof(FollowUpRow.Task)) return;
        SyncFollowUpTaskAndWorkTitle(row.Id, row.Task);
    }

    private void SyncFollowUpTaskAndWorkTitle(string followUpId, string text)
    {
        if (string.IsNullOrWhiteSpace(followUpId)) return;
        var name = text ?? "";
        _syncingTitle = true;
        try
        {
            string? workReportId = null;
            foreach (var project in _store.Projects)
            {
                var changed = false;
                foreach (var milestone in project.Milestones)
                {
                    foreach (var report in milestone.Reports)
                    {
                        if (report.RelatedFollowUpId == followUpId)
                            workReportId = report.Id;

                        foreach (var followUp in report.FollowUps)
                        {
                            if (followUp.Id != followUpId) continue;
                            followUp.Task = name;
                            changed = true;
                            if (!string.IsNullOrWhiteSpace(followUp.WorkReportId))
                                workReportId = followUp.WorkReportId;
                        }
                    }
                }

                if (changed)
                    _store.UpdateProject(project);
            }

            var title = string.IsNullOrWhiteSpace(name) ? "Untitled task" : name.TrimEnd();
            if (!string.IsNullOrWhiteSpace(workReportId) &&
                (SelectedReport is null || SelectedReport.Id != workReportId || SelectedReport.Title != title))
            {
                ApplyReportTitle(workReportId, title);
            }
            else if (!string.IsNullOrWhiteSpace(workReportId) && FindNode("report", workReportId) is { } node)
            {
                node.Title = title;
            }

            if (SelectedReport?.RelatedFollowUpId == followUpId && DraftTitle != title)
                DraftTitle = title;

            foreach (var row in ProjectFollowUps)
            {
                if (row.Id == followUpId && row.Task != name)
                    row.Task = name;
            }

            if (FindNode("followup", followUpId) is { } followUpNode)
                followUpNode.Title = string.IsNullOrWhiteSpace(name) ? "Follow-up" : name;
        }
        finally
        {
            _syncingTitle = false;
        }
    }

    private void ApplyReportTitle(string reportId, string newTitle)
    {
        var title = string.IsNullOrWhiteSpace(newTitle) ? "Untitled report" : newTitle.Trim();
        var located = LocateReport(reportId);
        if (located is null) return;

        var report = located.Value.Milestone.Reports.FirstOrDefault(r => r.Id == reportId);
        if (report is null) return;

        var oldTitle = report.Title;
        report.Title = title;

        foreach (var project in _store.Projects)
        {
            foreach (var milestone in project.Milestones)
            {
                foreach (var item in milestone.Reports)
                {
                    foreach (var followUp in item.FollowUps)
                    {
                        if (followUp.SourceReportId == reportId ||
                            (string.IsNullOrWhiteSpace(followUp.SourceReportId) && followUp.SourceReportTitle == oldTitle))
                        {
                            followUp.SourceReportTitle = title;
                        }
                    }
                }
            }
        }

        if (FindNode("report", reportId) is { } treeNode)
            treeNode.Title = title;

        foreach (var row in ProjectFollowUps)
        {
            if (row.SourceReportId == reportId ||
                (string.IsNullOrWhiteSpace(row.SourceReportId) && row.SourceReportTitle == oldTitle))
            {
                row.SourceReportTitle = title;
            }
        }

        foreach (var row in DraftFollowUps)
        {
            if (row.SourceReportId == reportId ||
                string.IsNullOrWhiteSpace(row.SourceReportId) ||
                row.SourceReportTitle == oldTitle)
            {
                row.SourceReportTitle = title;
            }
        }

        _store.UpdateProject(located.Value.Project);
    }

    [RelayCommand]
    private void AddMilestone()
    {
        if (SelectedProject is null) return;
        var row = new MilestoneRow(new Milestone { Id = Guid.NewGuid().ToString("N") })
        {
            No = Milestones.Count + 1,
            Name = "",
        };
        Milestones.Add(row);
        PersistMilestones();
    }

    [RelayCommand]
    private void RemoveMilestone(MilestoneRow? row)
    {
        if (row is null || SelectedProject is null) return;
        Milestones.Remove(row);
        PersistMilestones();
    }

    public void PersistMilestones()
    {
        if (SelectedProject is null) return;
        var kept = new List<Milestone>();
        for (var i = 0; i < Milestones.Count; i++)
        {
            var row = Milestones[i];
            row.No = i + 1;
            var existing = SelectedProject.Milestones.FirstOrDefault(m => m.Id == row.Model.Id);
            var ms = existing ?? row.Model;
            ms.No = row.No;
            ms.Name = row.Name;
            ms.Due = row.Due;
            kept.Add(ms);
        }
        SelectedProject.Milestones = kept;
        _store.UpdateProject(SelectedProject);
        RebuildTree(SelectedProject.Id, null, null);
    }

    [RelayCommand]
    private void AddDraftFollowUp()
    {
        if (!ShowDraftFollowUps) return;
        DraftFollowUps.Add(new FollowUpRow { No = DraftFollowUps.Count + 1 });
    }

    [RelayCommand]
    private void RemoveDraftFollowUp(FollowUpRow? row)
    {
        if (row is null) return;
        DraftFollowUps.Remove(row);
        for (var i = 0; i < DraftFollowUps.Count; i++)
            DraftFollowUps[i].No = i + 1;
    }

    [RelayCommand]
    private void Submit()
    {
        if (SelectedProject is null || SelectedMilestone is null) return;
        var body = RequestExportEditor?.Invoke() ?? "";
        var title = string.IsNullOrWhiteSpace(DraftTitle) ? "Untitled report" : DraftTitle.Trim();

        Report report;
        if (SelectedReport is not null && SelectedMilestone.Reports.Any(r => r.Id == SelectedReport.Id))
        {
            report = SelectedReport;
            ApplyReportTitle(report.Id, title);
            report.BodyPackageBase64 = body;
        }
        else
        {
            report = new Report
            {
                Title = title,
                BodyPackageBase64 = body,
                RelatedFollowUpId = _pendingRelatedFollowUpId,
            };
            SelectedMilestone.Reports.Add(report);
        }

        report.FollowUps = ShowDraftFollowUps
            ? DraftFollowUps.Select((r, i) =>
            {
                var item = r.ToModel();
                item.No = i + 1;
                item.SourceReportId = report.Id;
                item.SourceMilestoneId = SelectedMilestone.Id;
                item.SourceReportTitle = report.Title;
                return item;
            }).ToList()
            : [];

        _pendingRelatedFollowUpId = report.RelatedFollowUpId;
        if (!string.IsNullOrWhiteSpace(report.RelatedFollowUpId))
            LinkWorkReport(report.RelatedFollowUpId, report.Id);
        _store.UpdateProject(SelectedProject);
        _editingMilestoneId = null;
        RebuildTree(SelectedProject.Id, SelectedMilestone.Id, report.Id);
        var reportNode = FindNode("report", report.Id);
        if (reportNode is not null)
            SelectNode(reportNode);
    }

    [RelayCommand]
    private void Cancel()
    {
        _editingMilestoneId = null;
        _pendingRelatedFollowUpId = null;
        _workSeed = null;
        RequestClearEditor?.Invoke();
        if (SelectedProject is not null)
        {
            var projectNode = FindNode("project", SelectedProject.Id);
            if (projectNode is not null)
                SelectNode(projectNode);
        }
    }

    [RelayCommand]
    private void ExportProjectPdf(TreeItemViewModel? node) => ExportProject(node, "pdf");

    [RelayCommand]
    private void ExportProjectDocx(TreeItemViewModel? node) => ExportProject(node, "docx");

    private void ExportProject(TreeItemViewModel? node, string format)
    {
        if (node is null || node.Kind != "project" || node.Model is not Project project)
            return;

        var dlg = new SaveFileDialog
        {
            FileName = SanitizeFileName(project.Name),
            Filter = format == "pdf"
                ? "PDF documents (*.pdf)|*.pdf"
                : "Word documents (*.docx)|*.docx",
            DefaultExt = format == "pdf" ? ".pdf" : ".docx",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var model = ProjectExportService.Build(project);
            if (format == "pdf")
                ProjectExportService.ExportPdf(model, dlg.FileName);
            else
                ProjectExportService.ExportDocx(model, dlg.FileName);

            MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "project-export" : cleaned.Trim();
    }

    [RelayCommand]
    private void RemoveReport(TreeItemViewModel? node)
    {
        if (node is null) return;

        if (node.Kind == "project")
        {
            RemoveProjectNode(node);
            return;
        }

        if (node.Kind == "followup")
        {
            RemoveFollowUpNode(node);
            return;
        }

        if (node.Kind != "report") return;
        if (MessageBox.Show(
                $"Remove report \"{node.Title}\" from this milestone?",
                "Remove report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var located = LocateReport(node.Id);
        if (located is null) return;
        located.Value.Milestone.Reports.RemoveAll(r => r.Id == node.Id);
        _store.UpdateProject(located.Value.Project);
        RebuildTree();
        var milestoneNode = FindNode("milestone", located.Value.Milestone.Id);
        var projectNode = FindNode("project", located.Value.Project.Id);
        SelectNode(milestoneNode ?? projectNode);
    }

    [RelayCommand]
    private void RemoveSelectedProject()
    {
        if (SelectedProject is null) return;
        var node = FindNode("project", SelectedProject.Id);
        if (node is not null)
            RemoveProjectNode(node);
    }

    private void RemoveProjectNode(TreeItemViewModel node)
    {
        if (MessageBox.Show(
                $"Remove project \"{node.Title}\" and all of its milestones and reports?",
                "Remove project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _store.RemoveProject(node.Id);
        RebuildTree();
        if (Tree.Count > 0)
            SelectNode(Tree[0]);
        else
        {
            SelectedProject = null;
            SelectedMilestone = null;
            SelectedReport = null;
            ShowProjectPanel = ShowEditorPanel = false;
            ProjectFollowUps = [];
            Milestones = [];
        }
    }

    private void RemoveFollowUpNode(TreeItemViewModel node)
    {
        if (MessageBox.Show(
                $"Remove follow-up \"{node.Title}\" from this milestone?",
                "Remove follow-up",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        FollowUpItem? followUp = node.Model as FollowUpItem;
        if (followUp is null) return;

        foreach (var project in _store.Projects)
        {
            foreach (var milestone in project.Milestones)
            {
                foreach (var report in milestone.Reports)
                    report.FollowUps.RemoveAll(f => f.Id == followUp.Id);

                milestone.Reports.RemoveAll(r => r.RelatedFollowUpId == followUp.Id || r.Id == followUp.WorkReportId);
            }
            _store.UpdateProject(project);
        }

        RebuildTree();
        var milestoneNode = FindNode("milestone", followUp.SourceMilestoneId);
        var projectNode = FindNode("project", SelectedProject?.Id ?? "");
        SelectNode(milestoneNode ?? projectNode);
    }

    private void OpenFollowUp(FollowUpItem followUp)
    {
        WorkFollowUp(new FollowUpRow(followUp));
    }

    [RelayCommand]
    private void OpenSource(FollowUpRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.SourceReportId)) return;
        var node = FindNode("report", row.SourceReportId);
        if (node is not null)
            SelectNode(node);
    }

    [RelayCommand]
    private void WorkFollowUp(FollowUpRow? row)
    {
        if (row is null) return;

        var workReportId = FindWorkReportId(row);
        if (!string.IsNullOrWhiteSpace(workReportId))
        {
            _workSeed = null;
            var workNode = FindNode("report", workReportId);
            if (workNode is not null)
            {
                SelectNode(workNode);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(row.SourceMilestoneId)) return;
        _workSeed = row;
        _editingMilestoneId = null;
        var milestoneNode = FindNode("milestone", row.SourceMilestoneId);
        if (milestoneNode is not null)
            SelectNode(milestoneNode);
    }

    private string? FindWorkReportId(FollowUpRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.WorkReportId) && FindNode("report", row.WorkReportId) is not null)
            return row.WorkReportId;

        foreach (var project in _store.Projects)
        {
            foreach (var milestone in project.Milestones)
            {
                var match = milestone.Reports.FirstOrDefault(r => r.RelatedFollowUpId == row.Id);
                if (match is not null)
                    return match.Id;
            }
        }

        return null;
    }

    private void LinkWorkReport(string followUpId, string workReportId)
    {
        foreach (var project in _store.Projects)
        {
            foreach (var milestone in project.Milestones)
            {
                foreach (var report in milestone.Reports)
                {
                    foreach (var followUp in report.FollowUps)
                    {
                        if (followUp.Id != followUpId) continue;
                        followUp.WorkReportId = workReportId;
                    }
                }
            }
        }
    }

    private (Project Project, Milestone Milestone)? LocateReport(string reportId)
    {
        foreach (var project in _store.Projects)
        {
            var milestone = project.Milestones.FirstOrDefault(m => m.Reports.Any(r => r.Id == reportId));
            if (milestone is not null)
                return (project, milestone);
        }
        return null;
    }

    private void RebuildTree(string? selectProjectId = null, string? selectMilestoneId = null, string? selectReportId = null)
    {
        Tree = new ObservableCollection<TreeItemViewModel>(
            _store.Projects.Select(p =>
            {
                var pn = new TreeItemViewModel("project", p.Id, string.IsNullOrWhiteSpace(p.Name) ? "Untitled project" : p.Name, p);
                foreach (var m in p.Milestones)
                {
                    var title = $"{m.No}. {(string.IsNullOrWhiteSpace(m.Name) ? "Untitled milestone" : m.Name)}";
                    var mn = new TreeItemViewModel("milestone", m.Id, title, m);
                    foreach (var r in m.Reports.Where(rep => string.IsNullOrWhiteSpace(rep.RelatedFollowUpId)))
                    {
                        var reportNode = new TreeItemViewModel(
                            "report",
                            r.Id,
                            string.IsNullOrWhiteSpace(r.Title) ? "Report" : r.Title,
                            r);
                        foreach (var followUp in r.FollowUps)
                        {
                            var followUpNode = new TreeItemViewModel(
                                "followup",
                                followUp.Id,
                                string.IsNullOrWhiteSpace(followUp.Task) ? "Follow-up" : followUp.Task,
                                followUp);
                            var work = m.Reports.FirstOrDefault(w =>
                                w.Id == followUp.WorkReportId || w.RelatedFollowUpId == followUp.Id);
                            if (work is not null)
                            {
                                followUpNode.Children.Add(new TreeItemViewModel(
                                    "report",
                                    work.Id,
                                    string.IsNullOrWhiteSpace(work.Title) ? "Work report" : work.Title,
                                    work));
                            }
                            reportNode.Children.Add(followUpNode);
                        }
                        mn.Children.Add(reportNode);
                    }
                    pn.Children.Add(mn);
                }
                return pn;
            }));
    }

    private TreeItemViewModel? FindNode(string kind, string id) => FindNode(Tree, kind, id);

    private static TreeItemViewModel? FindNode(IEnumerable<TreeItemViewModel> nodes, string kind, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == kind && node.Id == id) return node;
            var child = FindNode(node.Children, kind, id);
            if (child is not null) return child;
        }
        return null;
    }

    private Project? FindProject(string milestoneId) =>
        _store.Projects.FirstOrDefault(p => p.Milestones.Any(m => m.Id == milestoneId));
}
