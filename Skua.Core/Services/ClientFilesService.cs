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
                File.Create(ClientFileSources.SkuaAdvancedSkillsFile);
        }

        if (!File.Exists(ClientFileSources.SkuaQuestsFile))
        {
            string rootQuestsFile = Path.Combine(AppContext.BaseDirectory, "QuestData.json");
            if (File.Exists(rootQuestsFile))
                File.Copy(rootQuestsFile, ClientFileSources.SkuaQuestsFile);
            else
                File.Create(ClientFileSources.SkuaQuestsFile);
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
    /// Skips the copy when that folder is already there, so an update does not replace local edits.
    /// The macOS build puts the pack in BundledScripts; other builds have nothing to copy.
    /// </summary>
    public static void InstallBundledUltrasLW()
    {
        try
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "BundledScripts", "UltrasLW");
            if (!Directory.Exists(bundled))
                return;

            Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);
            string dest = Path.Combine(ClientFileSources.SkuaScriptsDIR, "UltrasLW");
            if (Directory.Exists(dest))
                return;

            CopyDirectory(bundled, dest);
        }
        catch
        {
            // A missing scripts folder should not stop the client from opening.
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);

        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(dest, Path.GetFileName(directory)));
    }
}
