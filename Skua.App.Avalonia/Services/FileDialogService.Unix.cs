#if !IS_WINDOWS
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

/// <summary>
/// macOS/Linux file dialogs using Avalonia's native pickers. The interface is synchronous (it mirrors the WinForms
/// dialogs on Windows), so each call runs a nested UI loop until the picker closes.
/// </summary>
public class FileDialogService : IFileDialogService
{
    private const string DefaultFilter = "Text Files (*.txt)|*.txt";
    private const string AllFilesFilter = "All Files (*.*)|*.*";

    public string? OpenFile() => OpenFile(ClientFileSources.SkuaDIR, AllFilesFilter);

    public string? OpenFile(string filters) => OpenFile(ClientFileSources.SkuaDIR, filters);

    public string? OpenFile(string initialDirectory, string filters)
    {
        return RunModal(async provider =>
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = ParseFilters(string.IsNullOrWhiteSpace(filters) ? AllFilesFilter : filters),
                SuggestedStartLocation = await TryGetFolder(provider, initialDirectory),
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    public string? OpenFolder() => OpenFolder(ClientFileSources.SkuaDIR);

    public string? OpenFolder(string initialDirectory) => RunModal(provider => PickFolder(provider, initialDirectory));

    public Task<string?> OpenFolderAsync() => OpenFolderAsync(ClientFileSources.SkuaDIR);

    public async Task<string?> OpenFolderAsync(string initialDirectory)
    {
        return GetStorageProvider() is IStorageProvider provider ? await PickFolder(provider, initialDirectory) : null;
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
        var types = ParseFilters(string.IsNullOrWhiteSpace(filters) ? DefaultFilter : filters);
        return RunModal(async provider =>
        {
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                FileTypeChoices = types,
                DefaultExtension = types.SelectMany(t => t.Patterns ?? Array.Empty<string>())
                    .Select(p => p.TrimStart('*', '.')).FirstOrDefault(e => e.Length > 0 && e != "*"),
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await TryGetFolder(provider, initialDirectory),
            });
            return file?.TryGetLocalPath();
        });
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

    private static async Task<string?> PickFolder(IStorageProvider provider, string initialDirectory)
    {
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolder(provider, initialDirectory),
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static async Task<IStorageFolder?> TryGetFolder(IStorageProvider provider, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;
        try
        {
            return await provider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Converts a WinForms filter ("Name (*.a;*.b)|*.a;*.b|...") into picker file types.</summary>
    private static List<FilePickerFileType> ParseFilters(string filters)
    {
        List<FilePickerFileType> types = new();
        string[] parts = filters.Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string[] patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            types.Add(new FilePickerFileType(parts[i].Trim())
            {
                Patterns = patterns,
                // macOS pickers filter by extension; "*.*" means everything.
                AppleUniformTypeIdentifiers = patterns.Contains("*.*") ? new[] { "public.item" } : null,
            });
        }
        return types;
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        Window? owner = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        return owner?.StorageProvider;
    }

    /// <summary>Runs an async picker and blocks the caller until it finishes, keeping the UI responsive.</summary>
    private static string? RunModal(Func<IStorageProvider, Task<string?>> pick)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(() => RunModal(pick)).GetAwaiter().GetResult();

        if (GetStorageProvider() is not IStorageProvider provider)
            return null;

        Task<string?> task = pick(provider);
        if (!task.IsCompleted)
        {
            DispatcherFrame frame = new();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.IsCompletedSuccessfully ? task.Result : null;
    }
}
#endif
