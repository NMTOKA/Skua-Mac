using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    /// Tree hash of the UltrasLW snapshot shipped in Skua Mac 2.4.3 (commit 2b60a6b8),
    /// which was copied before a <c>.pristine</c> record existed. Keep this when the pin moves.
    /// </summary>
    internal const string UltrasLW243TreeSha256 = "92a16bba3bd05f54d709a6d11b918dec9af7869c90eda3ee114b86f706d47243";

    /// <summary>
    /// Copies the UltrasLW pack shipped beside the app into the user's Scripts folder.
    /// A folder that still matches the copy we installed is replaced when this pack is different.
    /// A folder the user has edited is left alone. An empty folder left by a failed copy is replaced.
    /// The macOS build puts the pack in BundledScripts; other builds have nothing to copy.
    /// </summary>
    public static void InstallBundledUltrasLW()
    {
        try
        {
            TryInstallBundledDirectory(
                Path.Combine(AppContext.BaseDirectory, "BundledScripts", "UltrasLW"),
                ClientFileSources.SkuaScriptsDIR,
                "UltrasLW",
                "CoreLoneWolf.cs",
                [UltrasLW243TreeSha256]);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"UltrasLW install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Publishes <paramref name="bundledDir"/> as <paramref name="folderName"/> under <paramref name="scriptsDir"/>.
    /// Copies into a temporary folder first and renames it into place, so a failed copy does not look like a finished install.
    /// Returns true when a copy was published. A folder that still matches the recorded pristine hash, or one of
    /// <paramref name="knownPristineHashes"/>, is replaced. A folder whose contents differ from those is left alone.
    /// </summary>
    internal static bool TryInstallBundledDirectory(
        string bundledDir,
        string scriptsDir,
        string folderName,
        string markerFileName,
        IReadOnlyCollection<string>? knownPristineHashes = null)
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

        string bundledHash = ContentSha256(bundledDir);
        if (!Directory.Exists(dest) || !HasContentFiles(dest))
        {
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);

            return Publish(bundledDir, scriptsDir, folderName, markerFileName, dest, bundledHash);
        }

        string destHash = ContentSha256(dest);
        if (string.Equals(destHash, bundledHash, StringComparison.OrdinalIgnoreCase))
        {
            WritePristineRecord(scriptsDir, folderName, bundledHash);
            return false;
        }

        string? recorded = ReadPristineRecord(scriptsDir, folderName);
        bool pristine = string.Equals(destHash, recorded, StringComparison.OrdinalIgnoreCase)
            || ContainsHash(knownPristineHashes, destHash);
        if (!pristine)
            return false;

        return PublishReplacing(bundledDir, scriptsDir, folderName, markerFileName, dest, bundledHash);
    }

    /// <summary>
    /// Sha256 of the file contents, matching the macOS build's tree hash.
    /// Finder metadata (<c>.DS_Store</c>, <c>._*</c>) is ignored so opening the folder is not an edit.
    /// </summary>
    internal static string ContentSha256(string directory)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        IEnumerable<string> relativePaths = Directory.EnumerateFiles(directory, "*", options)
            .Select(path => ToManifestPath(directory, path))
            .Where(path => !IsFinderMetadata(path))
            .OrderBy(path => path, StringComparer.Ordinal);

        StringBuilder manifest = new();
        foreach (string relative in relativePaths)
        {
            string full = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            manifest.Append(Sha256Hex(File.ReadAllBytes(full)));
            manifest.Append("  ");
            manifest.Append(relative);
            manifest.Append('\n');
        }

        return Sha256Hex(Encoding.UTF8.GetBytes(manifest.ToString()));
    }

    private static bool Publish(
        string bundledDir,
        string scriptsDir,
        string folderName,
        string markerFileName,
        string dest,
        string bundledHash)
    {
        string staging = StagingPath(scriptsDir, folderName);
        try
        {
            CopyDirectory(bundledDir, staging);
            if (!File.Exists(Path.Combine(staging, markerFileName)))
                return false;

            Directory.Move(staging, dest);
            WritePristineRecord(scriptsDir, folderName, bundledHash);
            return Directory.Exists(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Bundled script install failed for {folderName}: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteQuiet(staging);
        }
    }

    private static bool PublishReplacing(
        string bundledDir,
        string scriptsDir,
        string folderName,
        string markerFileName,
        string dest,
        string bundledHash)
    {
        string staging = StagingPath(scriptsDir, folderName);
        string backup = Path.Combine(scriptsDir, folderName + ".replacing-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(bundledDir, staging);
            if (!File.Exists(Path.Combine(staging, markerFileName)))
                return false;

            Directory.Move(dest, backup);
            try
            {
                Directory.Move(staging, dest);
            }
            catch
            {
                if (!Directory.Exists(dest) && Directory.Exists(backup))
                    Directory.Move(backup, dest);
                throw;
            }

            DeleteQuiet(backup);
            WritePristineRecord(scriptsDir, folderName, bundledHash);
            return Directory.Exists(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Bundled script install failed for {folderName}: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteQuiet(staging);
        }
    }

    private static void DeleteStaleStaging(string scriptsDir, string folderName)
    {
        string installing = folderName + ".installing-";
        string replacing = folderName + ".replacing-";
        foreach (string directory in Directory.EnumerateDirectories(scriptsDir))
        {
            string name = Path.GetFileName(directory);
            if (!name.StartsWith(installing, StringComparison.Ordinal)
                && !name.StartsWith(replacing, StringComparison.Ordinal))
                continue;

            DeleteQuiet(directory);
        }
    }

    private static bool HasContentFiles(string directory)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(directory, "*", options)
            .Select(path => ToManifestPath(directory, path))
            .Any(path => !IsFinderMetadata(path));
    }

    private static string StagingPath(string scriptsDir, string folderName)
        => Path.Combine(scriptsDir, folderName + ".installing-" + Guid.NewGuid().ToString("N"));

    private static string PristineRecordPath(string scriptsDir, string folderName)
        => Path.Combine(scriptsDir, folderName + ".pristine");

    private static void WritePristineRecord(string scriptsDir, string folderName, string hash)
        => File.WriteAllText(PristineRecordPath(scriptsDir, folderName), hash + "\n");

    private static string? ReadPristineRecord(string scriptsDir, string folderName)
    {
        string path = PristineRecordPath(scriptsDir, folderName);
        if (!File.Exists(path))
            return null;

        string text = File.ReadAllText(path).Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool ContainsHash(IReadOnlyCollection<string>? hashes, string hash)
        => hashes?.Any(candidate => string.Equals(candidate, hash, StringComparison.OrdinalIgnoreCase)) == true;

    private static string ToManifestPath(string root, string fullPath)
        => Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static bool IsFinderMetadata(string relativePath)
    {
        int slash = relativePath.LastIndexOf('/');
        string name = slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
        return name == ".DS_Store" || name.StartsWith("._", StringComparison.Ordinal);
    }

    private static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static void DeleteQuiet(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
