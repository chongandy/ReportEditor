using System.Diagnostics;
using System.IO;
using System.Text;

namespace ReportEditor.Services;

public static class ProtectedPdf
{
    public static bool LooksMicrosoftProtected(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
            return true;

        var length = Math.Min(bytes.Length, 2_000_000);
        var text = Encoding.ASCII.GetString(bytes, 0, length);
        return Contains(text, "MicrosoftIRMServices")
            || Contains(text, "MicrosoftIRM")
            || Contains(text, "/IRMEnabled")
            || Contains(text, "Microsoft.RightsManagement")
            || Contains(text, "MicrosoftRightsManagement")
            || Contains(text, "/Filter/Microsoft")
            || Contains(text, "application/vnd.ms-package")
            || Contains(text, "Purview")
            || Contains(text, "RMS_ENCRYPTED");
    }

    public static bool LooksEncrypted(byte[] bytes)
    {
        if (LooksMicrosoftProtected(bytes)) return true;
        var length = Math.Min(bytes.Length, 512_000);
        var text = Encoding.ASCII.GetString(bytes, 0, length);
        return Contains(text, "/Encrypt") || Contains(text, "/EncryptMetadata");
    }

    public static void OpenInMicrosoftEdge(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("PDF was not found.", full);

        var fileUri = new Uri(full).AbsoluteUri;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msedge",
                Arguments = $"\"{full}\"",
                UseShellExecute = true,
            });
            return;
        }
        catch
        {
            // Fall through to protocol / default handler.
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "microsoft-edge:" + fileUri,
                UseShellExecute = true,
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                UseShellExecute = true,
            });
        }
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
