using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;

namespace AvaloniaApp.Services.Notification;

public interface INotificationService
{
    void Show(string message, TimeSpan? autoClose = null);
    public void EnsureAiWindowVisible();
}

public class NotificationService : INotificationService
{
    private const int StartOffsetX = 10;
    private const int StartOffsetY = 10;
    private AiChatWindow? _aiWindow;
    private readonly object _aiSync = new();
    private readonly MainWindow _mainWindow;

    public void Show(string message, TimeSpan? autoClose = null)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureAiWindowVisible();

        });
    }

    public NotificationService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void EnsureAiWindowVisible()
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_aiSync)
            {
                if (_aiWindow != null && _aiWindow.IsVisible) return;

                // Try to find the main chat view-model from the application's main window DataContext
                var main = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk
                    ? desk.MainWindow
                    : null;
                if (main == null) return;

                var mainDc = main.DataContext as ViewModel.ChatViewModel;
                if (mainDc == null) return;

                var aiVm = new ViewModel.AiOnlyChatViewModel(mainDc);
                _aiWindow = new AiChatWindow(aiVm)
                {
                    Topmost = true,
                    ShowInTaskbar = false
                };

                // Dock to left: position at left working area edge and vertically center-ish near top
                var screen = main.Screens?.Primary ?? main.Screens?.All?.FirstOrDefault();
                var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
                _aiWindow.Position = new PixelPoint(area.X + StartOffsetX, area.Y + StartOffsetY);

                _mainWindow.AiChatWindow = _aiWindow;

                _aiWindow.Opened += (_, _) => { /* nothing for now */ };
                _aiWindow.Closed += (_, _) =>
                {
                    lock (_aiSync)
                    {
                        _aiWindow = null;
                    }
                };
                _aiWindow.Show();
            }
        });
    }
}
