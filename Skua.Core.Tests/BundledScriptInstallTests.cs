using System.Diagnostics;
using Skua.Core.Services;
using Xunit;

namespace Skua.Core.Tests;

public sealed class BundledScriptInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "skua-bundle-tests-" + Guid.NewGuid().ToString("N"));

    public BundledScriptInstallTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingBundleDoesNothing()
    {
        string scripts = ScriptsDir();

        bool installed = ClientFilesService.TryInstallBundledDirectory(
            Path.Combine(_root, "missing"), scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.False(installed);
        Assert.False(Directory.Exists(Path.Combine(scripts, "UltrasLW")));
    }

    [Fact]
    public void BundleWithoutMarkerIsRejected()
    {
        string bundled = Bundle();
        File.WriteAllText(Path.Combine(bundled, "Notes.txt"), "not the pack");
        string scripts = ScriptsDir();

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.False(installed);
        Assert.False(Directory.Exists(Path.Combine(scripts, "UltrasLW")));
    }

    [Fact]
    public void FreshInstallCopiesNestedFilesAndLeavesNoStagingDirectory()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.True(installed);
        Assert.Equal("core", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "CoreLoneWolf.cs")));
        Assert.Equal("daily", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "Dailies", "UltraWarden_LW.cs")));
        Assert.Empty(Directory.GetDirectories(scripts, "UltrasLW.installing-*"));
        Assert.Equal(
            ClientFilesService.ContentSha256(Path.Combine(scripts, "UltrasLW")),
            File.ReadAllText(Path.Combine(scripts, "UltrasLW.pristine")).Trim());
    }

    [Fact]
    public void PristineCopyIsReplacedWhenTheBundleChanges()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        Assert.True(Install(bundled, scripts));

        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "newer");
        File.WriteAllText(Path.Combine(bundled, "Dailies", "UltraWarden_LW.cs"), "newer-daily");

        Assert.True(Install(bundled, scripts));
        Assert.Equal("newer", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "CoreLoneWolf.cs")));
        Assert.Equal("newer-daily", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "Dailies", "UltraWarden_LW.cs")));
        Assert.Equal(
            ClientFilesService.ContentSha256(bundled),
            File.ReadAllText(Path.Combine(scripts, "UltrasLW.pristine")).Trim());
    }

    [Fact]
    public void EditedCopyIsNotReplacedWhenTheBundleChanges()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        Assert.True(Install(bundled, scripts));
        string edited = Path.Combine(scripts, "UltrasLW", "CoreLoneWolf.cs");
        File.WriteAllText(edited, "edited");
        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "newer");

        Assert.False(Install(bundled, scripts));
        Assert.Equal("edited", File.ReadAllText(edited));
        Assert.Equal("daily", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "Dailies", "UltraWarden_LW.cs")));
    }

    [Fact]
    public void KnownPristineHashIsReplacedWithoutARecord()
    {
        string oldPack = Bundle();
        WritePack(oldPack);
        string scripts = ScriptsDir();
        string dest = Path.Combine(scripts, "UltrasLW");
        CopyTree(oldPack, dest);
        string oldHash = ClientFilesService.ContentSha256(dest);

        string bundled = Bundle();
        WritePack(bundled);
        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "newer");

        bool installed = ClientFilesService.TryInstallBundledDirectory(
            bundled, scripts, "UltrasLW", "CoreLoneWolf.cs", [oldHash]);

        Assert.True(installed);
        Assert.Equal("newer", File.ReadAllText(Path.Combine(dest, "CoreLoneWolf.cs")));
    }

    [Fact]
    public void FinderMetadataDoesNotCountAsAnEdit()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        Assert.True(Install(bundled, scripts));
        File.WriteAllText(Path.Combine(scripts, "UltrasLW", ".DS_Store"), "finder");
        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "newer");

        Assert.True(Install(bundled, scripts));
        Assert.Equal("newer", File.ReadAllText(Path.Combine(scripts, "UltrasLW", "CoreLoneWolf.cs")));
        Assert.False(File.Exists(Path.Combine(scripts, "UltrasLW", ".DS_Store")));
    }

    [Fact]
    public void CurrentCopyRecordsTheHashWithoutRewritingFiles()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        string dest = Path.Combine(scripts, "UltrasLW");
        CopyTree(bundled, dest);
        string file = Path.Combine(dest, "CoreLoneWolf.cs");
        DateTime stamp = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        Assert.False(Install(bundled, scripts));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        Assert.Equal("core", File.ReadAllText(file));
        Assert.Equal(
            ClientFilesService.ContentSha256(dest),
            File.ReadAllText(Path.Combine(scripts, "UltrasLW.pristine")).Trim());
    }

    [Fact]
    public void ContentHashMatchesOpenSslTreeHash()
    {
        string bare = Path.Combine(_root, "hash-bare");
        Directory.CreateDirectory(Path.Combine(bare, "Dailies"));
        File.WriteAllBytes(Path.Combine(bare, "CoreLoneWolf.cs"), [1, 2, 3, 255]);
        File.WriteAllText(Path.Combine(bare, "Dailies", "UltraWarden_LW.cs"), "daily");

        string? shell = OpenSslTreeHash(bare);
        if (shell is null)
            return;

        string withFinder = Path.Combine(_root, "hash-finder");
        CopyTree(bare, withFinder);
        File.WriteAllText(Path.Combine(withFinder, ".DS_Store"), "finder");

        Assert.Equal(shell, ClientFilesService.ContentSha256(bare));
        Assert.Equal(shell, ClientFilesService.ContentSha256(withFinder));
        Assert.NotEqual(shell, OpenSslTreeHash(withFinder));
    }

    [Fact]
    public void Shipped243HashMatchesThePinnedSnapshotWhenPresent()
    {
        string fixture = "/tmp/ultraslw-hash/UltrasLW-2b60a6b8fef31ba3888dc3da1c54b35ead437df2";
        if (!Directory.Exists(fixture))
            return;

        Assert.Equal(ClientFilesService.UltrasLW243TreeSha256, ClientFilesService.ContentSha256(fixture));
    }

    [Fact]
    public void BuildScriptPinsThe243SnapshotHash()
    {
        string? scriptPath = FindUp("build-mac.sh");
        Assert.NotNull(scriptPath);
        string script = File.ReadAllText(scriptPath);
        Assert.Contains($"ULTRASLW_TREE_SHA256=\"{ClientFilesService.UltrasLW243TreeSha256}\"", script);
    }

    [Fact]
    public void ExistingFilesAreLeftAlone()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        string dest = Path.Combine(scripts, "UltrasLW");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "MyEdit.cs"), "mine");

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.False(installed);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(dest, "MyEdit.cs")));
        Assert.False(File.Exists(Path.Combine(dest, "CoreLoneWolf.cs")));
    }

    [Fact]
    public void EmptyDestinationIsReplaced()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        string dest = Path.Combine(scripts, "UltrasLW");
        Directory.CreateDirectory(Path.Combine(dest, "EmptyChild"));

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.True(installed);
        Assert.Equal("core", File.ReadAllText(Path.Combine(dest, "CoreLoneWolf.cs")));
        Assert.False(Directory.Exists(Path.Combine(dest, "EmptyChild")));
    }

    [Fact]
    public void StaleStagingDirectoryIsRemoved()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        string stale = Path.Combine(scripts, "UltrasLW.installing-old");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "partial.cs"), "partial");

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.True(installed);
        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void FileBlockingTheFolderNameIsNotDeleted()
    {
        string bundled = Bundle();
        WritePack(bundled);
        string scripts = ScriptsDir();
        string blocker = Path.Combine(scripts, "UltrasLW");
        File.WriteAllText(blocker, "not a folder");

        bool installed = ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

        Assert.False(installed);
        Assert.Equal("not a folder", File.ReadAllText(blocker));
    }

    private string Bundle()
    {
        string path = Path.Combine(_root, "bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private string ScriptsDir()
    {
        string path = Path.Combine(_root, "scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool Install(string bundled, string scripts)
        => ClientFilesService.TryInstallBundledDirectory(bundled, scripts, "UltrasLW", "CoreLoneWolf.cs");

    private static void WritePack(string bundled)
    {
        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "core");
        Directory.CreateDirectory(Path.Combine(bundled, "Dailies"));
        File.WriteAllText(Path.Combine(bundled, "Dailies", "UltraWarden_LW.cs"), "daily");
    }

    private static void CopyTree(string source, string dest)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string? FindUp(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? OpenSslTreeHash(string directory)
    {
        if (!File.Exists("/bin/bash"))
            return null;

        string script = """
            cd "$1" || exit 1
            find . -type f -print | LC_ALL=C sort | while IFS= read -r file; do
              hash=$(openssl dgst -sha256 "$file" | awk '{print $NF}')
              printf '%s  %s\n' "$hash" "${file#./}"
            done | openssl dgst -sha256 | awk '{print $NF}'
            """;

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("bash");
        process.StartInfo.ArgumentList.Add(directory);
        process.Start();
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Length == 64 ? output : null;
    }
}
