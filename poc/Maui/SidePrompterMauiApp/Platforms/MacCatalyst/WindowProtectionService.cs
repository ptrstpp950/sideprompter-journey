using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using ObjCRuntime;
using UIKit;
#if MACOS
using AppKit;
#endif

namespace SidePrompterMauiApp.Services
{
    public partial class WindowProtectionService
    {
        public async Task<(bool Success, string Message)> SetWindowProtectionAsync(bool enable, CancellationToken ct = default)
        {
#if MACOS
            // Real AppKit implementation (macOS desktop target)
            try
            {
                return await MainThread.InvokeOnMainThreadAsync<(bool, string)>(() =>
                {
                    var nsWindow = NSApplication.SharedApplication.Windows
                        .FirstOrDefault(w => w.IsKeyWindow);

                    if (nsWindow is null)
                        return (false, "Failed to locate key NSWindow.");

                    int desired = enable ? 0 : 1; // 0=None,1=ReadOnly
                    int current = -1;
                    try
                    {
                        if (nsWindow.RespondsToSelector(new Selector("sharingType")))
                        {
                            var curObj = nsWindow.ValueForKey((NSString)"sharingType") as NSNumber;
                            current = curObj?.Int32Value ?? -1;
                        }
                    }
                    catch { }

                    if (current == desired)
                        return (true, $"sharingType already {(enable ? "None(0)" : "ReadOnly(1)")}.");

                    if (nsWindow.RespondsToSelector(new Selector("setSharingType:")))
                    {
                        try
                        {
                            nsWindow.SetValueForKey(NSNumber.FromInt32(desired), (NSString)"sharingType");
                            return (true, $"sharingType set to {(enable ? "None(0)" : "ReadOnly(1)")}.");
                        }
                        catch (Exception setEx)
                        {
                            return (false, $"Failed to set sharingType: {setEx.Message}");
                        }
                    }

                    return (false, "sharingType not supported on this macOS version.");
                });
            }
            catch (Exception ex)
            {
                return (false, $"Exception (macOS): {ex.Message}");
            }
#elif MACCATALYST
            // Catalyst: NSApplication / NSWindow (AppKit) not available publicly.
            return (false, "Window sharingType cannot be changed under Mac Catalyst (NSApplication not exposed).");
#else
            return (false, "Window protection not supported on this platform.");
#endif
        }
    }
}
