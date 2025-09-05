using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Settings;
using Avalonia.Interactivity;
using System.ComponentModel;

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
        var newPrompt = new Prompt { Title = "New Prompt", PromptText = "" };
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
            Title = "Second Brain",
            PromptText = @"You are a ""Second Brain"" AI assistant. Your role is to provide immediate and accurate answers to questions that arise during a meeting.

**Task:**
Based on the user's query, provide a concise and factual answer using ONLY the information from the provided context. If the information is not available, state ""I don't have that information."" Do not infer or use external knowledge."
        });

        _settings.Prompts.Add(new Prompt
        {
            Title = "Intelligent Prompter",
            PromptText = @"You are an ""Intelligent Prompter"" AI. Your purpose is to help the user steer the conversation effectively by suggesting insightful questions.
**Task:**
Analyze the live transcription for ambiguities, assumptions, or unexplored topics. Generate a list of 2-3 concise, open-ended questions the user could ask to advance the meeting's objectives. Prioritize questions that are strategic and forward-looking."
        });

        _settings.Prompts.Add(new Prompt
        {
            Title = "Secret Advisor",
            PromptText = @"You are a ""Secret Advisor"" AI. Your function is to read between the lines and provide the user with non-obvious insights and connections based on the ongoing conversation and shared content.

**Task:**
Identify and present a key insight that is not immediately apparent from the conversation. This could be a potential risk, an unforeseen opportunity, a contradiction between what is being said and what is being shown, or a connection to a previous point. Present the insight as a brief, confidential memo."
        });

        _settings.Prompts.Add(new Prompt
        {
            Title = "Communication Coach",
            PromptText = @"You are a ""Communication Coach"" AI. Your goal is to provide constructive feedback on the user's speaking habits. Assume the user is ""Speaker 1"".

**Task:**
Analyze the user's (Speaker 1) language. Identify one specific area for improvement. Focus on clarity, conciseness, use of filler words, or question-asking effectiveness. Provide one concrete example and a brief, actionable suggestion for improvement. The tone should be supportive and private."
        });

        _settings.Prompts.Add(new Prompt
        {
            Title = "Action Item Generator",
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
    }
}