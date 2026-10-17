using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Diagnostics;

namespace Skua.Core.Services;

public class ProcessStartService : IProcessService
{
    public ProcessStartService(ISettingsService settingsService, IDialogService dialogService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
    }

    private readonly string _vscPath = Path.Combine(AppContext.BaseDirectory, "VSCode", "code");
    private readonly string _scriptsPath = ClientFileSources.SkuaScriptsDIR;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    public void OpenLink(string link)
    {
        ProcessStartInfo ps = new(link)
        {
            UseShellExecute = true,
            Verb = "open"
        };
        Process.Start(ps);
    }

    public void OpenVSC()
    {
        if (OperatingSystem.IsMacOS())
        {
            OpenInEditorMac(null);
            return;
        }

        if (_settingsService.Get("UseLocalVSC", false))
        {
            Process.Start(_vscPath, _scriptsPath);
            return;
        }
        try
        {
            VSCode(string.Empty);
        }
        catch
        {
            _dialogService.ShowMessageBox("VSCode was not found. If you have VSCode, reinstall it and be sure to check it to install in you PATH folder.", "VSCode not found");
        }
    }

    public void OpenVSC(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            OpenInEditorMac(path);
            return;
        }

        if (_settingsService.Get("UseLocalVSC", false))
        {
            Process.Start(_vscPath, new[] { _scriptsPath, path, "--reuse-window" });
            return;
        }
        try
        {
            VSCode(path);
        }
        catch
        {
            Process.Start("notepad", path);
        }
    }

    /// <summary>
    /// macOS: opens the scripts folder (and <paramref name="file"/>, if given) in Visual Studio Code when it is
    /// installed; otherwise the folder opens in Finder and a file opens in the default text editor.
    /// The bundled Windows VS Code ("UseLocalVSC") does not exist on macOS.
    /// </summary>
    private void OpenInEditorMac(string? file)
    {
        string? vscode = new[]
        {
            "/Applications/Visual Studio Code.app",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Visual Studio Code.app"),
        }.FirstOrDefault(Directory.Exists);

        ProcessStartInfo psi = new("open") { UseShellExecute = false };
        if (vscode is not null)
        {
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(vscode);
            psi.ArgumentList.Add(_scriptsPath);
            if (!string.IsNullOrWhiteSpace(file))
                psi.ArgumentList.Add(file);
        }
        else if (!string.IsNullOrWhiteSpace(file))
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(file);
        }
        else
        {
            psi.ArgumentList.Add(_scriptsPath);
        }

        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Could not open the editor: {ex.Message}", "Open in editor");
        }
    }

    private void VSCode(string path)
    {
        ProcessStartInfo psi = new("code", $"{_scriptsPath} {path}")
        {
            UseShellExecute = true,
            CreateNoWindow = true
        };

        Process.Start(psi);
    }
}