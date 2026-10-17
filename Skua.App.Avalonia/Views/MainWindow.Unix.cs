#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.App.Avalonia.Flash;
using Skua.App.Avalonia.Services;
using Skua.Core.Interfaces;
using System;

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
        KeepDockedPairOnScreen();
        DockGameWindow();
    }

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

    private void OnWindowStateChanged()
    {
        if (!_docked || ExternalHost is not FlashUtil host)
            return;

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
