using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsService.Load();
            if (!settings.SetupCompleted)
            {
                var wizard = new SetupWizard(settings);
                wizard.Closed += (_, _) =>
                {
                    var latest = SettingsService.Load();
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
}