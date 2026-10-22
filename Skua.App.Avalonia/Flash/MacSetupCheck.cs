#if !IS_WINDOWS
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Flash;

/// <summary>
/// First-run checks for what the macOS game window needs, so a missing piece shows a plain explanation
/// (and a fix) instead of a game window that silently never appears.
/// </summary>
internal static class MacSetupCheck
{
    public const string DefaultPepperFlashPath =
        "/Applications/Artix Game Launcher.app/Contents/Resources/plugins/PepperFlashPlayer.plugin";

    private const string ArtixLauncherDownloadUrl = "https://www.artix.com/downloads/artixlauncher/";
    private const string RosettaRuntime = "/Library/Apple/usr/libexec/oah/libRosettaRuntime";

    /// <summary>Returns true when the game window can be started.</summary>
    public static bool EnsureReady(string hostDir)
    {
        if (!OperatingSystem.IsMacOS())
            return true;

        return EnsureRosetta() && EnsurePepperFlash() && ClearQuarantine(hostDir);
    }

    private static IDialogService? Dialogs => Ioc.Default.GetService<IDialogService>();

    private static bool EnsureRosetta()
    {
        if (File.Exists(RosettaRuntime))
            return true;

        bool? install = Dialogs?.ShowMessageBox(
            "Skua Mac's game window uses Flash, which needs Apple's Rosetta to run on this Mac.\r\n\r\n" +
            "Install Rosetta now? It takes about a minute and only has to be done once.",
            "Rosetta needed", true);
        if (install != true)
            return false;

        int exitCode = RunAndPump("/usr/sbin/softwareupdate", "--install-rosetta", "--agree-to-license");
        if (exitCode == 0 && File.Exists(RosettaRuntime))
            return true;

        Dialogs?.ShowMessageBox(
            "Rosetta couldn't be installed automatically. Open the Terminal app, paste this line, press Return, " +
            "then reopen Skua Mac:\r\n\r\nsoftwareupdate --install-rosetta --agree-to-license",
            "Rosetta needed");
        return false;
    }

    private static bool EnsurePepperFlash()
    {
        string path = Environment.GetEnvironmentVariable("SKUA_PPAPI") ?? DefaultPepperFlashPath;
        if (Directory.Exists(path))
            return true;

        bool? open = Dialogs?.ShowMessageBox(
            "Skua Mac uses the Flash player that comes with the free Artix Game Launcher, but it isn't installed.\r\n\r\n" +
            "Install the Artix Game Launcher into your Applications folder, then reopen Skua Mac.\r\n\r\n" +
            "Open the download page now?",
            "Artix Game Launcher needed", true);
        if (open == true)
            Ioc.Default.GetService<IProcessService>()?.OpenLink(ArtixLauncherDownloadUrl);
        return false;
    }

    /// <summary>
    /// A downloaded app is quarantined. Once the user has opened Skua Mac, clear the flag from the bundled game
    /// window too, or macOS blocks it separately when it starts.
    /// </summary>
    private static bool ClearQuarantine(string hostDir)
    {
        try
        {
            if (Directory.Exists(hostDir))
                RunAndPump("/usr/bin/xattr", "-dr", "com.apple.quarantine", hostDir);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not clear quarantine on {hostDir}: {ex.Message}");
        }
        return true;
    }

    /// <summary>Runs a command to completion while keeping the UI responsive.</summary>
    private static int RunAndPump(string fileName, params string[] args)
    {
        ProcessStartInfo psi = new(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        Task<int> run = Task.Run(() =>
        {
            using Process p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        });

        if (Dispatcher.UIThread.CheckAccess() && !run.IsCompleted)
        {
            DispatcherFrame frame = new();
            run.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }

        try
        {
            return run.GetAwaiter().GetResult();
        }
        catch
        {
            return -1;
        }
    }
}
#endif
