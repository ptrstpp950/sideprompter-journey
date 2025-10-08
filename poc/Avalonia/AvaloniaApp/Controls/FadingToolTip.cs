using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.EnableWindowPrivacy;

namespace AvaloniaApp.Controls
{
    // Simple ToolTip that fades in when opened and fades out when closed.
    public class FadingToolTip : ToolTip
    {
        private const int FadeMs = 160;

        public FadingToolTip()
        {
            // start invisible
            Opacity = 0;
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {

            EnableWindowPrivacyService.SetProtected(TopLevel.GetTopLevel(this)?.TryGetPlatformHandle(), true);
            //this.PopupHost?.BringToFront();
            _ = FadeToAsync(1, FadeMs);
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            // Try to fade out; if already detached this is a no-op
            _ = FadeToAsync(0, FadeMs);
        }

        private async Task FadeToAsync(double to, int ms)
        {
            try
            {
                var from = Opacity;
                if (Math.Abs(from - to) < 0.01)
                {
                    Opacity = to;
                    return;
                }

                var steps = Math.Max(1, ms / 16);
                for (int i = 1; i <= steps; i++)
                {
                    var t = i / (double)steps;
                    var value = from + (to - from) * t;
                    Dispatcher.UIThread.Post(() => Opacity = value);
                    await Task.Delay(ms / steps);
                }

                Dispatcher.UIThread.Post(() => Opacity = to);
            }
            catch
            {
                Dispatcher.UIThread.Post(() => Opacity = to);
            }
        }
    }
}
