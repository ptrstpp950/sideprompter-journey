using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using System.Windows.Input;
using AvaloniaApp.Settings;

namespace AvaloniaApp.ViewModel;

/// <summary>
/// Lightweight view-model that exposes only AI assistant messages from an existing ChatViewModel.
/// It subscribes to MessageAdded and copies existing messages that are authored by the AI assistant.
/// </summary>
public class AiOnlyChatViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly ChatViewModel _source;
    public ObservableCollection<AiChatItem> Items { get; } = new ObservableCollection<AiChatItem>();

    public AiOnlyChatViewModel(ChatViewModel source)
    {
        _source = source;
        // copy existing AI messages
        foreach (var m in _source.Messages.Where(x => x.Author == MessageAuthor.AiAssistant || x.Author == MessageAuthor.Context).OrderBy(x => x.Timestamp))
        {
            Items.Add(CreateItem(m));
        }
        // subscribe to future AI messages
        _source.MessageAdded += Source_MessageAdded;
    }

    private string? _questionText;
    public string? QuestionText
    {
        get => _questionText;
        set
        {
            if (_questionText == value) return;
            _questionText = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(QuestionText)));
            if (AskCommand is DelegateCommand dc) dc.RaiseCanExecuteChanged();
        }
    }

    private DelegateCommand? _askCommand;
    public ICommand AskCommand => _askCommand ?? (_askCommand = new DelegateCommand(_ => ExecuteAsk(), _ => !string.IsNullOrWhiteSpace(QuestionText)));

    private void ExecuteAsk()
    {
        var q = QuestionText?.Trim();
        if (string.IsNullOrWhiteSpace(q)) return;
        // Post as Context (same behavior as previous code)
        AddMessage(q, MessageAuthor.Context);
        QuestionText = string.Empty;
    }

    // Expose a small passthrough API so views bound to the AiOnlyChatViewModel
    // can post messages back to the underlying ChatViewModel.
    public void AddMessage(string text, MessageAuthor author)
    {
        _source.AddMessage(text, author);
    }

    public bool IsAsking
    {
        get => _source.IsAsking;
        set => _source.IsAsking = value;
    }

    private AiChatItem CreateItem(ChatMessage m)
    {
        string? title;
        if(m.Author == MessageAuthor.Context)
            title = "Context";
        else
            title = ResolvePromptTitle(m.PromptId, _source.Settings);
        return new AiChatItem
        {
            Text = m.Text ?? string.Empty,
            PromptTitle = title ?? "Unknown",
            Timestamp = m.Timestamp
        };
    }

    private void Source_MessageAdded(object? sender, ChatMessage e)
    {
        if (e.Author != MessageAuthor.AiAssistant && e.Author != MessageAuthor.Context)
            return;
        // ensure we update on UI thread
        Dispatcher.UIThread.InvokeAsync(() => Items.Add(CreateItem(e)));
    }

    private string? ResolvePromptTitle(string? promptId, AppSettings settings)
    {
        if (string.IsNullOrEmpty(promptId) || settings == null) return null;
        var prompt = settings.Prompts.FirstOrDefault(p => p.GetHash() == promptId);
        return prompt?.Title ?? promptId;
    }

    public class AiChatItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public string PromptTitle { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public System.DateTime Timestamp { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
