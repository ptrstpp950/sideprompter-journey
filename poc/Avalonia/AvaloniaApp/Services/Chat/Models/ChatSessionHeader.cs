using System;

namespace AvaloniaApp.Services.Chat;

public class ChatSessionHeader
{
    public Guid SessionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public string Language { get; set; } = "";
    public string? PromptId { get; set; }
    public string? PromptText { get; set; }
    public string? WhisperModel { get; set; }
    public string? ChatModel { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
}
