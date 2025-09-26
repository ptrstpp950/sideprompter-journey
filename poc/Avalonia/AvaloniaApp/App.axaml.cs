using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaApp.Settings;
using AvaloniaApp.Services.Notification;
using AvaloniaApp.Services.Update;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp;

public partial class App : Application
{
    public static NotificationService Notifications { get; private set; } = null!;
    private static IAppUpdateService? _updateService;
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
                    
                    // Start background update checking after setup is complete
                    StartBackgroundUpdateCheck();
                };
                wizard.Show();
            }
            else
            {
                mainWindow = new MainWindow(settings, Log.Logger);
                desktop.MainWindow = mainWindow;
                Notifications = new NotificationService(mainWindow);
                
                // Start background update checking
                StartBackgroundUpdateCheck();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        //TODO: dialog box with error details
        Console.WriteLine($"UI thread unhandled exception: {e.Exception}");
        e.Handled = false;
    }

    private void StartBackgroundUpdateCheck()
    {
        if (_updateService != null) return; // Already started
        
        try
        {
            _updateService = new AppUpdateService(Log.Logger);
            
            // Start background update checking
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)); // Wait 30 seconds after startup
                    
                    Log.Information("Starting background update check");
                    var updateInfo = await _updateService.CheckForUpdatesAsync();
                    
                    if (updateInfo != null)
                    {
                        Log.Information("Update available: {Version}. Notifying user.", updateInfo.Version);
                        
                        // Log the update availability (for now, we can improve the notification system later)
                        Log.Information("Update notification: Version {Version} is available for download", updateInfo.Version);
                        
                        // For now, we'll automatically download and apply the update
                        // In a production app, you might want to ask user permission first
                        try
                        {
                            Log.Information("Auto-downloading and applying update: {Version}", updateInfo.Version);
                            await _updateService.DownloadAndApplyUpdatesAsync(updateInfo);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to auto-apply update");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background update check failed");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize update service");
        }
    }
}