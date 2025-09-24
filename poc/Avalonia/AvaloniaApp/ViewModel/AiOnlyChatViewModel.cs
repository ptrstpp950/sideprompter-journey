using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using AvaloniaApp.Settings;

namespace AvaloniaApp.ViewModel;

/// <summary>
/// Lightweight view-model that exposes only AI assistant messages from an existing ChatViewModel.
/// It subscribes to MessageAdded and copies existing messages that are authored by the AI assistant.
/// </summary>
public class AiOnlyChatViewModel
{
    private readonly ChatViewModel _source;
    public ObservableCollection<AiChatItem> Items { get; } = new ObservableCollection<AiChatItem>();

    public AiOnlyChatViewModel(ChatViewModel source)
    {
        _source = source;
        // copy existing AI messages
        foreach (var m in _source.Messages.Where(x => x.Author == MessageAuthor.AiAssistant))
        {
            Items.Add(CreateItem(m));
        }
        // subscribe to future AI messages
        _source.MessageAdded += Source_MessageAdded;
    }

    private AiChatItem CreateItem(ChatMessage m)
    {
        var title = ResolvePromptTitle(m.PromptId, _source.Settings);
        return new AiChatItem
        {
            Text = m.Text ?? string.Empty,
            PromptTitle = title ?? "AI",
            Timestamp = m.Timestamp
        };
    }

    private void Source_MessageAdded(object? sender, ChatMessage e)
    {
        if (e.Author != MessageAuthor.AiAssistant) return;
        // ensure we update on UI thread
        Dispatcher.UIThread.InvokeAsync(() => Items.Add(CreateItem(e)));
    }

    private string? ResolvePromptTitle(string? promptId, AppSettings settings)
    {
        if (string.IsNullOrEmpty(promptId) || settings == null) return null;
        var prompt = settings.Prompts.FirstOrDefault(p => p.GetHash() == promptId);
        return prompt?.Title ?? promptId;
    }

    public class AiChatItem
    {
        public string PromptTitle { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public System.DateTime Timestamp { get; set; }
    }
}
