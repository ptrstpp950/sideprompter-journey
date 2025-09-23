using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Settings;
using Avalonia.Interactivity;
using Avalonia.Input;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaloniaApp;

public partial class PromptsSettingsPageView : UserControl
{
    private AppSettings? _settings;

    public PromptsSettingsPageView()
    {
        InitializeComponent();
    }

    public PromptsSettingsPageView(AppSettings settings)
    {
        _settings = settings;
        EnsureDefaultPrompts();
        DataContext = new PromptsSettingsViewModel(_settings);
        InitializeComponent();
    }

    private PromptsSettingsViewModel ViewModel => (PromptsSettingsViewModel)DataContext!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        var newPrompt = new Prompt { Title = "New Prompt", PromptText = "", Icon = HeroIconsAvalonia.Enums.IconType.DocumentText };
        ViewModel.Settings.Prompts.Add(newPrompt);
        ViewModel.SelectedPrompt = newPrompt;
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Prompt prompt) return;

        ViewModel.Settings.Prompts.Remove(prompt);
        if (ViewModel.SelectedPrompt == prompt)
        {
            ViewModel.SelectedPrompt = null;
        }
    }

    private void MoveUpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Prompt prompt) return;
        var index = ViewModel.Settings.Prompts.IndexOf(prompt);
        if (index > 0)
        {
            ViewModel.Settings.Prompts.Move(index, index - 1);
        }
    }

    private void MoveDownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Prompt prompt) return;
        var index = ViewModel.Settings.Prompts.IndexOf(prompt);
        if (index < ViewModel.Settings.Prompts.Count - 1)
        {
            ViewModel.Settings.Prompts.Move(index, index + 1);
        }
    }

    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Prompt prompt)
        {
            ViewModel.SelectedPrompt = prompt;
        }
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPrompt = null;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPrompt = null;
    }

    private void EnsureDefaultPrompts()
    {
        if (_settings == null || _settings.Prompts.Count > 0) return;

    _settings.Prompts.Add(new Prompt
    {
        Title = "Meeting Summary",
        Icon = HeroIconsAvalonia.Enums.IconType.DocumentText,
        PromptText = @"You are a meeting summarization assistant.

**Task:**
Produce a concise factual summary (2-4 sentences) of the current conversation using ONLY the provided context.
Do not repeat or rephrase any previous responses you gave in this session.
If the necessary information is not present in the context, state ""I don't have that information.""
If you interfere mark it with <interference>. If you use external knowledge mark it with <external>"
    });

    _settings.Prompts.Add(new Prompt
    {
        Title = "Suggested Follow-Up",
        Icon = HeroIconsAvalonia.Enums.IconType.ArrowPath,
        PromptText = @"You are a follow-up question generator.

**Task:**
Based on the provided conversation context, propose one concise, strategic follow-up question (you may optionally provide one brief alternative).
Ground your suggestion strictly in the available context and do not repeat any responses you previously generated.
If context is insufficient, state ""I don't have that information."""
    });

    
        _settings.Prompts.Add(new Prompt
        {
            Title = "Key Insight",
            Icon = HeroIconsAvalonia.Enums.IconType.LightBulb,
            PromptText = @"You are a Key Insight analyst.

    **Task:**
    From the provided conversation context, extract a single, high-impact insight (1-2 sentences) that is not immediately obvious. Then list 1-2 brief bullets of supporting evidence taken directly from the transcript, and finish with an optional one-line recommended action.
    Use only information from the provided context. If no non-obvious insight is present, return 'No non-obvious insight identified.' Do not repeat any responses you previously produced. Your job is to listen for decisions, tasks, and next steps, and to organize them into a clear summary."
        });
    _settings.Prompts.Add(new Prompt
    {
        Title = "Suggested Reply",
        Icon = HeroIconsAvalonia.Enums.IconType.ChatBubbleLeftRight,
        PromptText = @"You are a reply composer.
**Task:**
Given a selected speaker turn or question from the conversation, compose a short (1-2 sentence) professional reply the user can speak or paste.
Keep the reply concise and accurate using only the provided context.
Do not repeat any responses you previously produced.
If the information needed to craft a reply is missing, state ""I don't have that information."""
    });

    _settings.Prompts.Add(new Prompt
    {
        Title = "Action Summary",
        Icon = HeroIconsAvalonia.Enums.IconType.ListBullet,
        PromptText = @"You are an ""Action Item Generator"" AI. Your job is to listen for decisions, tasks, and next steps, and to organize them into a clear summary.

**Task:**
Based on the entire conversation, generate a summary of key outcomes. The summary must include:
1.  **Decisions Made:** A bulleted list of final decisions.
2.  **Action Items:** A list of tasks, with the assigned owner if mentioned.
3.  **Open Questions:** Any critical questions that remain unresolved.

If no items are identified for a category, state ""None."""
    });
    }
}
public class PromptsSettingsViewModel : INotifyPropertyChanged
{
    public AppSettings Settings { get; }

    // All possible icons
    public ObservableCollection<HeroIconsAvalonia.Enums.IconType> IconOptions { get; }

    // Filtered list used for autocomplete
    public ObservableCollection<HeroIconsAvalonia.Enums.IconType> FilteredIconOptions { get; }

    private Prompt? _selectedPrompt;
    public Prompt? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            _selectedPrompt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPrompt)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PromptsSettingsViewModel(AppSettings settings)
    {
        Settings = settings;
        var values = Enum.GetValues(typeof(HeroIconsAvalonia.Enums.IconType)).Cast<HeroIconsAvalonia.Enums.IconType>().ToList();
        IconOptions = new ObservableCollection<HeroIconsAvalonia.Enums.IconType>(values);
        FilteredIconOptions = new ObservableCollection<HeroIconsAvalonia.Enums.IconType>(values);
    }

    public void FilterIcons(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (FilteredIconOptions.Count != IconOptions.Count)
            {
                FilteredIconOptions.Clear();
                foreach (var v in IconOptions) FilteredIconOptions.Add(v);
            }
            return;
        }

        var lower = text.ToLowerInvariant();
        var matches = IconOptions.Where(i => i.ToString().ToLowerInvariant().Contains(lower)).ToList();
        FilteredIconOptions.Clear();
        foreach (var m in matches) FilteredIconOptions.Add(m);
    }
}

// ComboBox KeyUp handler to support autocomplete filtering
partial class PromptsSettingsPageView
{
    private void IconComboBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is ComboBox cb && DataContext is PromptsSettingsViewModel vm)
        {
            // Use the editable text to filter. Editable ComboBox contains a TextBox named PART_EditableTextBox
            string? text = null;
            try
            {
                if (cb.FindControl<TextBox>("PART_EditableTextBox") is TextBox tb)
                {
                    text = tb.Text;
                }
            }
            catch
            {
                // ignore
            }
            if (text == null && cb.SelectedItem != null)
            {
                text = cb.SelectedItem.ToString();
            }
            vm.FilterIcons(text);
            // open dropdown to show suggestions
            cb.IsDropDownOpen = true;
        }
    }

    private void IconFilterTextBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && DataContext is PromptsSettingsViewModel vm)
        {
            vm.FilterIcons(tb.Text);
        }
    }

}