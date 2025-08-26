using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace AvaloniaApp;

/// <summary>
/// Provides cross-platform helpers to toggle window privacy / capture exclusion.
/// macOS: uses NSWindow setSharingType: (0 None / hidden, 1 ReadOnly / visible).
/// Windows: uses SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE (0x11) / WDA_NONE (0).
/// Other platforms: no-op.
/// </summary>
public static class WindowPrivacy
{
    public static void SetProtected(Window window, bool protect)
    {
        if (window is null) return;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                SetProtectedMac(window, protect);
            }
            else if (OperatingSystem.IsWindows())
            {
                SetProtectedWindows(window, protect);
            }
            // Linux / others: not implemented (Wayland / X11 would need different APIs)
        }
        catch
        {
            // Swallow any interop exceptions to avoid crashing app.
        }
    }

    #region macOS
    // macOS sharing types
    private const nint NSWindowSharingNone = 0;       // hidden from window capture enumeration
    private const nint NSWindowSharingReadOnly = 1;   // default / visible
    private static void SetProtectedMac(Window window, bool protect)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;
        SetMacSharingType(handle.Handle, protect ? NSWindowSharingNone : NSWindowSharingReadOnly);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, nint arg);

    private static void SetMacSharingType(IntPtr nsWindow, nint sharingType)
    {
        var sel = sel_registerName("setSharingType:");
        objc_msgSend(nsWindow, sel, sharingType);
    }
    #endregion

    #region Windows
    // Display affinity constants
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 21H1+

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private static void SetProtectedWindows(Window window, bool protect)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;
        // Best-effort; ignore failures (older OS versions may not support EXCLUDEFROMCAPTURE)
        _ = SetWindowDisplayAffinity(handle.Handle, protect ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }
    #endregion
}
