using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

namespace AvaloniaApp
{
    public class WindowTextExtractionService : IWindowTextExtractionService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, int wParam, StringBuilder lParam);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const int GWL_STYLE = -16;
        private const int ES_READONLY = 0x0800;
        private const int WS_VISIBLE = 0x10000000;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        #endregion

        /// <summary>
        /// Gets the title of the current active window in the system
        /// </summary>
        /// <returns>The title of the active window</returns>
        public string GetActiveWindowTitle()
        {
            if (!OperatingSystem.IsWindows())
                return "Not supported on this platform";

            IntPtr handle = GetForegroundWindow();
            StringBuilder title = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            return title.ToString();
        }

        /// <summary>
        /// Gets the text from the current active window in the system
        /// </summary>
        /// <returns>The text content of the active window, or empty string if no text could be extracted</returns>
        public async Task<string> GetActiveWindowTextAsync()
        {
            if (!OperatingSystem.IsWindows())
                return "Text extraction not supported on this platform";

            return await Task.Run(() => {
                StringBuilder result = new StringBuilder();

                try
                {
                    IntPtr foregroundWindow = GetForegroundWindow();
                    if (foregroundWindow == IntPtr.Zero)
                        return string.Empty;

                    // Get process ID for the foreground window
                    GetWindowThreadProcessId(foregroundWindow, out int processId);
                    string processName = GetProcessNameById(processId);
                    result.AppendLine($"[Window: {GetActiveWindowTitle()}, Process: {processName}]");

                    // Extract text from the foreground window and its child windows
                    StringBuilder windowText = new StringBuilder(4096);
                    
                    // Try to get text directly from the window first
                    int length = SendMessage(foregroundWindow, WM_GETTEXT, windowText.Capacity, windowText);
                    if (length > 0)
                    {
                        result.AppendLine(windowText.ToString());
                    }
                    
                    // Find all child windows and extract text from them
                    List<string> childTexts = new List<string>();
                    EnumChildWindows(foregroundWindow, (childHwnd, lParam) => {
                        // Get class name to identify edit controls, etc.
                        StringBuilder className = new StringBuilder(100);
                        GetClassName(childHwnd, className, className.Capacity);
                        string classNameStr = className.ToString().ToLower();

                        // Check if the window is visible
                        if (IsWindowVisible(childHwnd))
                        {
                            // Get text length
                            int textLen = SendMessage(childHwnd, WM_GETTEXTLENGTH, 0, IntPtr.Zero);
                            //if (textLen > 0)
                            {
                                // Common text control classes
                                /*if (classNameStr.Contains("edit") ||
                                    classNameStr.Contains("text") ||
                                    classNameStr.Contains("rich") ||
                                    classNameStr.Contains("scintilla") ||   // For VS Code, Notepad++, etc.
                                    classNameStr == "monaco")               // For web-based editors
                                */
                                {
                                    StringBuilder text = new StringBuilder(textLen + 1);
                                    SendMessage(childHwnd, WM_GETTEXT, text.Capacity, text);
                                    string textContent = text.ToString().Trim();
                                    if (!string.IsNullOrEmpty(textContent))
                                    {
                                        childTexts.Add(textContent);
                                    }
                                }
                            }
                        }
                        return true; // Continue enumeration
                    }, IntPtr.Zero);

                    // Add child window texts
                    foreach (string text in childTexts)
                    {
                        result.AppendLine(text);
                    }

                    // Special handling for specific applications
                    if (processName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||    // Visual Studio
                        processName.Equals("code", StringComparison.OrdinalIgnoreCase) ||      // VS Code
                        processName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
                        processName.Equals("notepad++", StringComparison.OrdinalIgnoreCase))
                    {
                        // These applications may need specialized approaches for certain UI frameworks
                        // Additional custom handling could be added here
                    }
                }
                catch (Exception ex)
                {
                    return $"Error extracting text: {ex.Message}";
                }

                return result.ToString();
            });
        }

        private string GetProcessNameById(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
