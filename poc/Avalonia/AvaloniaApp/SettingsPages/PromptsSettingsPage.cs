using Avalonia.Controls;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public class PromptsSettingsPage : SettingsPageViewModel
{
    private readonly AppSettings _settings;

    public PromptsSettingsPage(AppSettings settings) : base("Prompts", "\uf0eb", new PromptsSettingsPageView(settings))
    {
        _settings = settings;
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        // Validation: ensure each prompt has title and prompt text
        foreach (var prompt in _settings.Prompts)
        {
            if (string.IsNullOrWhiteSpace(prompt.Title))
            {
                errorMessage = "All prompts must have a title.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(prompt.PromptText))
            {
                errorMessage = "All prompts must have prompt text.";
                return false;
            }
        }
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
