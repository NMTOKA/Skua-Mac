using System.IO;
using System;

namespace Skua.Manager.Avalonia.Services;

internal static class SkuaExecutableLocator
{
    public static string ExecutablePath { get; } = Resolve();

    public static string WorkingDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    private static string Resolve()
    {
        if (!OperatingSystem.IsWindows())
            return ResolveUnix();

        string vibePath = Path.Combine(AppContext.BaseDirectory, "VibeSkua.exe");
        if (File.Exists(vibePath))
            return vibePath;

        string deployedPath = Path.Combine(AppContext.BaseDirectory, "Skua.exe");
        if (File.Exists(deployedPath))
            return deployedPath;

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Build", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "win-x64", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0-windows", "win-x64", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "Skua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "win-x64", "VibeSkua.exe"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0-windows", "win-x64", "Skua.exe")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return File.Exists(vibePath) ? vibePath : deployedPath;
    }

    private static string ResolveUnix()
    {
        string vibePath = Path.Combine(AppContext.BaseDirectory, "VibeSkua");
        if (File.Exists(vibePath))
            return vibePath;

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (string config in new[] { "Release", "Debug" })
        {
            string candidate = Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", config, "net10.0", "VibeSkua");
            if (File.Exists(candidate))
                return candidate;
        }

        return vibePath;
    }
}
