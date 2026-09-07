using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace ReportEditor.Services;

public static class ShellLink
{
    public static void Apply(Hyperlink link, string target)
    {
        var value = target.Trim();
        link.Tag = value;
        link.ToolTip = value;
        // An http/https NavigateUri makes RichTextBox render the hyperlink with no text.
        // File and folder links can keep a file URI; websites are opened from Tag.
        if (TryCreateNavigateUri(value, out var uri) && uri.IsFile)
            link.NavigateUri = uri;
        else if (link.NavigateUri is not null)
            link.NavigateUri = null;
    }

    public static string FromHyperlink(Hyperlink link)
    {
        if (link.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            return tag;
        if (link.ToolTip is string tip && !string.IsNullOrWhiteSpace(tip))
            return tip;
        return link.NavigateUri?.IsFile == true
            ? link.NavigateUri.LocalPath
            : link.NavigateUri?.ToString() ?? "";
    }

    public static string DisplayName(string target)
    {
        var value = target.Trim().Trim('"');
        if (LooksLikeWeb(value) || value.Contains("://", StringComparison.OrdinalIgnoreCase))
            return value;
        var name = Path.GetFileName(value.TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? value : name;
    }

    public static void Open(string? target)
    {
        var value = (target ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value)) return;

        try
        {
            if (TryCreateNavigateUri(value, out var uri) && IsWeb(uri))
            {
                Start(uri.AbsoluteUri);
                return;
            }

            var path = ToLocalPath(value, uri);
            if (Directory.Exists(path) || LooksLikeFolder(path))
            {
                OpenFolder(path);
                return;
            }

            if (File.Exists(path) || LooksLikeFile(path))
            {
                Start(path);
                return;
            }

            if (LooksLikeWeb(value))
            {
                Start(value.Contains("://", StringComparison.OrdinalIgnoreCase) ? value : "https://" + value);
                return;
            }

            MessageBox.Show($"Could not open:\n{value}", "Hyperlink", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open:\n{value}\n\n{ex.Message}", "Hyperlink", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static bool TryCreateNavigateUri(string target, out Uri uri)
    {
        var value = target.Trim().Trim('"');
        if (value.StartsWith(@"\\", StringComparison.Ordinal))
            return Uri.TryCreate(value, UriKind.Absolute, out uri!);

        if (LooksLikeWeb(value) && !value.Contains("://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value;

        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (uri.IsFile || IsWeb(uri)))
            return true;

        if (Path.IsPathRooted(value) && Uri.TryCreate(value, UriKind.Absolute, out uri!))
            return true;

        uri = null!;
        return false;
    }

    private static string ToLocalPath(string value, Uri? uri)
    {
        if (uri?.IsFile == true)
            return uri.LocalPath;
        return value;
    }

    private static bool IsWeb(Uri uri) =>
        uri.Scheme is "http" or "https";

    private static bool LooksLikeWeb(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeFolder(string path) =>
        path.EndsWith('\\') || path.EndsWith('/') ||
        (Path.IsPathRooted(path) && string.IsNullOrEmpty(Path.GetExtension(path)));

    private static bool LooksLikeFile(string path) =>
        Path.IsPathRooted(path) && !string.IsNullOrEmpty(Path.GetExtension(path));

    private static void OpenFolder(string path)
    {
        var folder = path.TrimEnd('\\', '/');
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder.Replace("\"", "")}\"",
            UseShellExecute = true,
        });
    }

    private static void Start(string fileName) =>
        Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
}
