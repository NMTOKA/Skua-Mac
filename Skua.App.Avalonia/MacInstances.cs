#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
        menu.Items.Add(CreateEngineMenu());
        NativeMenu.SetMenu(app, menu);
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int SendInt(IntPtr receiver, IntPtr selector);

    /// <summary>Process id of the app the user is using (macOS's frontmost app), or -1. Call on the UI thread.</summary>
    public static int FrontmostProcessId()
    {
        try
        {
            IntPtr workspace = SendPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            IntPtr app = SendPtr(workspace, sel_registerName("frontmostApplication"));
            return app == IntPtr.Zero ? -1 : SendInt(app, sel_registerName("processIdentifier"));
        }
        catch
        {
            return -1;
        }
    }

    private static readonly string EngineFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "game-engine");

    /// <summary>Game Engine > Flash / Ruffle: writes the choice FlashUtil.Ruffle.cs reads at startup.</summary>
    private static NativeMenuItem CreateEngineMenu()
    {
        bool ruffleNow = Flash.FlashUtil.UseRuffle;
        NativeMenuItem flash = new("Flash (needs Rosetta and the Artix Game Launcher)") { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = !ruffleNow };
        NativeMenuItem ruffle = new("Ruffle") { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = ruffleNow };
        flash.Click += (_, _) => ChooseEngine(useRuffle: false, flash, ruffle);
        ruffle.Click += (_, _) => ChooseEngine(useRuffle: true, flash, ruffle);

        NativeMenu engines = new();
        engines.Items.Add(flash);
        engines.Items.Add(ruffle);
        return new NativeMenuItem("Game Engine") { Menu = engines };
    }

    private static void ChooseEngine(bool useRuffle, NativeMenuItem flash, NativeMenuItem ruffle)
    {
        flash.IsChecked = !useRuffle;
        ruffle.IsChecked = useRuffle;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EngineFile)!);
            File.WriteAllText(EngineFile, useRuffle ? "ruffle\n" : "flash\n");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the game engine choice: {ex.Message}");
            return;
        }

        if (useRuffle == Flash.FlashUtil.UseRuffle)
            return;

        string name = useRuffle ? "Ruffle" : "Flash";
        string others = Skua.Core.AppStartup.ArmyChannel.Instances().Count > 1
            ? "\n\nOther Skua Mac windows switch when they are reopened."
            : string.Empty;
        bool? restart = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Skua.Core.Interfaces.IDialogService>()?.ShowMessageBox(
            $"Skua Mac will use {name} the next time it opens. Restart this window now? A running script will stop.{others}",
            "Game Engine", true);
        if (restart == true)
        {
            LaunchNewInstance();
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
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
