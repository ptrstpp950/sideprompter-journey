using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaApp.Settings;
using AvaloniaApp.Services.Notification;
using Serilog;
using System;
using System.Threading.Tasks;
using AvaloniaApp.Services.Update;

namespace AvaloniaApp;

public partial class App : Application
{
    public static NotificationService Notifications { get; private set; } = null!;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow;
            var settings = SettingsService.Load();
            // settings.SetupCompleted = false; // TEMP: force re-setup for testing
            if (settings.AppSettingsVersion != AppSettings.CurrentVersion)
            {
                settings.Prompts.Clear(); // clear out old prompts on breaking change
                settings.SetupCompleted = false; // force re-setup on breaking change
                settings.AppSettingsVersion = AppSettings.CurrentVersion;
            }
            if (!settings.SetupCompleted)
            {
                var wizard = new SetupWizard(settings, false);
                wizard.Closed += (_, _) =>
                {
                    var latest = SettingsService.Load();
                    latest.SetupCompleted = true;
                    SettingsService.Save(latest);
                    mainWindow = new MainWindow(latest, Log.Logger);
                    Notifications = new NotificationService(mainWindow);
                    desktop.MainWindow = mainWindow;
                    desktop.MainWindow.Show();
                };
                wizard.Show();
            }
            else
            {
                mainWindow = new MainWindow(settings, Log.Logger);
                desktop.MainWindow = mainWindow;
                Notifications = new NotificationService(mainWindow);
            }



            // TODO: Re-enable background update check once Velopack API usage is confirmed.
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        //TODO: dialog box with error details
        Console.WriteLine($"UI thread unhandled exception: {e.Exception}");
        e.Handled = false;
    }
}