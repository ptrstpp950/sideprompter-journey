#if WINDOWS
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Services.WindowTextExtraction
{
    public class WindowTextExtractionServiceWin : IWindowTextExtractionService
    {
        public Task<string> GetActiveWindowTextAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var automation = new UIA3Automation();
                    var window = automation.FromHandle(Win32.GetForegroundWindow());
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (window != null)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"[Window: {window.Name}, Process: {window.Properties.ProcessId.ValueOrDefault}]");
                        AppendText(window, sb, 0);
                        return sb.ToString();
                    }
                }
                catch (Exception ex)
                {
                    // Log or handle the exception
                    Console.WriteLine($"Error extracting window text: {ex.Message}");
                }
                return string.Empty;
            });
        }

        public string GetActiveWindowTitle()
        {
            try
            {
                using var automation = new UIA3Automation();
                var window = automation.FromHandle(Win32.GetForegroundWindow());
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                return window?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                // Log or handle the exception
                Console.WriteLine($"Error getting window title: {ex.Message}");
                return string.Empty;
            }
        }

        private void AppendText(AutomationElement element, StringBuilder sb, int indent)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (element == null) return;

            try
            {
                var indentStr = new string(' ', indent * 2);

                // Extract basic properties
                var name = element.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    sb.AppendLine($"{indentStr}Name: {name}");
                }

                // Extract text using ValuePattern
                if (element.Patterns.Value.IsSupported)
                {
                    var value = element.Patterns.Value.Pattern.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        sb.AppendLine($"{indentStr}Value: {value}");
                    }
                }

                // Extract text using TextPattern (for documents, web pages, etc.)
                if (element.Patterns.Text.IsSupported)
                {
                    var text = element.Patterns.Text.Pattern.DocumentRange.GetText(-1).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine($"{indentStr}Text: {text}");
                    }
                }

                // Recursively process children
                foreach (var child in element.FindAllChildren())
                {
                    AppendText(child, sb, indent + 1);
                }
            }
            catch (Exception ex)
            {
                // Ignore elements that can't be accessed
                Console.WriteLine($"Could not access element: {ex.Message}");
            }
        }
    }

    internal static class Win32
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
#endif