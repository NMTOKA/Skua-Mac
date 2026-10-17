using Skua.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

#if IS_WINDOWS
using Skua.Core.Models;
using System.IO;
using System.Linq;
using global::Avalonia.Platform.Storage;
using WinForms = System.Windows.Forms;
public class FileDialogService : IFileDialogService
{
    private const string DefaultFilter = "Text Files (*.txt)|*.txt";

    public string? OpenFile() => OpenFile(ClientFileSources.SkuaDIR, "All Files (*.*)|*.*");

    public string? OpenFile(string filters) => OpenFile(ClientFileSources.SkuaDIR, filters);

    public string? OpenFile(string initialDirectory, string filters)
    {
        using WinForms.OpenFileDialog dialog = new()
        {
            InitialDirectory = initialDirectory,
            Filter = string.IsNullOrWhiteSpace(filters) ? "All Files (*.*)|*.*" : filters
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
    }

    public string? OpenFolder() => OpenFolder(ClientFileSources.SkuaDIR);

    public string? OpenFolder(string initialDirectory)
    {
        using WinForms.FolderBrowserDialog dialog = new()
        {
            Description = "Select the folder.",
            UseDescriptionForTitle = true,
            InitialDirectory = initialDirectory,
            AutoUpgradeEnabled = true
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public Task<string?> OpenFolderAsync() => OpenFolderAsync(ClientFileSources.SkuaDIR);

    public async Task<string?> OpenFolderAsync(string initialDirectory)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.StorageProvider is global::Avalonia.Platform.Storage.IStorageProvider provider)
        {
            var options = new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = !string.IsNullOrWhiteSpace(initialDirectory)
                    ? await provider.TryGetFolderFromPathAsync(initialDirectory)
                    : null
            };
            var folders = await provider.OpenFolderPickerAsync(options);
            string? localPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
                return localPath;
            if (folders.Count == 0)
                return null;
        }

        return OpenFolder(initialDirectory);
    }

    public IEnumerable<string>? OpenText()
    {
        string? path = OpenFile(ClientFileSources.SkuaDIR, DefaultFilter);
        return string.IsNullOrWhiteSpace(path) ? null : File.ReadAllLines(path);
    }

    public string? Save() => Save(ClientFileSources.SkuaDIR, DefaultFilter);

    public string? Save(string filters) => Save(ClientFileSources.SkuaDIR, filters);

    public string? Save(string initialDirectory, string filters)
    {
        using WinForms.SaveFileDialog dialog = new()
        {
            InitialDirectory = initialDirectory,
            Filter = string.IsNullOrWhiteSpace(filters) ? DefaultFilter : filters
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
    }

    public void SaveText(string contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllText(path, contents);
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllLines(path, contents);
    }
}
#endif
