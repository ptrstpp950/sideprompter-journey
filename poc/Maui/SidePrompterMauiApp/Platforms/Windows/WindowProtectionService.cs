using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using WinRT;

namespace SidePrompterMauiApp.Services
{
    public partial class WindowProtectionService
    {
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Requires Windows 10 2004+

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        public async Task<(bool Success, string Message)> SetWindowProtectionAsync(bool enable, CancellationToken ct = default)
        {
            try
            {
                return await MainThread.InvokeOnMainThreadAsync<(bool, string)>(() =>
                {
                    var mauiWindow = Application.Current?.Windows?.FirstOrDefault();
                    if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
                        return (false, "Failed to get platform Window (Windows).");

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                    if (hwnd == IntPtr.Zero)
                        return (false, "Failed to get HWND (Windows).");

                    uint affinity = enable ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                    bool ok = SetWindowDisplayAffinity(hwnd, affinity);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (false, $"SetWindowDisplayAffinity failed. GetLastError={err}");
                    }

                    string result = enable ? "WDA_EXCLUDEFROMCAPTURE" : "WDA_NONE";
                    return (true, result);
                });
            }
            catch (Exception ex)
            {
                return (false, $"Exception (Windows): {ex.Message}");
            }
        }
    }
}