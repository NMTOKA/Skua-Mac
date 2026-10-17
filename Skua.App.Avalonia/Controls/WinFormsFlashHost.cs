using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using System;

namespace Skua.App.Avalonia.Controls;

#if IS_WINDOWS
using WinForms = System.Windows.Forms;
public class WinFormsFlashHost : NativeControlHost
{
    private WinForms.Panel? _panel;
    private WinForms.Control? _child;

    public void SetChild(WinForms.Control child)
    {
        _child = child;
        if (_panel is null)
            return;

        AttachChildToPanel();
    }

    public void SetScaleMode(bool isExactFit = false)
    {
        ApplyScaleMode();
    }

    private void ApplyScaleMode()
    {
        if (_child is AxShockwaveFlashObjects.AxShockwaveFlash flash)
        {
            try
            {
                flash.ScaleMode = 0;
                flash.CtlScale = "ShowAll";
            }
            catch { }
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _panel = new WinForms.Panel
        {
            Dock = WinForms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(20, 20, 20)
        };

        _panel.Resize += (s, e) => ApplyScaleMode();

        if (_child is not null)
            AttachChildToPanel();

        return new PlatformHandle(_panel.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_panel is not null)
        {
            _panel.Controls.Clear();
            _panel.Dispose();
            _panel = null;
        }
    }

    private void AttachChildToPanel()
    {
        if (_panel is null || _child is null)
            return;

        if (_child.Parent is not null && _child.Parent != _panel)
            _child.Parent.Controls.Remove(_child);

        _child.Dock = WinForms.DockStyle.Fill;
        _panel.Controls.Clear();
        _panel.Controls.Add(_child);
        ApplyScaleMode();
    }
}
#else
/// <summary>
/// Off Windows the game runs in the separate Skua.FlashHost window, so this area shows a pointer to it
/// instead of an embedded (and otherwise empty, black) native view.
/// </summary>
public class WinFormsFlashHost : ContentControl
{
    public WinFormsFlashHost()
    {
        Button show = new()
        {
            Content = "Show game window",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        show.Click += (_, _) =>
        {
            if (Ioc.Default.GetService<IFlashUtil>() is Skua.App.Avalonia.Flash.FlashUtil flash)
                flash.FocusHost();
        };

        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "The game is running in its own window",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Use the menus above for scripts, options and tools.",
                    Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                show,
            }
        };
    }

    public void SetScaleMode(bool isExactFit = false) { }
}
#endif
