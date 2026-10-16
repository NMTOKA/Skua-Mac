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
}