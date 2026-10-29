#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.App.Avalonia.Flash;
using Skua.App.Avalonia.Services;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using System;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

// macOS/Linux: the game runs in the separate Skua.FlashHost window instead of the embedded FlashHost control.
public partial class MainWindow
{
    private bool _closingForGood;

    private FlashUtil? ExternalHost => _flash as FlashUtil;

    private void AttachExternalGameHost()
    {
        if (ExternalHost is not FlashUtil host)
            return;

        _flash.FlashCall += OnExternalHostCall;
        host.HostExited += OnHostExited;
        ArmyChannel.Listen(OnArmyCommand);
        StartCpuSaver();
        Closed += (_, _) => ArmyChannel.Stop();

        // SKUA_FPS_OVERLAY=1 shows Avalonia's frame-rate overlay (diagnosing redraws that waste CPU).
        string? overlay = Environment.GetEnvironmentVariable("SKUA_FPS_OVERLAY");
        if (overlay == "1")
            RendererDiagnostics.DebugOverlays = global::Avalonia.Rendering.RendererDebugOverlays.Fps;
        else if (overlay == "dirty")
            RendererDiagnostics.DebugOverlays = global::Avalonia.Rendering.RendererDebugOverlays.DirtyRects;
        Closing += (_, _) => _closingForGood = true;

        if (FlashUtil.IsDocked)
        {
            PositionChanged += (_, _) => DockGameWindow();
            SizeChanged += (_, _) => DockGameWindow();
            Activated += (_, _) => ExternalHost?.RaiseHost();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty)
                    OnWindowStateChanged();
            };
        }
    }

    private bool _docked;

    /// <summary>
    /// Shrinks this window to its title and menu bars and keeps the frameless game window directly underneath,
    /// at the same width, so the pair looks and moves like the single embedded window on Windows.
    /// </summary>
    private void EnterDockedLayout()
    {
        if (_docked || !FlashUtil.IsDocked)
            return;
        _docked = true;

        double chrome = TitleBar.Bounds.Height + TopNavBorder.Bounds.Height;
        MinHeight = chrome;
        MaxHeight = chrome;
        Height = chrome;
        MinWidth = DockedMinWidth;
        if (Width < DockedMinWidth)
            Width = DockedMinWidth;
        MacWindowChrome.DisableFullScreen(this);
        MacWindowChrome.RedirectZoomButton(this, ToggleDockedZoom);
        TitleBar.ZoomRequested += (_, e) =>
        {
            e.Cancel = true;
            ToggleDockedZoom();
        };
        KeepDockedPairOnScreen();
        DockGameWindow();
    }

    // Narrower than this and the menu bar no longer fits.
    private const double DockedMinWidth = 640;

    private double GameHeightFor(double width) => Math.Round(width * GameAspect);

    // skua.swf's stage is 958x550.
    private const double GameAspect = 550.0 / 958.0;

    private void DockGameWindow()
    {
        if (!_docked || ExternalHost is not FlashUtil host || WindowState == WindowState.Minimized)
            return;

        double scaling = DesktopScaling > 0 ? DesktopScaling : 1;
        double x = Position.X / scaling;
        double y = Position.Y / scaling + Bounds.Height;
        double width = Bounds.Width;
        host.DockHost(x, y, width, GameHeightFor(width));
    }

    /// <summary>Moves this window up if the game window would hang off the bottom of the screen.</summary>
    private void KeepDockedPairOnScreen()
    {
        Screen? screen = Screens.ScreenFromWindow(this);
        if (screen is null)
            return;

        double scaling = DesktopScaling > 0 ? DesktopScaling : 1;
        double bottom = Position.Y / scaling + Height + GameHeightFor(Bounds.Width);
        double overflow = bottom - screen.WorkingArea.Bottom / scaling;
        if (overflow > 0)
            Position = new PixelPoint(Position.X, Math.Max(screen.WorkingArea.Y, (int)(Position.Y - overflow * scaling)));
    }

    // Where the bar was before the green button zoomed it, and the width it was zoomed to.
    private PixelPoint _unzoomedPosition;
    private double _unzoomedWidth, _zoomedWidth;

    /// <summary>
    /// The green window button (or double-clicking the title bar): grows the bar and the game together until the game
    /// fills the screen, keeping its shape; again puts them back. macOS's own zoom can't, because the bar's height is
    /// fixed and the game is a separate window.
    /// </summary>
    private void ToggleDockedZoom()
    {
        if (_zoomedWidth > 0 && Math.Abs(Width - _zoomedWidth) < 1)
        {
            _zoomedWidth = 0;
            Width = _unzoomedWidth;
            Position = _unzoomedPosition;
            return;
        }

        Screen? screen = Screens.ScreenFromWindow(this);
        if (screen is null)
            return;

        double scaling = DesktopScaling > 0 ? DesktopScaling : 1;
        PixelRect area = screen.WorkingArea;
        double areaWidth = area.Width / scaling;
        double width = Math.Floor(Math.Max(DockedMinWidth, Math.Min(areaWidth, (area.Height / scaling - Bounds.Height) / GameAspect)));

        _unzoomedPosition = Position;
        _unzoomedWidth = Width;
        _zoomedWidth = width;
        Width = width;
        Position = new PixelPoint(area.X + (int)((areaWidth - width) / 2 * scaling), area.Y);
    }

    private void OnWindowStateChanged()
    {
        if (!_docked || ExternalHost is not FlashUtil host)
            return;

        if (WindowState is WindowState.Maximized or WindowState.FullScreen)
        {
            // Zoomed or full screen some other way than the green button (which zooms the docked pair itself):
            // the fixed-height bar can't fill the screen, so put it back.
            WindowState = WindowState.Normal;
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            host.SetHostVisible(false);
        }
        else
        {
            if (!_options.HeadlessMode)
                host.SetHostVisible(true);
            DockGameWindow();
        }
    }

    private void OnExternalHostCall(string function, params object[] args)
    {
        switch (function)
        {
            case "requestLoadGame":
                // First message from a freshly started game window: attach it under the toolbar.
                if (_docked)
                    DockGameWindow();
                else
                    EnterDockedLayout();
                break;

            case "loaded":
                ExternalHost?.SetHostTitle(_mainViewModel.Title ?? "VibeSkua");
                ApplyExternalHeadless(_options.HeadlessMode);
                break;

            case FlashUtil.HostKeyEvent when args is { Length: > 0 } && args[0] is string spec:
                if (TryParseHostKey(spec, out Key key, out KeyModifiers modifiers)
                    && Ioc.Default.GetService<IHotKeyService>() is HotKeyService hotKeys)
                    hotKeys.HandleKey(key, modifiers);
                break;
        }
    }

    private void ApplyExternalHeadless(bool isHeadless)
    {
        ExternalHost?.SetHostVisible(!isHeadless);
    }

    private void OnHostExited()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closingForGood)
                return;

            _isFlashLoaded = false;
            bool? reopen = Ioc.Default.GetService<IDialogService>()?.ShowMessageBox(
                "The game window was closed. Reopen it?", "Game window closed", true);
            if (reopen == true)
                _flash.InitializeFlash();
        });
    }

    /// <summary>An army action from another Skua Mac window (or the Army menu of this one).</summary>
    private Task<int> OnArmyCommand(ArmyChannel.Command command)
    {
        if (command.Msg == ArmyChannel.StatusMessage)
            return Dispatcher.UIThread.InvokeAsync(ArmyStatusFlags).GetTask();

        return Dispatcher.UIThread.InvokeAsync(() =>
            HandleArmyMessage(command.Msg, new IntPtr(command.W), new IntPtr(command.L), command.Data)?.ToInt32() ?? -1).GetTask();
    }

    private static int ArmyStatusFlags()
    {
        int flags = 0;
        try
        {
            if (Ioc.Default.GetRequiredService<IScriptInterface>().Player.LoggedIn)
                flags |= ArmyChannel.StatusLoggedIn;
            if (Ioc.Default.GetRequiredService<IScriptManager>().ScriptRunning)
                flags |= ArmyChannel.StatusScriptRunning;
        }
        catch { }
        return flags;
    }

    private MenuItem? _armyMenu, _armyStatus;
    private readonly System.Collections.Generic.List<MenuItem> _armyWindowItems = new();
    private MenuItem? _armyLagKiller, _armyHeadless, _armyHidePlayers;

    /// <summary>
    /// The Army menu: what the Windows tab host's army controls do, for every running Skua Mac window.
    /// </summary>
    private MenuItem CreateArmyMenu()
    {
        _armyStatus = new MenuItem { IsEnabled = false };
        _armyLagKiller = ArmyToggle(ArmyChannel.OptionLagKiller, () => _options.LagKiller);
        _armyHeadless = ArmyToggle(ArmyChannel.OptionHeadless, () => _options.HeadlessMode);
        _armyHidePlayers = ArmyToggle(ArmyChannel.OptionHidePlayers, () => _options.HidePlayers);

        MenuItem army = new() { Header = "Army" };
        _armyMenu = army;
        army.Classes.Add("top-nav-item");
        army.Items.Add(_armyStatus);
        army.Items.Add(new Separator());
        army.Items.Add(ArmyItem("Start scripts on all windows", () =>
            ArmyChannel.BroadcastAsync(ArmyChannel.SetOptionMessage, ArmyChannel.OptionScript, 1)));
        army.Items.Add(ArmyItem("Stop scripts on all windows", () =>
            ArmyChannel.BroadcastAsync(ArmyChannel.SetOptionMessage, ArmyChannel.OptionScript, 0)));
        army.Items.Add(ArmyItem("Load a script on all windows...", ArmyChannel.PickAndLoadScriptOnAll));
        army.Items.Add(new Separator());
        army.Items.Add(ArmyItem("Log in all windows", () =>
            ArmyChannel.BroadcastAsync(ArmyChannel.LoginMessage, delayBetween: TimeSpan.FromSeconds(2))));
        army.Items.Add(ArmyItem("Log out all windows", () => ArmyChannel.BroadcastAsync(ArmyChannel.LogoutMessage)));
        army.Items.Add(ArmyItem("Send all to map...", () =>
        {
            string? target = AskArmyTarget("Send Army to Map", "Map and cell (map,cell):", "e.g. yulgar-1,Enter");
            if (target is null)
                return null;
            string[] parts = target.Split(',', 2);
            string cell = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : "Enter";
            return ArmyChannel.BroadcastAsync(ArmyChannel.JumpMapMessage, data: $"{parts[0].Trim()}\n{cell}");
        }));
        army.Items.Add(ArmyItem("Send all to player...", () =>
        {
            string? target = AskArmyTarget("Send Army to Player", "Player name:", "e.g. Artix");
            return target is null ? null : ArmyChannel.BroadcastAsync(ArmyChannel.JumpPlayerMessage, data: target);
        }));
        army.Items.Add(new Separator());
        army.Items.Add(_armyLagKiller);
        army.Items.Add(_armyHeadless);
        army.Items.Add(_armyHidePlayers);
        _armyCpuSaver = new MenuItem();
        _armyCpuSaver.Click += (_, _) =>
        {
            CpuSaverEnabled = !CpuSaverEnabled;
            RefreshArmyMenu();
        };
        army.Items.Add(_armyCpuSaver);
        army.Items.Add(new Separator());
        army.Items.Add(ArmyItem("New Skua Window", () =>
        {
            MacInstances.LaunchNewInstance();
            return null;
        }));

        army.SubmenuOpened += (_, _) => RefreshArmyMenu();
        RefreshArmyMenu();
        return army;
    }

    private void RefreshArmyMenu()
    {
        System.Collections.Generic.List<int> pids = ArmyChannel.Instances();
        int count = pids.Count;
        if (_armyStatus is not null)
            _armyStatus.Header = count == 1 ? "1 Skua Mac window running" : $"{count} Skua Mac windows running";
        RefreshArmyWindowList(pids);
        // Each toggle says what clicking it does, based on this window's setting.
        if (_armyLagKiller is not null)
            _armyLagKiller.Header = $"Turn Lag Killer {(_options.LagKiller ? "off" : "on")} on all windows";
        if (_armyHeadless is not null)
            _armyHeadless.Header = _options.HeadlessMode ? "Show the game on all windows" : "Hide the game (headless) on all windows";
        if (_armyHidePlayers is not null)
            _armyHidePlayers.Header = $"{(_options.HidePlayers ? "Show" : "Hide")} other players on all windows";
        if (_armyCpuSaver is not null)
            _armyCpuSaver.Header = CpuSaverEnabled
                ? "Stop saving CPU on windows you're not using"
                : "Save CPU on windows you're not using";
    }

    private MenuItem? _armyCpuSaver;
    private DispatcherTimer? _cpuSaverTimer;
    private bool _cpuSaverThrottled;

    // One switch for every window: a file the others see within a second, so it needs no army message.
    private static readonly string CpuSaverFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "save-cpu-background");

    private static bool CpuSaverEnabled
    {
        get => System.IO.File.Exists(CpuSaverFile);
        set
        {
            try
            {
                if (value)
                    System.IO.File.WriteAllText(CpuSaverFile, "on\n");
                else
                    System.IO.File.Delete(CpuSaverFile);
            }
            catch { }
        }
    }

    /// <summary>
    /// With "Save CPU on windows you're not using" on, a window whose Skua bar and game window are both in the
    /// background runs the game at 2 frames a second in low quality (the Windows tab host's throttle for hidden
    /// tabs), and goes back to normal as soon as the user switches to it. Bots keep running either way.
    /// </summary>
    private void StartCpuSaver()
    {
        _cpuSaverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cpuSaverTimer.Tick += (_, _) =>
        {
            bool background = false;
            if (CpuSaverEnabled && _isFlashLoaded)
            {
                int front = MacInstances.FrontmostProcessId();
                background = front > 0 && front != Environment.ProcessId && front != ExternalHost?.HostProcessId;
            }
            if (background == _cpuSaverThrottled)
                return;
            _cpuSaverThrottled = background;
            ArmyThrottle(background);
        };
        _cpuSaverTimer.Start();
        Closed += (_, _) => _cpuSaverTimer?.Stop();
    }

    /// <summary>
    /// One line per running window under the count: its character and whether it is logged in and running a
    /// script. Lines start as "checking..." and fill in as each window answers.
    /// </summary>
    private void RefreshArmyWindowList(System.Collections.Generic.List<int> pids)
    {
        if (_armyMenu is null || _armyStatus is null)
            return;

        foreach (MenuItem old in _armyWindowItems)
            _armyMenu.Items.Remove(old);
        _armyWindowItems.Clear();

        int index = _armyMenu.Items.IndexOf(_armyStatus) + 1;
        for (int i = 0; i < pids.Count; i++)
        {
            int pid = pids[i];
            string who = ArmyChannel.CharacterName(pid) is { Length: > 0 } name ? name : $"Window {i + 1}";
            if (pid == Environment.ProcessId)
                who += " (this window)";

            MenuItem line = new() { Header = $"    {who}: checking...", IsEnabled = false };
            _armyWindowItems.Add(line);
            _armyMenu.Items.Insert(index + i, line);

            _ = Task.Run(async () =>
            {
                int? flags = await ArmyChannel.SendAsync(pid, new ArmyChannel.Command(ArmyChannel.StatusMessage, 0, 0, null));
                string state = flags switch
                {
                    null or < 0 => "not responding",
                    _ when (flags & ArmyChannel.StatusLoggedIn) == 0 => "not logged in",
                    _ when (flags & ArmyChannel.StatusScriptRunning) != 0 => "script running",
                    _ => "logged in, no script running",
                };
                Dispatcher.UIThread.Post(() => line.Header = $"    {who}: {state}");
            });
        }
    }

    /// <summary>A menu item that runs <paramref name="send"/> and shows how many windows it reached.</summary>
    private MenuItem ArmyItem(string header, Func<Task<int>?> send)
    {
        MenuItem item = new() { Header = header };
        item.Click += async (_, _) =>
        {
            try
            {
                Task<int>? sending = send();
                if (sending is null)
                    return;
                if (_armyStatus is not null)
                    _armyStatus.Header = "Sending...";
                int reached = await sending;
                if (_armyStatus is not null)
                    _armyStatus.Header = reached == 1 ? "Last action reached 1 window" : $"Last action reached {reached} windows";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Army: {header} failed: {ex.Message}");
            }
        };
        return item;
    }

    /// <summary>An option switch: sets the opposite of this window's setting on every window.</summary>
    private MenuItem ArmyToggle(int optionId, Func<bool> isOn)
    {
        return ArmyItem(string.Empty, () => ArmyChannel.BroadcastAsync(ArmyChannel.SetOptionMessage, optionId, isOn() ? 0 : 1));
    }

    private static string? AskArmyTarget(string title, string message, string example)
    {
        InputDialogViewModel vm = new(title, message, example, false);
        if (Ioc.Default.GetRequiredService<IDialogService>().ShowDialog(vm) != true)
            return null;
        string text = vm.DialogTextInput?.Trim() ?? "";
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// Parses "code|modifiers" from the host, where code is a DOM KeyboardEvent.code
    /// (KeyA, Digit1, F5, Numpad3, Space, Minus...) and modifiers is a subset of "csa".
    /// </summary>
    private static bool TryParseHostKey(string spec, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;

        string[] parts = spec.Split('|');
        string code = parts[0];
        string mods = parts.Length > 1 ? parts[1] : string.Empty;
        if (mods.Contains('c')) modifiers |= KeyModifiers.Control;
        if (mods.Contains('s')) modifiers |= KeyModifiers.Shift;
        if (mods.Contains('a')) modifiers |= KeyModifiers.Alt;

        string name = code switch
        {
            _ when code.StartsWith("Key") => code[3..],
            _ when code.StartsWith("Digit") => "D" + code[5..],
            _ when code.StartsWith("Numpad") && code.Length == 7 && char.IsDigit(code[6]) => "NumPad" + code[6],
            "Minus" => nameof(Key.OemMinus),
            "Equal" => nameof(Key.OemPlus),
            "Period" => nameof(Key.OemPeriod),
            "Slash" => nameof(Key.OemQuestion),
            "Backquote" => nameof(Key.OemTilde),
            "NumpadAdd" => nameof(Key.Add),
            "NumpadSubtract" => nameof(Key.Subtract),
            "NumpadMultiply" => nameof(Key.Multiply),
            "NumpadDivide" => nameof(Key.Divide),
            "NumpadDecimal" => nameof(Key.Decimal),
            "ArrowUp" => nameof(Key.Up),
            "ArrowDown" => nameof(Key.Down),
            "ArrowLeft" => nameof(Key.Left),
            "ArrowRight" => nameof(Key.Right),
            _ => code
        };

        return Enum.TryParse(name, ignoreCase: true, out key) && key != Key.None;
    }
}
#endif
