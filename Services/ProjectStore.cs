using System.IO;
using System.Text.Json;
using ReportEditor.Models;

namespace ReportEditor.Services;

public class ProjectStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private List<Project> _projects = [];

    public ProjectStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReportEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "projects.json");
        Load();
    }

    public IReadOnlyList<Project> Projects
    {
        get
        {
            lock (_gate) return _projects.ToList();
        }
    }

    public Project AddProject(Project project)
    {
        lock (_gate)
        {
            _projects.Insert(0, project);
            Save();
            return project;
        }
    }

    public void UpdateProject(Project project)
    {
        lock (_gate)
        {
            var i = _projects.FindIndex(p => p.Id == project.Id);
            if (i >= 0)
            {
                _projects[i] = project;
                Save();
            }
        }
    }

    public bool RemoveProject(string id)
    {
        lock (_gate)
        {
            var removed = _projects.RemoveAll(p => p.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        var json = File.ReadAllText(_path);
        _projects = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.ListProject) ?? [];
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_projects, ProjectJsonContext.Default.ListProject);
        File.WriteAllText(_path, json);
    }
}
