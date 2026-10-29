#if !IS_WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Skua.App.Avalonia;

/// <summary>
/// Puts a copy of the Skua Mac bar's menus (Scripts, Options, ..., Army) in the macOS menu bar at the top of the
/// screen. The bar keeps its own menus: while the game window is in front, the menu bar belongs to the game window.
/// Each native item clicks its bar item, so commands, Army actions and their refresh logic stay in one place.
/// macOS shows the menus of the window in front, so every Skua window (Settings, tools, ...) gets its own copy.
/// </summary>
internal sealed class MacMenuBar
{
    private static Menu? _source;
    private static bool _watchingWindows;

    // One native copy per bar item, reused when a submenu is rebuilt.
    private readonly ConditionalWeakTable<MenuItem, NativeMenuItem> _mirrors = new();

    /// <summary>Uses <paramref name="source"/> as the menu bar of every open and future window. Call again after the bar menus change.</summary>
    public static void Use(Menu source)
    {
        _source = source;
        if (!_watchingWindows)
        {
            _watchingWindows = true;
            Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Apply(window));
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (Window window in desktop.Windows)
                Apply(window);
    }

    private static void Apply(Window window)
    {
        if (_source is not null)
            NativeMenu.SetMenu(window, new MacMenuBar().Mirror(_source));
    }

    private NativeMenu Mirror(Menu source)
    {
        NativeMenu menu = new();
        foreach (object? item in source.Items)
            if (item is MenuItem menuItem)
                menu.Items.Add(MirrorItem(menuItem));
        return menu;
    }

    private NativeMenuItemBase MirrorItem(MenuItem source)
    {
        if (_mirrors.TryGetValue(source, out NativeMenuItem? existing))
            return existing;

        NativeMenuItem item = new();
        _mirrors.Add(source, item);
        Sync(item, source);
        source.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.HeaderProperty || e.Property == MenuItem.IsEnabledProperty
                || e.Property == MenuItem.IsCheckedProperty || e.Property == MenuItem.ToggleTypeProperty)
                Sync(item, source);
        };

        if (source.ItemCount > 0)
        {
            item.Menu = new NativeMenu();
            FillSubmenu(item.Menu, source);
            // Menus such as Army refresh their entries when they open, so rebuild from the bar menu each time.
            item.Menu.Opening += (_, _) =>
            {
                source.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, source));
                FillSubmenu(item.Menu, source);
            };
        }
        else
        {
            item.Click += (_, _) => source.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, source));
        }
        return item;
    }

    private void FillSubmenu(NativeMenu menu, MenuItem source)
    {
        menu.Items.Clear();
        foreach (object? child in source.Items)
        {
            if (child is Separator)
                menu.Items.Add(new NativeMenuItemSeparator());
            else if (child is MenuItem childItem && childItem.IsVisible)
                menu.Items.Add(MirrorItem(childItem));
        }
    }

    private static void Sync(NativeMenuItem item, MenuItem source)
    {
        item.Header = HeaderText(source.Header);
        item.IsEnabled = source.IsEnabled;
        item.ToggleType = source.ToggleType switch
        {
            MenuItemToggleType.CheckBox => NativeMenuItemToggleType.CheckBox,
            MenuItemToggleType.Radio => NativeMenuItemToggleType.Radio,
            _ => NativeMenuItemToggleType.None,
        };
        item.IsChecked = source.IsChecked;
    }

    // Top-level bar items show an icon and a TextBlock; the menu bar only takes text.
    private static string HeaderText(object? header) => header switch
    {
        string text => text,
        TextBlock block => block.Text ?? string.Empty,
        Panel panel => panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty,
        null => string.Empty,
        _ => header.ToString() ?? string.Empty,
    };
}
#endif
