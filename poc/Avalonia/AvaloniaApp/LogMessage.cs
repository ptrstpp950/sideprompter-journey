using System;

namespace AvaloniaApp;

public class LogMessage
{
    public DateTime Timestamp { get; set; }
    public MessageType MessageType { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Context { get; set; }
}
