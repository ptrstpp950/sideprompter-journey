using AvaloniaApp.Settings;

namespace AvaloniaApp;

public class LanguagesSettingsPage : SettingsPageViewModel
{
    private readonly AppSettings _settings;

    public LanguagesSettingsPage(AppSettings settings) : base("Languages", "🌐", new LanguagesSettingsPageView(settings))
    {
        _settings = settings;
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        if (_settings.Languages.Count == 0)
        {
            errorMessage = "Select at least one language.";
            return false;
        }
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
