using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;

namespace AvaloniaApp.Services.Notification;

public interface INotificationService
{
    NotificationWindow Show(string message, TimeSpan? autoClose = null);
}

public class NotificationService : INotificationService
{
    private readonly List<NotificationWindow> _windows = new();
    private readonly object _sync = new();
    private const int StartOffsetX = 10;
    private const int StartOffsetY = 10;
    private const int Gap = 8;

    public NotificationWindow Show(string message, TimeSpan? autoClose = null)
    {
        NotificationWindow window = null!;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            window = new NotificationWindow(message, autoClose)
            {
                Topmost = true,
                ShowInTaskbar = false
            };
            window.Closed += (_, _) =>
            {
                lock (_sync)
                {
                    _windows.Remove(window);
                }
                Reposition();
            };
            lock (_sync)
            {
                _windows.Add(window);
            }
            window.Opened += (_, _) => Reposition();
            window.Show();
        });
        return window;
    }

    private void Reposition()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var screen = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk
                ? desk.MainWindow?.Screens?.Primary ?? desk.MainWindow?.Screens?.All?.FirstOrDefault()
                : null;
            var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            int currentY = area.Y + StartOffsetY;
            List<NotificationWindow> snapshot;
            lock (_sync)
            {
                snapshot = _windows.ToList();
            }
            foreach (var w in snapshot)
            {
                if (!w.IsVisible) continue;
                var h = (int)w.Bounds.Height;
                w.Position = new PixelPoint(area.X + StartOffsetX, currentY);
                currentY += h + Gap;
            }
        });
    }
}
