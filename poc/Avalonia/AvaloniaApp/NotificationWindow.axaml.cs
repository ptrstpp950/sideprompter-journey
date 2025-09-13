using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp;

public partial class NotificationWindow : Window
{
    private TimeSpan? _autoClose;

    public NotificationWindow(): this("Empty notification")
    {
    }
    
    public NotificationWindow(string message, TimeSpan? autoClose = null)
    {
        InitializeComponent();
        _autoClose = autoClose;
        var tb = this.FindControl<TextBlock>("MessageText");
        if (tb != null) tb.Text = message;
        Opened += NotificationWindow_Opened;
    }

    private void NotificationWindow_Opened(object? sender, EventArgs e)
    {
        // trigger fade-in by setting final opacity
        this.Opacity = 1;

        if (_autoClose != null)
        {
            var delay = _autoClose.Value;
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!IsClosed)
                            Close();
                    });
                }
                catch { /* ignore */ }
            });
        }
    }

    private bool IsClosed => !IsVisible;

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
