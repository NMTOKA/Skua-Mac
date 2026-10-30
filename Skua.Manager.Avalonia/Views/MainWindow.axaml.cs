using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Manager.Avalonia.ViewModels;
using System;
using System.Runtime.InteropServices;

namespace Skua.Manager.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly ManagerMainViewModel _viewModel;
    private bool _allowClose;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<ManagerMainViewModel>();
        DataContext = _viewModel;

        StrongReferenceMessenger.Default.Register<MainWindow, ShowMainWindowMessage>(this, ShowManager);
        StrongReferenceMessenger.Default.Register<MainWindow, ExitManagerMessage>(this, AllowClose);

        Opened += (s, e) =>
        {
            try
            {
                Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this);
            }
            catch { }
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // On macOS there's no tray to come back from, so closing the window (or Quit) quits the Manager.
        if (OperatingSystem.IsMacOS() && !_allowClose)
        {
            _allowClose = true;
            if (e.CloseReason != WindowCloseReason.ApplicationShutdown
                && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                e.Cancel = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                return;
            }
        }

        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _viewModel.IsActive = false;
            return;
        }

        _viewModel.IsActive = false;
        StrongReferenceMessenger.Default.UnregisterAll(this);
        base.OnClosing(e);
    }

    private void ShowManager(MainWindow recipient, ShowMainWindowMessage message)
    {
        if (!recipient.IsVisible)
        {
            recipient.Show();
        }

        if (recipient.WindowState == WindowState.Minimized)
            recipient.WindowState = WindowState.Normal;

        recipient.Activate();
        recipient.Topmost = true;
        recipient.Topmost = false;
        recipient.Focus();

        if (OperatingSystem.IsWindows() && recipient.TryGetPlatformHandle() is { Handle: var handle } && handle != IntPtr.Zero)
        {
            ShowWindow(handle, 9); // SW_RESTORE
            SetForegroundWindow(handle);
        }
    }

    private void AllowClose(MainWindow recipient, ExitManagerMessage message)
    {
        recipient._allowClose = true;
    }
}
