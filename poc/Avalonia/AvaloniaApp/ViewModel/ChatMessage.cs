using System;

namespace AvaloniaApp.ViewModel;

public class ChatMessage
{
    public string? Text { get; set; }
    public MessageAuthor Author { get; set; }
    public string? PromptId { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum MessageAuthor
{
    Me,
    Other,
    AiAssistant,
    Context
}
