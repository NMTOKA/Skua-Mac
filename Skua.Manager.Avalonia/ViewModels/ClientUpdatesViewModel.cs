using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Services;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class ClientUpdatesViewModel : BotControlViewModelBase
{
    public ClientUpdatesViewModel(IClientUpdateService updateService, IGetScriptsService scriptsService, IDialogService dialogService, ISettingsService settingsService)
        : base("Client Updates")
    {
        StrongReferenceMessenger.Default.Register<ClientUpdatesViewModel, UpdateScriptsMessage>(this, ReceiveUpdateScriptsMessage);
        StrongReferenceMessenger.Default.Register<ClientUpdatesViewModel, CheckClientUpdateMessage>(this, CheckUpdate);

        _updateService = updateService;
        _scriptsService = scriptsService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        Current = ClientFileSources.AssemblyVersion;
        _progress = new Progress<string>(p => ProgressStatus = p);
    }

    private async void CheckUpdate(ClientUpdatesViewModel recipient, CheckClientUpdateMessage message)
    {
        await recipient.Refresh();

        if (recipient.UpdateVisible && recipient._dialogService.ShowMessageBox($"New update available: {recipient.Latest}\r\nDo you want to download it?", "Update Available", true) == true)
            await recipient.Update();
    }

    private async void ReceiveUpdateScriptsMessage(ClientUpdatesViewModel recipient, UpdateScriptsMessage message)
    {
        if (message.Reset)
        {
            await recipient.ResetScripts(default);
            return;
        }

        await recipient.UpdateScripts(default);
    }

    protected override void OnActivated()
    {
        if (Latest is null)
            Refresh();
    }

    protected override void OnDeactivated()
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        base.OnDeactivated();
    }

    private readonly IGetScriptsService _scriptsService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IClientUpdateService _updateService;
    private readonly IProgress<string> _progress;

    public string Current { get; }

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private string? _progressStatus = null;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _updateVisible;

    [ObservableProperty]
    private string? _latest;

    [RelayCommand]
    public async Task Refresh()
    {
        IsBusy = true;
        Status = "Loading...";
        try
        {
            await _updateService.CheckForUpdateAsync();
            UpdateVisible = _updateService.UpdateAvailable;
            Latest = _updateService.LatestVersion;
            Status = UpdateVisible ? $"Update available: {Latest}" : "You have the latest version";

            if (UpdateVisible
                && _settingsService.Get("UseNightlyBuilds", false)
                && _settingsService.Get("AutoUpdateNightlyBuilds", false))
                await Update();
        }
        catch
        {
            Status = "Error while checking for updates";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task Update()
    {
        if (!UpdateVisible)
            return;

        IsBusy = true;
        await _updateService.DownloadUpdateAsync(_progress);
        await Task.Delay(1000);
        ProgressStatus = null;
        IsBusy = false;
    }

    [RelayCommand]
    public async Task ResetScripts(CancellationToken token)
    {
        IsBusy = true;
        string skuaPath = ClientFileSources.SkuaScriptsDIR;
        if (Directory.Exists(skuaPath))
            Directory.Delete(skuaPath, true);

        if (!Directory.Exists(skuaPath))
            Directory.CreateDirectory(skuaPath);

        if (File.Exists(ClientFileSources.SkuaScriptsCommitFile))
            File.Delete(ClientFileSources.SkuaScriptsCommitFile);

        await UpdateScripts(token);
    }

    [RelayCommand]
    public async Task UpdateScripts(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            await _scriptsService.RefreshScriptsAsync(_progress, token);

            int count = await Task.Run(async () => await _scriptsService.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated));
            ProgressStatus = $"Downloaded {count} scripts.";
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Task cancelled.";
        }
        catch { }
        finally
        {
            // Reset deletes the whole Scripts folder, including UltrasLW. Put the bundled copy back.
            ClientFilesService.InstallBundledUltrasLW();
            await Task.Delay(1000);
            IsBusy = false;
            ProgressStatus = null;
        }
    }
}
