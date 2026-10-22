#if !IS_WINDOWS
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Skua.App.Avalonia;

/// <summary>
/// Skua Mac has no auto-updater, so it checks GitHub Releases for a newer version (at most once a day, from one
/// window) and offers to open the download page. "Skip this version" stops asking until the next release.
/// </summary>
internal static class MacUpdates
{
    private const string LatestReleaseApi = "https://api.github.com/repos/NMTOKA/Skua-Mac/releases/latest";
    private const string ReleasesPage = "https://github.com/NMTOKA/Skua-Mac/releases/latest";

    private static readonly string StateFile = Path.Combine(ClientFileSources.SkuaDIR, "update-check.txt");

    public static Version CurrentVersion => typeof(MacUpdates).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Startup check: quiet unless a newer version is out that the user hasn't skipped.</summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));

            // Only the first window checks, so several bots don't all ask at once.
            if (ArmyChannel.Instances().Count > 1)
                return;

            (DateTime lastCheck, string skipped) = ReadState();
            if (DateTime.UtcNow - lastCheck < TimeSpan.FromDays(1))
                return;

            (Version? latest, string? page) = await GetLatestAsync();
            WriteState(DateTime.UtcNow, skipped);
            if (latest is null || latest <= CurrentVersion || latest.ToString() == skipped)
                return;

            Dispatcher.UIThread.Post(() => Offer(latest, page ?? ReleasesPage, allowSkip: true));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    /// <summary>Skua Mac menu > Check for Updates: always answers.</summary>
    public static async void CheckNow()
    {
        IDialogService? dialogs = Ioc.Default.GetService<IDialogService>();
        try
        {
            (Version? latest, string? page) = await GetLatestAsync();
            if (latest is null)
                throw new InvalidOperationException("no release found");

            if (latest > CurrentVersion)
                Offer(latest, page ?? ReleasesPage, allowSkip: false);
            else
                dialogs?.ShowMessageBox($"You have the latest version of Skua Mac ({Format(CurrentVersion)}).", "Skua Mac is up to date");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update check failed: {ex.Message}");
            dialogs?.ShowMessageBox("Couldn't check for updates. Check your internet connection and try again.", "Check for Updates");
        }
    }

    private static void Offer(Version latest, string page, bool allowSkip)
    {
        IDialogService? dialogs = Ioc.Default.GetService<IDialogService>();
        if (dialogs is null)
            return;

        string message = $"Skua Mac {Format(latest)} is available. You have {Format(CurrentVersion)}.\n\n"
            + "Download the new .dmg, then drag Skua Mac into Applications to replace this one. Your settings and scripts are kept.";
        DialogResult result = allowSkip
            ? dialogs.ShowMessageBox(message, "Update available", "Download", "Later", "Skip this version")
            : dialogs.ShowMessageBox(message, "Update available", "Download", "Later");

        if (result.Text == "Download")
            OpenUrl(page);
        else if (result.Text == "Skip this version")
            WriteState(DateTime.UtcNow, latest.ToString());
    }

    private static async Task<(Version?, string?)> GetLatestAsync()
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Skua-Mac");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using JsonDocument doc = JsonDocument.Parse(await client.GetStringAsync(LatestReleaseApi));

        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string? page = doc.RootElement.TryGetProperty("html_url", out JsonElement url) ? url.GetString() : null;
        // Release tags look like "mac-v2.1.0".
        string number = tag.StartsWith("mac-v", StringComparison.OrdinalIgnoreCase) ? tag[5..] : tag.TrimStart('v', 'V');
        return (Version.TryParse(number, out Version? version) ? Normalize(version) : null, page);
    }

    // 2.1.0 and 2.1.0.0 compare as equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static string Format(Version v) => v.ToString(3);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { url }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not open {url}: {ex.Message}");
        }
    }

    private static (DateTime, string) ReadState()
    {
        try
        {
            string[] lines = File.ReadAllLines(StateFile);
            DateTime last = lines.Length > 0 && DateTime.TryParse(lines[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d : DateTime.MinValue;
            return (last, lines.Length > 1 ? lines[1] : "");
        }
        catch
        {
            return (DateTime.MinValue, "");
        }
    }

    private static void WriteState(DateTime lastCheck, string skipped)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllLines(StateFile, new[] { lastCheck.ToString("o"), skipped });
        }
        catch { }
    }
}
#endif
