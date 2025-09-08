using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#if MACOS
using Foundation;
#endif

namespace AvaloniaApp.Services
{
    public interface IMacOsPermissionsService
    {
        bool HasAccessibilityPermission();
        Task<bool> RequestAccessibilityPermission();
    }

#if MACOS
    public class MacOsPermissionsService : IMacOsPermissionsService
    {
        // Delegate for the callback handler
        public delegate void CGDisplayStreamFrameAvailableHandler(int status, ulong displayTime, IntPtr frameSurface, IntPtr updateRef);

        // For Accessibility
        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

        // For Screen Capture (Audio)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern uint CGMainDisplayID();

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGDisplayStreamCreateWithDispatchQueue(uint displayId, int outputWidth, int outputHeight, int pixelFormat, IntPtr properties, IntPtr queue, CGDisplayStreamFrameAvailableHandler handler);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern IntPtr dispatch_get_main_queue();

        public bool HasAccessibilityPermission()
        {
            return AXIsProcessTrustedWithOptions(IntPtr.Zero);
        }

        public Task<bool> RequestAccessibilityPermission()
        {
            var options = new NSDictionary(new NSString("AXTrustedCheckOptionPrompt"), new NSNumber(true));
            return Task.FromResult(AXIsProcessTrustedWithOptions(options.Handle));
        }

        // Dummy callback to satisfy the API
        private static void DummyHandler(int status, ulong displayTime, IntPtr frameSurface, IntPtr updateRef) { }
    }
#else
    // Provide a dummy implementation for other platforms.
    public class MacOsPermissionsService : IMacOsPermissionsService
    {
        public bool HasAccessibilityPermission() => true;
        public Task<bool> RequestAccessibilityPermission() => Task.FromResult(true);
    }
#endif
}
