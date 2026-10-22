#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Diagnostics;
using System.IO;

namespace Skua.App.Avalonia;

/// <summary>
/// macOS has no tab host: each bot is its own copy of Skua Mac. This adds "New Skua Window" (⌘N) to the app
/// menu and staggers new copies so they don't open exactly on top of each other.
/// </summary>
internal static class MacInstances
{
    private const int CascadeStep = 40;

    /// <summary>Must run from <see cref="App.Initialize"/>: macOS builds the app menu before the main window opens.</summary>
    public static void AddAppMenu(Application app)
    {
        NativeMenuItem about = new("About Skua Mac");
        about.Click += (_, _) => ShowAbout();

        NativeMenuItem newWindow = new("New Skua Window")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta),
        };
        newWindow.Click += (_, _) => LaunchNewInstance();

        NativeMenuItem updates = new("Check for Updates...");
        updates.Click += (_, _) => MacUpdates.CheckNow();

        NativeMenu menu = new();
        menu.Items.Add(about);
        menu.Items.Add(updates);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(newWindow);
        NativeMenu.SetMenu(app, menu);
    }

    private static void ShowAbout()
    {
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Skua.Core.Interfaces.IDialogService>()?.ShowMessageBox(
            $"Skua Mac {MacUpdates.CurrentVersion.ToString(3)}\n\n"
            + "A macOS port of VibeSkua by NinjaXz, built on Skua (auqw/skua).\n"
            + "github.com/NMTOKA/Skua-Mac",
            "About Skua Mac");
    }

    /// <summary>Starts another copy of the app (Finder would only switch to the running one).</summary>
    public static void LaunchNewInstance()
    {
        try
        {
            string? bundle = FindAppBundle();
            ProcessStartInfo psi = bundle is not null
                ? new ProcessStartInfo("open") { ArgumentList = { "-n", bundle } }
                : new ProcessStartInfo(Environment.ProcessPath!);
            psi.UseShellExecute = false;
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not start another Skua Mac: {ex.Message}");
        }
    }

    /// <summary>Moves this window down-right by its position among the running copies.</summary>
    public static void Cascade(Window window)
    {
        int others;
        try
        {
            using Process self = Process.GetCurrentProcess();
            others = Process.GetProcessesByName(self.ProcessName).Length - 1;
        }
        catch
        {
            return;
        }

        if (others <= 0)
            return;

        int step = (others % 8) * CascadeStep;
        window.Position = new PixelPoint(window.Position.X + step, window.Position.Y + step);
    }

    private static string? FindAppBundle()
    {
        // .../Skua Mac.app/Contents/MacOS/
        DirectoryInfo? dir = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string? bundle = dir.Parent?.Parent?.FullName;
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }
}
#endif
