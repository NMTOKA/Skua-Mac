using System.Diagnostics;

namespace Skua.Core.Utils;

public class Link
{
    public static void OpenBrowser(string link)
    {
        if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", link);
            return;
        }

        ProcessStartInfo ps = new("explorer", link)
        {
            UseShellExecute = true,
            Verb = "open"
        };
        Process.Start(ps);
    }

    /// <summary>Opens a folder in the platform file manager (Explorer, Finder, or the Linux default).</summary>
    public static void OpenFolder(string path)
    {
        string opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        ProcessStartInfo ps = new(opener) { UseShellExecute = false };
        ps.ArgumentList.Add(path);
        Process.Start(ps)?.Dispose();
    }
}