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

    private static void WritePack(string bundled)
    {
        File.WriteAllText(Path.Combine(bundled, "CoreLoneWolf.cs"), "core");
        Directory.CreateDirectory(Path.Combine(bundled, "Dailies"));
        File.WriteAllText(Path.Combine(bundled, "Dailies", "UltraWarden_LW.cs"), "daily");
    }
}
