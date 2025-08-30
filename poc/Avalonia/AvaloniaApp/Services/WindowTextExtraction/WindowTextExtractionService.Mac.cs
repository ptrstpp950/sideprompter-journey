// macOS implementation of IWindowTextExtractionService using the Accessibility (AX) API.
// This file is conditionally compiled only on macOS targets. Adjust the preprocessor symbols
// (e.g. MACOS, OSX, MACCATALYST) to match your project configuration.

#if MACOS || OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AppKit;
using AvaloniaApp.Services.WindowTextExtraction;
using Foundation;     // Referenced for NSWorkspace / bundle metadata (optional fallback)

namespace AvaloniaApp.Services.WindowTextExtraction
{
    /// <summary>
    /// macOS Accessibility-based implementation. Requires the app to have the "Accessibility" permission
    /// (System Settings > Privacy & Security > Accessibility). Returns empty strings if permission is missing.
    /// </summary>
    public sealed class WindowTextExtractionServiceMac : IWindowTextExtractionService
    {
        // AX attribute constants
        private const string kAXFocusedUIElementAttribute = "AXFocusedUIElement";
        private const string kAXTitleAttribute = "AXTitle";
        private const string kAXValueAttribute = "AXValue";
        private const string kAXRoleAttribute = "AXRole";
        private const string kAXChildrenAttribute = "AXChildren";
        private const string kAXWindowAttribute = "AXWindow";

        // AX error codes (subset)
        private const int kAXErrorSuccess = 0;
        private const int kAXErrorAPIDisabled = -25211; // Accessibility disabled

        public Task<string> GetActiveWindowTextAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var systemWide = AXUIElementCreateSystemWide();
                    if (systemWide == IntPtr.Zero)
                        return string.Empty;

                    var focused = GetAttribute(systemWide, kAXFocusedUIElementAttribute);
                    if (focused == IntPtr.Zero)
                        return string.Empty;

                    // Try to resolve containing window for contextual header
                    var window = GetAttribute(focused, kAXWindowAttribute);
                    var windowTitle = CFStringToString(GetAttribute(window == IntPtr.Zero ? focused : window, kAXTitleAttribute)) ?? GetActiveWindowTitle();

                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(windowTitle))
                        sb.AppendLine($"[Window: {windowTitle}]");

                    // BFS / limited traversal to gather text attributes
                    TraverseCollectText(focused, sb, maxDepth: 6, maxNodes: 1000);
                    return sb.ToString();
                }
                catch (AccessibilityDisabledException)
                {
                    return string.Empty; // Caller can interpret empty string as no access / no data
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            });
        }

        public string GetActiveWindowTitle()
        {
            try
            {
                var systemWide = AXUIElementCreateSystemWide();
                if (systemWide == IntPtr.Zero) return string.Empty;

                var focused = GetAttribute(systemWide, kAXFocusedUIElementAttribute);
                if (focused == IntPtr.Zero) return string.Empty;

                // First attempt: title on focused element or its window
                var window = GetAttribute(focused, kAXWindowAttribute);
                var titlePtr = GetAttribute(focused, kAXTitleAttribute);
                if (titlePtr == IntPtr.Zero && window != IntPtr.Zero)
                    titlePtr = GetAttribute(window, kAXTitleAttribute);

                var title = CFStringToString(titlePtr);
                if (!string.IsNullOrWhiteSpace(title))
                    return title;

                // Fallback: front most application name
                try
                {
                    var ws = NSWorkspace.SharedWorkspace;
                    return ws?.FrontmostApplication?.LocalizedName ?? string.Empty;
                }
                catch { return string.Empty; }
            }
            catch (AccessibilityDisabledException)
            {
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void TraverseCollectText(IntPtr element, StringBuilder sb, int maxDepth, int maxNodes)
        {
            if (element == IntPtr.Zero) return;

            var queue = new Queue<(IntPtr node, int depth)>();
            var visited = new HashSet<IntPtr>();
            queue.Enqueue((element, 0));

            int nodes = 0;
            while (queue.Count > 0 && nodes < maxNodes)
            {
                var (node, depth) = queue.Dequeue();
                if (node == IntPtr.Zero || depth > maxDepth) continue;
                if (!visited.Add(node)) continue;
                nodes++;

                var indent = new string(' ', depth * 2);
                var role = CFStringToString(GetAttribute(node, kAXRoleAttribute));
                var title = CFStringToString(GetAttribute(node, kAXTitleAttribute));
                var value = CFStringToString(GetAttribute(node, kAXValueAttribute));

                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(value))
                {
                    sb.Append(indent);
                    if (!string.IsNullOrWhiteSpace(role)) sb.Append($"{role}: ");
                    if (!string.IsNullOrWhiteSpace(title)) sb.Append($"Title=\"{title}\" ");
                    if (!string.IsNullOrWhiteSpace(value)) sb.Append($"Value=\"{value}\"");
                    sb.AppendLine();
                }

                // Children
                var childrenArray = GetAttribute(node, kAXChildrenAttribute);
                if (childrenArray != IntPtr.Zero)
                {
                    var children = NSArrayToList(childrenArray);
                    foreach (var child in children)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }

        #region Accessibility Helpers

        private IntPtr GetAttribute(IntPtr element, string attribute)
        {
            if (element == IntPtr.Zero) return IntPtr.Zero;
            var attr = CreateCFString(attribute);
            try
            {
                var err = AXUIElementCopyAttributeValue(element, attr, out var valuePtr);
                if (err == kAXErrorAPIDisabled)
                    throw new AccessibilityDisabledException();
                if (err != kAXErrorSuccess)
                    return IntPtr.Zero;
                return valuePtr;
            }
            finally
            {
                if (attr != IntPtr.Zero) CFRelease(attr);
            }
        }

        // ReSharper disable once InconsistentNaming
        private static string? CFStringToString(IntPtr cfString)
        {
            if (cfString == IntPtr.Zero) return null;
            var length = CFStringGetLength(cfString);
            if (length == 0) return string.Empty;
            var buffer = new char[length];
            var range = new CFRange { Location = 0, Length = length };
            CFStringGetCharacters(cfString, range, buffer);
            return new string(buffer);
        }
        
        // ReSharper disable once InconsistentNaming
        private static List<IntPtr> NSArrayToList(IntPtr nsArray)
        {
            var list = new List<IntPtr>();
            if (nsArray == IntPtr.Zero) return list;
            // NSArray layout / access via Objective-C runtime.
            // Simplify via Foundation API if available.
            try
            {
                var array = ObjCRuntime.Runtime.GetNSObject<NSArray>(nsArray);
                if (array != null)
                {
                    for (nuint i = 0; i < array.Count; i++)
                    {
                        var obj = array.ValueAt(i);
                        if (obj != IntPtr.Zero) list.Add(obj);
                    }
                }
            }
            catch
            {
                // ignored
            }

            return list;
        }

        // ReSharper disable once InconsistentNaming
        private static IntPtr CreateCFString(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var chars = value.ToCharArray();
            return CFStringCreateWithCharacters(IntPtr.Zero, chars, chars.Length);
        }

        #endregion

        #region PInvoke

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern IntPtr AXUIElementCreateSystemWide();

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, char[] chars, nint numChars);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern nint CFStringGetLength(IntPtr handle);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFStringGetCharacters(IntPtr handle, CFRange range, [Out] char[] buffer);

        // ReSharper disable once InconsistentNaming
        [StructLayout(LayoutKind.Sequential)]
        private struct CFRange
        {
            public nint Location;
            public nint Length;
        }

        #endregion
    }

    internal sealed class AccessibilityDisabledException : Exception
    {
        public AccessibilityDisabledException() : base("macOS Accessibility permission not granted.") { }
    }
}
#endif
