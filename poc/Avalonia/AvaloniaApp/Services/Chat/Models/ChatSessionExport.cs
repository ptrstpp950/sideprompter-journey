using System;
using System.Collections.Generic;

namespace AvaloniaApp.Services.Chat;

public class ChatSessionExport
{
    public Guid SessionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? PromptId { get; set; }
    public string? PromptText { get; set; }
    public string? WhisperModel { get; set; }
    public string? ChatModel { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = new();
}
