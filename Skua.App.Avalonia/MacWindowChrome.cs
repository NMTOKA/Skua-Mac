#if !IS_WINDOWS
using Avalonia.Controls;
using System;
using System.Runtime.InteropServices;

namespace Skua.App.Avalonia;

/// <summary>Small AppKit tweaks Avalonia has no property for.</summary>
internal static class MacWindowChrome
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendReturningNUInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendNUInt(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendNInt(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [DllImport(ObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    private const nuint FullScreenPrimary = 1 << 7;
    private const nuint FullScreenAuxiliary = 1 << 8;
    private const nuint FullScreenNone = 1 << 9;

    /// <summary>
    /// Makes the green window button zoom instead of entering full screen. A docked Skua Mac bar in full screen would
    /// be a 66-point strip on its own Space, with the game window left behind.
    /// </summary>
    public static void DisableFullScreen(Window window)
    {
        if (NSWindow(window) is not IntPtr nsWindow)
            return;

        try
        {
            nuint behavior = SendReturningNUInt(nsWindow, sel_registerName("collectionBehavior"));
            behavior = (behavior & ~(FullScreenPrimary | FullScreenAuxiliary)) | FullScreenNone;
            SendNUInt(nsWindow, sel_registerName("setCollectionBehavior:"), behavior);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Could not turn off full screen: {ex.Message}");
        }
    }

    private delegate void ZoomClickedHandler(IntPtr self, IntPtr selector, IntPtr sender);

    // Kept alive for as long as the green button points at it.
    private static ZoomClickedHandler? s_zoomClickedHandler;
    private static IntPtr s_zoomTarget;
    private static Action? s_zoom;

    /// <summary>
    /// Makes the green window button run <paramref name="zoom"/> instead of macOS's own zoom, which would only
    /// stretch the fixed-height docked bar (and slide it to the bottom of the screen on the way).
    /// </summary>
    public static void RedirectZoomButton(Window window, Action zoom)
    {
        if (NSWindow(window) is not IntPtr nsWindow)
            return;

        try
        {
            const nint NSWindowZoomButton = 2;
            IntPtr button = SendNInt(nsWindow, sel_registerName("standardWindowButton:"), NSWindowZoomButton);
            if (button == IntPtr.Zero)
                return;

            s_zoom = zoom;
            if (s_zoomTarget == IntPtr.Zero)
            {
                IntPtr cls = objc_allocateClassPair(objc_getClass("NSObject"), "SkuaZoomButtonTarget", 0);
                s_zoomClickedHandler = (_, _, _) => s_zoom?.Invoke();
                class_addMethod(cls, sel_registerName("skuaZoom:"),
                    Marshal.GetFunctionPointerForDelegate(s_zoomClickedHandler), "v@:@");
                objc_registerClassPair(cls);
                s_zoomTarget = Send(Send(cls, sel_registerName("alloc")), sel_registerName("init"));
            }

            SendPtr(button, sel_registerName("setTarget:"), s_zoomTarget);
            SendPtr(button, sel_registerName("setAction:"), sel_registerName("skuaZoom:"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Could not redirect the zoom button: {ex.Message}");
        }
    }

    private static IntPtr? NSWindow(Window window)
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var handle = window.TryGetPlatformHandle();
        return handle is null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "NSWindow"
            ? null
            : handle.Handle;
    }
}
#endif
