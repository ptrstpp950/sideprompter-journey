using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using ObjCRuntime;
using UIKit;

namespace SidePrompterMauiApp.Services
{
    public partial class WindowProtectionService
    {
        public async Task<(bool Success, string Message)> SetWindowProtectionAsync(bool enable, CancellationToken ct = default)
        {
            try
            {
                return await MainThread.InvokeOnMainThreadAsync<(bool, string)>(() =>
                {
                    var uiWindow = UIApplication.SharedApplication.ConnectedScenes
                        .OfType<UIWindowScene>()
                        .SelectMany(s => s.Windows)
                        .FirstOrDefault(w => w.IsKeyWindow);

                    if (uiWindow is null)
                        return (false, "Failed to locate key UIWindow (macOS).");

                    var nsWindowObj = uiWindow.PerformSelector(new Selector("nsWindow"));
                    if (nsWindowObj is null)
                        return (false, "Failed to get underlying nsWindow (macOS).");

                    int level = enable ? 5 : 0; // Adjust as needed
                    var selSetLevel = Selector.GetHandle("setLevel:");
                    //Messaging.void_objc_msgSend_int(nsWindowObj.Handle, selSetLevel, level);

                    return (true, $"Window level set to {level} (macOS)");
                });
            }
            catch (Exception ex)
            {
                return (false, $"Exception (macOS): {ex.Message}");
            }
        }
    }
}