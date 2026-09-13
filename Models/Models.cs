using System.Text.Json.Serialization;

namespace ReportEditor.Models;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ChargeNumber { get; set; } = "";
    public string MaterialNumber { get; set; } = "";
    public string TravelNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Milestone> Milestones { get; set; } = [];
}

public class Milestone
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int No { get; set; }
    public string Name { get; set; } = "";
    public DateTime? Due { get; set; }
    public List<Report> Reports { get; set; } = [];
}

public class Report
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string BodyPackageBase64 { get; set; } = "";
    public string? RelatedFollowUpId { get; set; }
    public List<FollowUpItem> FollowUps { get; set; } = [];
}

public class FollowUpItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int No { get; set; }
    public string Task { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime? Due { get; set; }
    public string Status { get; set; } = "Open";
    public string SourceReportId { get; set; } = "";
    public string SourceMilestoneId { get; set; } = "";
    public string SourceReportTitle { get; set; } = "";
    public string WorkReportId { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Project>))]
internal partial class ProjectJsonContext : JsonSerializerContext;
