using System;
using System.Threading.Tasks;
using AvaloniaApp.Settings;
using AvaloniaApp.ViewModel;

namespace AvaloniaApp.Services.Chat;

public class ChatSessionService
{
    private readonly IChatStorage _storage;
    private readonly AppSettings _settings;

    private Guid _currentSessionId = Guid.NewGuid();
    private DateTime _startedUtc = DateTime.UtcNow;
    private bool _headerWritten;

    public ChatSessionService(IChatStorage storage, AppSettings settings)
    {
        _storage = storage;
        _settings = settings;
    }

    public async Task EnsureHeaderAsync(ChatViewModel vm, string language)
    {
        if (_headerWritten) return;
        var header = new ChatSessionHeader
        {
            SessionId = _currentSessionId,
            StartedUtc = _startedUtc,
            Language = language,
            PromptId = vm.SelectedPrompt?.Title,
            PromptText = vm.SelectedPrompt?.PromptText,
            WhisperModel = _settings.WhisperModelType.ToString(),
            ChatModel = _settings.ActiveChatProviderConfig?.Model
        };
        await _storage.AppendSessionHeaderAsync(header);
        _headerWritten = true;
    }

    public async Task AppendMessageAsync(ChatMessage message)
    {
        var dto = new ChatMessageDto
        {
            TimestampUtc = message.Timestamp.ToUniversalTime(),
            Author = message.Author switch
            {
                MessageAuthor.Me => ChatAuthorDto.Me,
                MessageAuthor.Other => ChatAuthorDto.Other,
                MessageAuthor.AiAssistant => ChatAuthorDto.AiAssistant,
                _ => ChatAuthorDto.Other
            },
            Text = message.Text ?? string.Empty
        };
        await _storage.AppendMessageAsync(_currentSessionId, dto);
    }

    public void NewSession()
    {
        _currentSessionId = Guid.NewGuid();
        _startedUtc = DateTime.UtcNow;
        _headerWritten = false;
    }

    public Task FinalizeAsync(string title, string summary)
        => _storage.FinalizeSessionAsync(_currentSessionId, title, summary);
}
