using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

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

    public ChatViewModel()
    {
        AddMessage("me: Lorem ipsum dolor sit amet, consectetur adipiscing elit.", MessageAuthor.Me);
        AddMessage("other: Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.", MessageAuthor.Other);
        AddMessage("And AI response could be", MessageAuthor.AiAssistant);
    }

    public void AddMessage(string text, MessageAuthor author)
    {
        if(author == MessageAuthor.Me && Messages.Count>0 && Messages.Last().Text != null && Messages.Last().Text!.Contains(text))
            return;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
