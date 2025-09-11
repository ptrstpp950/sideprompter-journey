using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using AvaloniaApp.Settings;
using OpenAI.VectorStores;

namespace AvaloniaApp.ViewModel;

public class ChatViewModel : INotifyPropertyChanged
{
    private ObservableCollection<ChatMessage> _messages = new();
    public ObservableCollection<ChatMessage> Messages
    {
        get => _messages;
        set
        {
            _messages = value;
            OnPropertyChanged();
        }
    }
    
    private ObservableCollection<ChatMessage> _logMessages = new();
    public ObservableCollection<ChatMessage> LogMessages
    {
        get => _logMessages;
        set
        {
            _logMessages = value;
            OnPropertyChanged();
        }
    }

    public AppSettings Settings { get; }

    private Prompt? _selectedPrompt;
    public Prompt? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            _selectedPrompt = value;
            OnPropertyChanged();
        }
    }

    public ChatViewModel()
    {
        Settings = SettingsService.Load();
        SelectedPrompt = Settings.Prompts.FirstOrDefault();
        /*AddMessage("me: Lorem ipsum dolor sit amet, consectetur adipiscing elit.", MessageAuthor.Me);
        AddMessage("other: Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.", MessageAuthor.Other);
        AddMessage("And AI response could be", MessageAuthor.AiAssistant);*/
    }

    public void AddMessage(string text, MessageAuthor author)
    {
        text = text.Trim();
        if(author == MessageAuthor.Me && Messages.Count>0 && Messages.Last().Text != null && Messages.Last().Text!.Contains(text))
            return;
        if (author == MessageAuthor.Other && Messages.Count > 0 && Messages.Last().Text != null)
        {
            // Remove duplicate
            var last = Messages.Last().Text ?? "";
            var overlap = GetOverlap(last, text);
            if (overlap.Length > 0)
            {
                text = text.Substring(overlap.Length).Trim();
            }
            if (string.IsNullOrWhiteSpace(text) || text == last)
                return;
        }
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lastMessage = Messages.LastOrDefault();
            if (lastMessage?.Author == author)
            {
                lastMessage.Text += " " + text;
                // This is a bit of a hack to force the UI to update
                var index = Messages.IndexOf(lastMessage);
                Messages[index] = lastMessage;
            }
            else
            {
                Messages.Add(new ChatMessage { Text = text, Author = author, Timestamp = DateTime.Now });
            }
        });
    }
    
    public void AddLogMessage(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogMessages.Add(new ChatMessage { Text = text, Author = MessageAuthor.Other, Timestamp = DateTime.Now });
        });
    }

    public void ClearMessages()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Messages.Clear();
        });
    }
    
    public void ClearLogMessages()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogMessages.Clear();
        });
    }

    private string GetOverlap(string s1, string s2)
    {
        for (int len = Math.Min(s1.Length, s2.Length); len > 0; len--)
        {
            string suffix = s1.Substring(s1.Length - len);
            if (s2.StartsWith(suffix))
            {
                return suffix;
            }
        }
        return "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
