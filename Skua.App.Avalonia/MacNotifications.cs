#if !IS_WINDOWS
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Skua.App.Avalonia;

/// <summary>
/// macOS notifications when a script stops, a script crashes or the character dies, shown only while you're
/// using another app (not this Skua Mac window or its game). Chosen in Skua Mac menu > Notifications; saved in
/// ~/Library/Application Support/VibeSkua/notifications.
/// </summary>
internal static class MacNotifications
{
    [Flags]
    private enum Kind
    {
        None = 0,
        Stopped = 1,
        Crashed = 2,
        Died = 4,
    }

    private const Kind Default = Kind.Stopped | Kind.Crashed;

    private static readonly string ChoiceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "notifications");

    private static readonly object Recipient = new();
    private static Kind _enabled = LoadChoice();
    private static DateTime _lastCrash;
    private static DateTime _lastDeath;

    /// <summary>The game window's process, so a notification isn't shown while you're looking at the game.</summary>
    public static Func<int?>? GameWindowProcessId { get; set; }

    public static void Start()
    {
        StrongReferenceMessenger.Default.Register<object, ScriptErrorMessage, int>(Recipient, (int)MessageChannels.ScriptStatus,
            (_, m) => OnUiThread(() =>
            {
                _lastCrash = DateTime.UtcNow;
                Notify(Kind.Crashed, "Script crashed", $"{ScriptName()} stopped with an error: {m.Exception.InnerException?.Message ?? m.Exception.Message}");
            }));
        StrongReferenceMessenger.Default.Register<object, ScriptStoppedMessage, int>(Recipient, (int)MessageChannels.ScriptStatus,
            (_, _) => OnUiThread(() =>
            {
                // A crash is followed by a stop; one notification is enough.
                if (DateTime.UtcNow - _lastCrash > TimeSpan.FromSeconds(10))
                    Notify(Kind.Stopped, "Script stopped", $"{ScriptName()} has stopped.");
            }));
        StrongReferenceMessenger.Default.Register<object, PlayerDeathMessage, int>(Recipient, (int)MessageChannels.GameEvents,
            (_, _) => OnUiThread(() =>
            {
                // Farming scripts can die often; at most one a minute.
                if (DateTime.UtcNow - _lastDeath < TimeSpan.FromMinutes(1))
                    return;
                _lastDeath = DateTime.UtcNow;
                Notify(Kind.Died, "Character died", "Your character died.");
            }));

        if (_enabled != Kind.None)
            RequestPermission();
    }

    /// <summary>Skua Mac menu > Notifications.</summary>
    public static NativeMenuItem CreateMenu()
    {
        NativeMenu menu = new();
        menu.Items.Add(Toggle("When a script stops", Kind.Stopped));
        menu.Items.Add(Toggle("When a script crashes", Kind.Crashed));
        menu.Items.Add(Toggle("When your character dies", Kind.Died));
        return new NativeMenuItem("Notifications") { Menu = menu };
    }

    private static NativeMenuItem Toggle(string header, Kind kind)
    {
        NativeMenuItem item = new(header) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = _enabled.HasFlag(kind) };
        item.Click += (_, _) =>
        {
            _enabled ^= kind;
            item.IsChecked = _enabled.HasFlag(kind);
            SaveChoice();
            if (_enabled.HasFlag(kind))
                RequestPermission();
        };
        return item;
    }

    private static void Notify(Kind kind, string title, string body)
    {
        if (!_enabled.HasFlag(kind) || UserIsLookingAtThisBot())
            return;

        string who = CharacterName();
        Post(who.Length > 0 ? $"{title} ({who})" : title, body);
    }

    private static bool UserIsLookingAtThisBot()
    {
        int front = MacInstances.FrontmostProcessId();
        return front == Environment.ProcessId || (front > 0 && front == GameWindowProcessId?.Invoke());
    }

    private static string ScriptName()
    {
        string? path = Ioc.Default.GetService<IScriptManager>()?.LoadedScript;
        return string.IsNullOrWhiteSpace(path) ? "The script" : Path.GetFileNameWithoutExtension(path);
    }

    private static string CharacterName()
    {
        string? name = Ioc.Default.GetService<IScriptInterface>()?.Player?.Username;
        return string.IsNullOrWhiteSpace(name) || name == "loginInfo.strUsername" ? string.Empty : name;
    }

    private static void OnUiThread(Action action) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Notification failed: {ex.Message}");
        }
    });

    private static Kind LoadChoice()
    {
        try
        {
            if (!File.Exists(ChoiceFile))
                return Default;
            string[] words = File.ReadAllText(ChoiceFile).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Aggregate(Kind.None, (k, w) => k | (Enum.TryParse(w, true, out Kind parsed) ? parsed : Kind.None));
        }
        catch
        {
            return Default;
        }
    }

    private static void SaveChoice()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChoiceFile)!);
            string words = string.Join(' ', new[] { Kind.Stopped, Kind.Crashed, Kind.Died }.Where(k => _enabled.HasFlag(k)));
            File.WriteAllText(ChoiceFile, (words.Length > 0 ? words : "none") + "\n");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the notification choice: {ex.Message}");
        }
    }

    // UserNotifications through the Objective-C runtime. Completion handlers are blocks: a static "global" block
    // (no captured state) is a small fixed struct, built once below.

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendPtrPtr(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr3(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendNUIntPtr(IntPtr receiver, IntPtr selector, nuint a, IntPtr b);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private delegate void AuthorizationHandler(IntPtr block, byte granted, IntPtr error);

    private static readonly AuthorizationHandler AuthorizationDone = (_, granted, _) =>
        Trace.WriteLine($"Notifications allowed: {granted != 0}");

    private static IntPtr _center;
    private static IntPtr _authorizationBlock;

    private static IntPtr Center()
    {
        if (_center != IntPtr.Zero)
            return _center;
        // UserNotifications aborts the process outside an app bundle (a development build run from bin/).
        if (!AppContext.BaseDirectory.Contains(".app/Contents/", StringComparison.Ordinal))
            return IntPtr.Zero;
        dlopen("/System/Library/Frameworks/UserNotifications.framework/UserNotifications", 1);
        _center = Send(objc_getClass("UNUserNotificationCenter"), sel_registerName("currentNotificationCenter"));
        return _center;
    }

    private static void RequestPermission()
    {
        try
        {
            if (Center() == IntPtr.Zero)
                return;
            _authorizationBlock = _authorizationBlock != IntPtr.Zero
                ? _authorizationBlock
                : GlobalBlock(Marshal.GetFunctionPointerForDelegate(AuthorizationDone));
            const nuint alertAndSound = 4 | 2; // UNAuthorizationOptionAlert | UNAuthorizationOptionSound
            SendNUIntPtr(_center, sel_registerName("requestAuthorizationWithOptions:completionHandler:"), alertAndSound, _authorizationBlock);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not ask for notification permission: {ex.Message}");
        }
    }

    private static void Post(string title, string body)
    {
        if (Center() == IntPtr.Zero)
            return;

        IntPtr content = Send(Send(objc_getClass("UNMutableNotificationContent"), sel_registerName("alloc")), sel_registerName("init"));
        SendPtr(content, sel_registerName("setTitle:"), NSString(title));
        SendPtr(content, sel_registerName("setBody:"), NSString(body));
        SendPtr(content, sel_registerName("setSound:"), Send(objc_getClass("UNNotificationSound"), sel_registerName("defaultSound")));

        IntPtr request = SendPtr3(objc_getClass("UNNotificationRequest"),
            sel_registerName("requestWithIdentifier:content:trigger:"), NSString(Guid.NewGuid().ToString()), content, IntPtr.Zero);
        SendPtrPtr(_center, sel_registerName("addNotificationRequest:withCompletionHandler:"), request, IntPtr.Zero);
        Send(content, sel_registerName("release"));
    }

    private static IntPtr NSString(string text) =>
        SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);

    /// <summary>A block literal with no captured variables, kept for the life of the app.</summary>
    private static IntPtr GlobalBlock(IntPtr invoke)
    {
        const int blockIsGlobal = 1 << 28;
        IntPtr descriptor = Marshal.AllocHGlobal(2 * IntPtr.Size);
        Marshal.WriteIntPtr(descriptor, 0, IntPtr.Zero);                         // reserved
        Marshal.WriteIntPtr(descriptor, IntPtr.Size, 2 * IntPtr.Size + 8 + IntPtr.Size); // size of the block below

        IntPtr block = Marshal.AllocHGlobal(2 * IntPtr.Size + 8 + IntPtr.Size);
        Marshal.WriteIntPtr(block, 0, dlsym(new IntPtr(-2), "_NSConcreteGlobalBlock")); // isa (RTLD_DEFAULT)
        Marshal.WriteInt32(block, IntPtr.Size, blockIsGlobal);                   // flags
        Marshal.WriteInt32(block, IntPtr.Size + 4, 0);                           // reserved
        Marshal.WriteIntPtr(block, IntPtr.Size + 8, invoke);                     // invoke
        Marshal.WriteIntPtr(block, 2 * IntPtr.Size + 8, descriptor);             // descriptor
        return block;
    }
}
#endif
