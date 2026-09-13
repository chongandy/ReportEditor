using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ReportEditor.Services;

public static class WorkspaceFiles
{
    private const string Filter = "Project JSON (Project.json)|Project.json;*.json|JSON files (*.json)|*.json";

    public static bool Create(ProjectStore store)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Create Project.json",
            Filter = Filter,
            FileName = "Project.json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return false;

        if (File.Exists(dlg.FileName) &&
            MessageBox.Show(
                "This file already exists. Replace it with an empty project list?",
                "Create Project.json",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;

        store.CreateNew(dlg.FileName);
        return true;
    }

    public static bool Open(ProjectStore store)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Project.json",
            Filter = Filter,
            FileName = "Project.json",
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            store.Open(dlg.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Project.json:\n{ex.Message}", "Open Project.json",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public static bool SaveAs(ProjectStore store)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Project.json As",
            Filter = Filter,
            FileName = string.IsNullOrWhiteSpace(store.FileName) ? "Project.json" : store.FileName,
            DefaultExt = ".json",
            AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(store.FilePath),
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            store.SaveAs(dlg.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save Project.json:\n{ex.Message}", "Save As",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
