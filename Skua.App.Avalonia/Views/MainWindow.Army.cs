using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.App.Avalonia.ViewModels;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

// Army actions sent to this client by another one. On Windows they arrive as WM_SKUA_* window messages from the
// tab host (WindowSubclassProc); on macOS over the army channel (MainWindow.Unix.cs). Both call these on the UI thread.
public partial class MainWindow
{
    private const int WM_SKUA_LOGOUT = 0x0400 + 448;
    private const int WM_SKUA_THROTTLE = 0x0400 + 452;
    private const int WM_SKUA_ACCEPT_QUEST = 0x0400 + 458;

    /// <summary>
    /// Runs the army action <paramref name="msg"/>. <paramref name="data"/> carries the action's text (script path,
    /// map, player...) when the sender passes it directly; when null it is read from the temp file the tab host writes.
    /// Returns null when <paramref name="msg"/> is not an army action.
    /// </summary>
    internal IntPtr? HandleArmyMessage(uint msg, IntPtr wParam, IntPtr lParam, string? data = null)
    {
        switch (msg)
        {
            case WM_SKUA_GRIDVIEW: ArmySetGridView(wParam.ToInt32() == 1); return IntPtr.Zero;
            case WM_SKUA_LOGIN: ArmyLogin(); return IntPtr.Zero;
            case WM_SKUA_LOGOUT: ArmyLogout(); return IntPtr.Zero;
            case WM_SKUA_JUMP_MAP: ArmyJumpMap(data); return IntPtr.Zero;
            case WM_SKUA_JUMP_PLAYER: ArmyJumpPlayer(data); return IntPtr.Zero;
            case WM_SKUA_LOAD_SCRIPT: ArmyLoadScript(data); return IntPtr.Zero;
            case WM_SKUA_SET_OPTION: ArmySetOption(wParam.ToInt32(), lParam.ToInt32() == 1); return IntPtr.Zero;
            case WM_SKUA_ARMY_SCHEDULER: ArmyStartScheduler(data); return IntPtr.Zero;
            case WM_SKUA_ARMY_SCHEDULER_STOP: ArmyStopScheduler(); return IntPtr.Zero;
            case WM_SKUA_CHECK_LOGIN: return new IntPtr(ArmyIsLoggedIn() ? 1 : 0);
            case WM_SKUA_ACCEPT_QUEST: ArmyAcceptQuest(data); return IntPtr.Zero;
            case WM_SKUA_AUTO_ATTACK: ArmyAutoAttack(wParam.ToInt32() == 1); return IntPtr.Zero;
            case WM_SKUA_THROTTLE: ArmyThrottle(wParam.ToInt32() == 1); return IntPtr.Zero;
            case WM_SKUA_OPEN_BANK: ArmyOpenBank(); return IntPtr.Zero;
            default: return null;
        }
    }

    private static string? ReadArmyData(string? data, string tempFileName)
    {
        if (data is not null)
            return data;
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), tempFileName);
        return System.IO.File.Exists(tempFile) ? System.IO.File.ReadAllText(tempFile) : null;
    }

    private void ArmySetGridView(bool isGrid)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var topNav = this.FindControl<global::Avalonia.Controls.Control>("TopNavBorder");
            if (topNav != null)
                topNav.IsVisible = !isGrid;

            var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
            if (titleBar != null && !_isTitleBarPermanentlyHidden)
                titleBar.IsVisible = !isGrid;

            this.MinWidth = 0;
            this.MinHeight = 0;

            if (FlashHost != null)
                FlashHost.SetScaleMode(false);

            try
            {
                _flash.Call("setGameObject", "stage.scaleMode", "showAll");
            }
            catch { }
        });
    }

    private static void ArmyLogin()
    {
        Task.Run(() =>
        {
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            var servers = Ioc.Default.GetRequiredService<IScriptServers>();
            string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
            servers.Relogin(targetServer);
        });
    }

    private static void ArmyLogout()
    {
        Ioc.Default.GetRequiredService<IScriptServers>().Logout();
    }

    private static void ArmyJumpMap(string? data)
    {
        Task.Run(() =>
        {
            try
            {
                string? text = ReadArmyData(data, "skua_global_jump.txt");
                if (text != null)
                {
                    string[] lines = text.Split('\n');
                    string map = lines.Length > 0 ? lines[0].Trim() : "";
                    string cell = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "Enter";
                    string pad = "Spawn";

                    if (cell.Contains(','))
                    {
                        var cellParts = cell.Split(',');
                        cell = cellParts[0].Trim();
                        if (cellParts.Length > 1) pad = cellParts[1].Trim();
                    }

                    var mapService = Ioc.Default.GetRequiredService<IScriptMap>();
                    if (string.IsNullOrWhiteSpace(map) || mapService.Name.Equals(map.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                        mapService.Jump(cell, pad, false);
                    else
                        mapService.Join(map, cell, pad, true, false);
                }
            } catch { }
        });
    }

    private static void ArmyJumpPlayer(string? data)
    {
        Task.Run(() =>
        {
            try
            {
                string targetPlayer = ReadArmyData(data, "skua_global_jump_player.txt")?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(targetPlayer))
                    Ioc.Default.GetRequiredService<IScriptPlayer>().Goto(targetPlayer);
            } catch { }
        });
    }

    private static void ArmyLoadScript(string? data)
    {
        Task.Run(() =>
        {
            try
            {
                string? targetScript = ReadArmyData(data, "skua_global_script.txt");
                if (!string.IsNullOrWhiteSpace(targetScript) && System.IO.File.Exists(targetScript))
                {
                    var sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (sm.LoadScriptCommand.CanExecute(targetScript))
                            sm.LoadScriptCommand.Execute(targetScript);
                    });
                }
            } catch { }
        });
    }

    private static void ArmySetOption(int optionId, bool value)
    {
        var options = Ioc.Default.GetRequiredService<IScriptOption>();
        switch (optionId)
        {
            case 1: options.LagKiller = value; break;
            case 2: options.HeadlessMode = value; break;
            case 3: options.HidePlayers = value; break;
            case 4: options.DisableFX = value; break;
            case 5: options.InfiniteRange = value; break;
            case 6: options.Magnetise = value; break;
            case 7: options.SkipCutscenes = value; break;
            case 8: options.UseFunctionBasedSkills = value; break;
            case 9: options.StreamerMode = value; break;
            case 99:
                var sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                if (value)
                {
                    if (!sm.ScriptManager.ScriptRunning)
                    {
                        if (sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                    }
                }
                else
                {
                    if (scheduler.IsRunningQueue && scheduler.StopQueueCommand.CanExecute(null))
                        scheduler.StopQueueCommand.Execute(null);
                    else if (sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                        sm.ToggleScriptCommand.Execute(null);
                }
                break;
        }
    }

    private static void ArmyStartScheduler(string? data)
    {
        Task.Run(() =>
        {
            try
            {
                string? json = ReadArmyData(data, "skua_global_playlist.json");
                if (json != null)
                {
                    var items = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel.SavedScriptItem>>(json);
                    var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                    Dispatcher.UIThread.Post(() =>
                    {
                        scheduler.ScriptQueue.Clear();
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                if (System.IO.File.Exists(item.Path))
                                {
                                    var vm = new Skua.Shared.Avalonia.ViewModels.ScriptItemViewModel(item.Path) { Id = item.Id };
                                    if (!string.IsNullOrEmpty(item.Name)) vm.Name = item.Name;
                                    scheduler.ScriptQueue.Add(vm);
                                }
                            }
                        }
                        if (scheduler.ScriptQueue.Count > 0 && !scheduler.IsRunningQueue)
                        {
                            if (scheduler.StartQueueCommand.CanExecute(null))
                                scheduler.StartQueueCommand.Execute(null);
                        }
                    });
                }
            } catch { }
        });
    }

    private static void ArmyStopScheduler()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
            if (scheduler.StopQueueCommand.CanExecute(null))
                scheduler.StopQueueCommand.Execute(null);
        });
    }

    private static bool ArmyIsLoggedIn()
    {
        var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
        return bot.Player.LoggedIn;
    }

    private static void ArmyAcceptQuest(string? data)
    {
        Task.Run(() =>
        {
            try
            {
                string? text = ReadArmyData(data, "skua_global_accept_quest.txt");
                if (text != null)
                {
                    string[] lines = text.Split('\n');
                    string questIdRaw = lines.Length > 0 ? lines[0].Trim() : "";
                    string rewardIdRaw = lines.Length > 1 ? lines[1].Trim() : "-1";
                    int rewardId = int.TryParse(rewardIdRaw, out int rId) ? rId : -1;
                    if (int.TryParse(questIdRaw, out int questId) && questId > 0)
                    {
                        var quest = Ioc.Default.GetRequiredService<IScriptQuest>();
                        quest.RegisterQuests((questId, rewardId));
                    }
                }
            } catch { }
        });
    }

    private static void ArmyAutoAttack(bool start)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (start)
                StrongReferenceMessenger.Default.Send<StartAutoAttackMessage>();
            else
                StrongReferenceMessenger.Default.Send<StopAutoMessage>();
        });
    }

    private void ArmyThrottle(bool throttle)
    {
        var options = Ioc.Default.GetRequiredService<IScriptOption>();
        if (!options.HeadlessMode)
        {
            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
            try
            {
                if (throttle)
                {
                    if (_preThrottleFPS <= 0)
                    {
                        int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                        if (currentFPS > 2)
                            _preThrottleFPS = currentFPS;
                        string? currentQuality = bot.Flash?.GetGameObject<string>("stage.quality");
                        if (!string.IsNullOrEmpty(currentQuality))
                            _preThrottleQuality = currentQuality;
                    }

                    bot.Flash?.SetGameObject("stage.frameRate", 2);
                    bot.Flash?.SetGameObject("stage.quality", "LOW");
                    Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
                }
                else
                {
                    int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                    int targetFPS = _preThrottleFPS > 2 ? _preThrottleFPS : (currentFPS > 2 ? currentFPS : options.SetFPS);
                    string? targetQuality = _preThrottleQuality;

                    _preThrottleFPS = -1;
                    _preThrottleQuality = null;

                    if (targetFPS > 0)
                        bot.Flash?.SetGameObject("stage.frameRate", targetFPS);
                    if (!string.IsNullOrEmpty(targetQuality))
                        bot.Flash?.SetGameObject("stage.quality", targetQuality);
                }
            }
            catch { }
        }
    }

    private static void ArmyOpenBank()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Ioc.Default.GetRequiredService<IScriptBank>().Open();
            }
            catch { }
        });
    }
}
