using System.Diagnostics;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Services;

public class ClientFilesService : IClientFilesService
{
    public void CreateDirectories()
    {
        ClientFileSources.EnsureInitialized();

        if (!Directory.Exists(ClientFileSources.SkuaDIR))
        {
            Directory.CreateDirectory(ClientFileSources.SkuaDIR);

            if (!Directory.Exists(ClientFileSources.SkuaOptionsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaOptionsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaPluginsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaPluginsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaThemesDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaThemesDIR);
        }
        else
        {
            if (!Directory.Exists(ClientFileSources.SkuaOptionsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaOptionsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaPluginsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaPluginsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaThemesDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaThemesDIR);
        }
    }

    public void CreateFiles()
    {
        if (!File.Exists(ClientFileSources.SkuaAdvancedSkillsFile))
        {
            string rootAdvancedSkillsFile = Path.Combine(AppContext.BaseDirectory, "AdvancedSkills.json");
            if (File.Exists(rootAdvancedSkillsFile))
                File.Copy(rootAdvancedSkillsFile, ClientFileSources.SkuaAdvancedSkillsFile);
            else
                File.WriteAllText(ClientFileSources.SkuaAdvancedSkillsFile, string.Empty);
        }

        if (!File.Exists(ClientFileSources.SkuaQuestsFile))
        {
            string rootQuestsFile = Path.Combine(AppContext.BaseDirectory, "QuestData.json");
            if (File.Exists(rootQuestsFile))
                File.Copy(rootQuestsFile, ClientFileSources.SkuaQuestsFile);
            else
                File.WriteAllText(ClientFileSources.SkuaQuestsFile, string.Empty);
        }

        if (!File.Exists(ClientFileSources.SkuaJunkItemsFile))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClientFileSources.SkuaJunkItemsFile)!);
                File.WriteAllText(ClientFileSources.SkuaJunkItemsFile, "[]");
            }
            catch
            {
                // ignored
            }
        }

        InstallBundledUltrasLW();
    }

    /// <summary>
    /// Copies the UltrasLW pack shipped beside the app into the user's Scripts folder.
    /// A folder that already contains files is left alone, so an update does not replace local edits.
    /// An empty folder left by a failed copy is replaced. The macOS build puts the pack in
    /// BundledScripts; other builds have nothing to copy.
    /// </summary>
    public static void InstallBundledUltrasLW()
    {
        try
        {
            TryInstallBundledDirectory(
                Path.Combine(AppContext.BaseDirectory, "BundledScripts", "UltrasLW"),
                ClientFileSources.SkuaScriptsDIR,
                "UltrasLW",
                "CoreLoneWolf.cs");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"UltrasLW install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Publishes <paramref name="bundledDir"/> as <paramref name="folderName"/> under <paramref name="scriptsDir"/>.
    /// Copies into a temporary folder first and renames it into place, so a failed copy does not look like a finished install.
    /// </summary>
    internal static bool TryInstallBundledDirectory(string bundledDir, string scriptsDir, string folderName, string markerFileName)
    {
        if (string.IsNullOrWhiteSpace(bundledDir) || string.IsNullOrWhiteSpace(scriptsDir)
            || string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(markerFileName))
            return false;

        if (!Directory.Exists(bundledDir) || !File.Exists(Path.Combine(bundledDir, markerFileName)))
            return false;

        Directory.CreateDirectory(scriptsDir);
        DeleteStaleStaging(scriptsDir, folderName);

        string dest = Path.Combine(scriptsDir, folderName);
        if (File.Exists(dest))
            return false;

        if (Directory.Exists(dest))
        {
            if (DirectoryHasAnyFile(dest))
                return false;

            Directory.Delete(dest, recursive: true);
        }

        string staging = Path.Combine(scriptsDir, folderName + ".installing-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(bundledDir, staging);
            if (!File.Exists(Path.Combine(staging, markerFileName)))
                return false;

            Directory.Move(staging, dest);
            return Directory.Exists(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Bundled script install failed for {folderName}: {ex.Message}");
            return false;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static void DeleteStaleStaging(string scriptsDir, string folderName)
    {
        string prefix = folderName + ".installing-";
        foreach (string directory in Directory.EnumerateDirectories(scriptsDir))
        {
            if (!Path.GetFileName(directory).StartsWith(prefix, StringComparison.Ordinal))
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool DirectoryHasAnyFile(string directory)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(directory, "*", options).Any();
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if (IsReparsePoint(file))
                continue;

            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            if (IsReparsePoint(directory))
                continue;

            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
