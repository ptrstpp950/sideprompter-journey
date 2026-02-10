using System;
using System.Collections.Generic;
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

    public Guid CurrentSessionId => _currentSessionId;

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
                MessageAuthor.Context => ChatAuthorDto.Context,
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

    /// <summary>
    /// Load a previously saved session and set it as the current session
    /// so new messages get appended to it.
    /// Returns the session export (header + messages) for populating the ChatViewModel.
    /// </summary>
    public async Task<ChatSessionExport?> LoadAndResumeSessionAsync(Guid sessionId)
    {
        // First load the full session data for display
        var export = await _storage.LoadSessionAsync(sessionId);
        if (export == null) return null;

        // Re-open the finalized JSON back to JSONL for continued recording
        var header = await _storage.ReopenSessionAsync(sessionId);
        if (header != null)
        {
            _currentSessionId = header.SessionId;
            _startedUtc = header.StartedUtc;
            _headerWritten = true; // Header already exists in the reopened JSONL
        }
        else
        {
            // Session was already in JSONL form (in-progress) - just point to it
            _currentSessionId = export.SessionId;
            _startedUtc = export.StartedUtc;
            _headerWritten = true;
        }

        return export;
    }

    /// <summary>
    /// Load a session for read-only viewing (no re-opening for recording).
    /// </summary>
    public Task<ChatSessionExport?> LoadSessionAsync(Guid sessionId)
        => _storage.LoadSessionAsync(sessionId);

    /// <summary>
    /// List all saved sessions, auto-finalizing any orphaned in-progress sessions.
    /// </summary>
    public Task<List<ChatSessionExport>> ListSessionsAsync()
        => _storage.ListSessionsAsync(_currentSessionId);

    /// <summary>
    /// Update the title of a session.
    /// </summary>
    public Task UpdateSessionTitleAsync(Guid sessionId, string newTitle)
        => _storage.UpdateSessionTitleAsync(sessionId, newTitle);

    /// <summary>
    /// Delete a session permanently.
    /// </summary>
    public Task DeleteSessionAsync(Guid sessionId)
        => _storage.DeleteSessionAsync(sessionId);
}
