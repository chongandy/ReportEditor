using System.IO;
using System.Text.Json;
using ReportEditor.Models;

namespace ReportEditor.Services;

public class ProjectStore
{
    private readonly string _lastPathFile;
    private readonly object _gate = new();
    private List<Project> _projects = [];

    public ProjectStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReportEditor");
        Directory.CreateDirectory(dir);
        _lastPathFile = Path.Combine(dir, "last-workspace.txt");
        FilePath = ReadLastPath() ?? Path.Combine(dir, "projects.json");
        Load();
    }

    public event EventHandler? FilePathChanged;

    public string FilePath { get; private set; }

    public string FileName => Path.GetFileName(FilePath);

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

    public void CreateNew(string path)
    {
        lock (_gate)
        {
            _projects = [];
            SetPath(path);
            Save();
        }
    }

    public void Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Project.json was not found.", path);

        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.ListProject)
            ?? throw new InvalidDataException("The file is not a valid Project.json list.");

        lock (_gate)
        {
            _projects = loaded;
            SetPath(path);
        }
    }

    public void SaveAs(string path)
    {
        lock (_gate)
        {
            SetPath(path);
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        var json = File.ReadAllText(FilePath);
        _projects = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.ListProject) ?? [];
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(_projects, ProjectJsonContext.Default.ListProject);
        File.WriteAllText(FilePath, json);
    }

    private void SetPath(string path)
    {
        FilePath = Path.GetFullPath(path);
        try
        {
            File.WriteAllText(_lastPathFile, FilePath);
        }
        catch
        {
            // Last-path memory is optional.
        }
        FilePathChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? ReadLastPath()
    {
        try
        {
            if (!File.Exists(_lastPathFile)) return null;
            var path = File.ReadAllText(_lastPathFile).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }
}
