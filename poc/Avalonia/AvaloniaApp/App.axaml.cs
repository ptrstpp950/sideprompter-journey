using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaApp.Settings;
using System;

namespace AvaloniaApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsService.Load();
            if (!settings.SetupCompleted)
            {
                var wizard = new SetupWizard(settings, false);
                wizard.Closed += (_, _) =>
                {
                    var latest = SettingsService.Load();
                    latest.SetupCompleted = true;
                    SettingsService.Save(latest);
                    desktop.MainWindow = new MainWindow(latest);
                    desktop.MainWindow.Show();
                };
                wizard.Show();
            }
            else
            {
                desktop.MainWindow = new MainWindow(settings);
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
}