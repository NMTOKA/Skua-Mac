using System.Text.RegularExpressions;

namespace Skua.Core.Utils;

/// <summary>
/// Source adjustments applied to scripts before compiling on non-Windows platforms, for community scripts
/// that assume Windows. Has no effect on Windows.
/// </summary>
public static partial class ScriptPlatformCompat
{
    /// <summary>
    /// Stand-in for <see cref="OperatingSystem.IsWindowsVersionAtLeast"/>: a minimum Windows version is only a
    /// requirement on Windows, so it counts as met on any other OS.
    /// </summary>
    public static bool WindowsVersionRequirementMet(int major, int minor = 0, int build = 0, int revision = 0)
    {
        return !OperatingSystem.IsWindows() || OperatingSystem.IsWindowsVersionAtLeast(major, minor, build, revision);
    }

    /// <summary>
    /// Rewrites Windows-only checks in script source. CoreBots.cs stops every script unless
    /// OperatingSystem.IsWindowsVersionAtLeast(10) is true, which is never the case on macOS.
    /// </summary>
    public static string Apply(string source)
    {
        if (OperatingSystem.IsWindows() || !source.Contains("IsWindowsVersionAtLeast", StringComparison.Ordinal))
            return source;

        return WindowsVersionCheck().Replace(source, "global::Skua.Core.Utils.ScriptPlatformCompat.WindowsVersionRequirementMet(");
    }

    [GeneratedRegex(@"(?:global::)?(?:System\.)?OperatingSystem\.IsWindowsVersionAtLeast\(")]
    private static partial Regex WindowsVersionCheck();
}
