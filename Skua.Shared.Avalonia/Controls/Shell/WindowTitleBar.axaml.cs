using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.ComponentModel;

namespace Skua.Shared.Avalonia.Controls.Shell;

public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(TitleText), "VibeSkua");

    private static readonly IImage DefaultIconSource = LoadDefaultIconSource();

    public static readonly StyledProperty<IImage?> IconSourceProperty =
        AvaloniaProperty.Register<WindowTitleBar, IImage?>(nameof(IconSource), DefaultIconSource);

    /// <summary>
    /// Raised before the maximise button or a title bar double-click zooms the window. Set Cancel to zoom some
    /// other way.
    /// </summary>
    public event EventHandler<CancelEventArgs>? ZoomRequested;

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public IImage? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public WindowTitleBar()
    {
        InitializeComponent();
        PointerPressed += OnTitleBarPointerPressed;
        MinimizeButton.Click += OnMinimizeClicked;
        MaximizeButton.Click += OnMaximizeClicked;
        CloseButton.Click += OnCloseClicked;
        if (OperatingSystem.IsMacOS())
            UseMacChrome();
        Loaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window && !window.CanResize)
                MaximizeButton.IsVisible = false;
        };
    }

    // A standard macOS title bar: 28 points high, with a small centred title.
    private const double MacTitleBarHeight = 28;
    private const double MacTitleFontSize = 13;

    /// <summary>
    /// On macOS the window keeps its native window buttons (close, minimise, zoom at the top left) drawn over this
    /// bar, instead of the Windows-style buttons at the right.
    /// </summary>
    private void UseMacChrome()
    {
        CaptionButtons.IsVisible = false;
        IconHost.IsVisible = false;
        BarGrid.Height = MacTitleBarHeight;
        Grid.SetColumn(TitleTextHost, 0);
        Grid.SetColumnSpan(TitleTextHost, 3);
        TitleTextHost.Stretch = Stretch.None;
        TitleTextHost.Margin = default;
        TitleTextHost.HorizontalAlignment = HorizontalAlignment.Center;
        TitleTextBlock.FontSize = MacTitleFontSize;
        TitleTextBlock.FontWeight = FontWeight.SemiBold;
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
                return;
            window.SystemDecorations = SystemDecorations.Full;
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            window.ExtendClientAreaTitleBarHeightHint = MacTitleBarHeight;
        };
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not Window window)
            return;

        // macOS: double-clicking a title bar zooms the window.
        if (OperatingSystem.IsMacOS() && e.ClickCount == 2)
        {
            if (window.CanResize)
                ToggleMaximized(window);
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            ToggleMaximized(window);
    }

    private void ToggleMaximized(Window window)
    {
        CancelEventArgs args = new();
        ZoomRequested?.Invoke(this, args);
        if (args.Cancel)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Close();
    }

    private static IImage LoadDefaultIconSource()
    {
        // Keep shell icon parity with WPF title bar artwork so badge tint behavior matches.
        using var stream = AssetLoader.Open(new Uri("avares://Skua.Shared.Avalonia/Assets/SkuaShellImage.png"));
        return new Bitmap(stream);
    }
}
