#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Skua.App.Avalonia;

/// <summary>
/// Remembers where the Skua Mac bar and each tool window were, and puts them back there next time, like other Mac
/// apps. The bar of the first running window, the second, ... each have their own spot, so several bots reopen
/// side by side. Saved to ~/Library/Application Support/VibeSkua/window-places.json.
/// </summary>
internal static class MacWindowPlaces
{
    internal sealed record Place(int X, int Y, double Width, double Height);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "window-places.json");

    private static readonly object FileLock = new();

    /// <summary>The spot for this copy's bar: "main-1" for the first running Skua Mac, "main-2" for the next, ...</summary>
    public static string MainKey { get; } = $"main-{RunningCopiesBeforeThis() + 1}";

    public static Place? Get(string key)
    {
        lock (FileLock)
            return Load().TryGetValue(key, out Place? place) ? place : null;
    }

    public static void Save(string key, Window window)
    {
        if (window.WindowState != WindowState.Normal || window.Bounds.Width <= 0)
            return;

        Place place = new(window.Position.X, window.Position.Y, window.Bounds.Width, window.Bounds.Height);
        try
        {
            lock (FileLock)
            {
                // Other copies write their own keys; reread so their spots are kept.
                Dictionary<string, Place> places = Load();
                places[key] = place;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(places));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the window position: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves <paramref name="window"/> to its saved spot if that is still on a screen (a display may have been
    /// unplugged). Restores the width too when <paramref name="restoreWidth"/>, and the height when
    /// <paramref name="restoreHeight"/>. Returns false when there was nothing usable to restore.
    /// </summary>
    public static bool Restore(string key, Window window, bool restoreWidth, bool restoreHeight)
    {
        if (Get(key) is not Place place || !IsOnScreen(window, place))
            return false;

        if (restoreWidth && place.Width >= window.MinWidth)
            window.Width = place.Width;
        if (restoreHeight && place.Height >= window.MinHeight)
            window.Height = place.Height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(place.X, place.Y);
        return true;
    }

    // Needs the title bar (its top 40 points) on a screen, so the window can always be dragged back.
    private static bool IsOnScreen(Window window, Place place)
    {
        IReadOnlyList<Screen>? screens = window.Screens?.All;
        if (screens is null || screens.Count == 0)
            return false;

        double scaling = screens[0].Scaling > 0 ? screens[0].Scaling : 1;
        PixelRect titleBar = new(place.X, place.Y, (int)(Math.Min(place.Width, 200) * scaling), (int)(40 * scaling));
        return screens.Any(s => s.WorkingArea.Contains(titleBar.TopLeft) && s.WorkingArea.Contains(titleBar.BottomRight));
    }

    private static Dictionary<string, Place> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, Place>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* a damaged file just means default positions */ }
        return new();
    }

    private static int RunningCopiesBeforeThis()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            return Process.GetProcessesByName(self.ProcessName).Count(p => p.Id != self.Id && p.StartTime < self.StartTime);
        }
        catch
        {
            return 0;
        }
    }
}
#endif
