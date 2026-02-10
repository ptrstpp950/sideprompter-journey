using System;

namespace AvaloniaApp.Services.Chat;

public enum ChatAuthorDto
{
    Me,
    Other,
    AiAssistant,
    Context
}

public class ChatMessageDto
{
    public DateTime TimestampUtc { get; set; }
    public ChatAuthorDto Author { get; set; }
    public string Text { get; set; } = string.Empty;
}
