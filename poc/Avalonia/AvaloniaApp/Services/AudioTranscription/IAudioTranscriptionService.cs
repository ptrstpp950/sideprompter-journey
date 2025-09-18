using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Services.AudioTranscription;

/// <summary>
/// Interface for audio transcription services that capture and transcribe audio
/// </summary>
public interface IAudioTranscriptionService : IDisposable
{
    /// <summary>
    /// Event fired when a log message is received
    /// </summary>
    event Action<LogMessage>? LogReceived;
    
    /// <summary>
    /// Event fired when the service status changes
    /// </summary>
    event Action<string>? StatusChanged;

    /// <summary>
    /// Indicates whether the service is currently running
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Start capturing and transcribing audio
    /// </summary>
    /// <param name="language">The language code for transcription (e.g., "en")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartProcessing(string language = "en", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop capturing and transcribing audio
    /// </summary>
    Task StopProcessing();
}

public class AudioChunk(byte[] data, DateTime timestamp)
{
    public Memory<byte> Data { get; } = data;
    public DateTime Timestamp { get; } = timestamp;
}


public class LogMessage
{
    public DateTime Timestamp { get; set; }
    public MessageType MessageType { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Context { get; set; }
}

public class TranscriptionMessage
{
    public TranscriptionMessageType MessageType { get; set; }
    public string Message { get; set; } = "Empty";
}

public enum TranscriptionMessageType
{
    Speaker,
    Mic
}
public enum MessageType
{
    Info,
    Error,
    StreamStart,
    StreamStop,
    Transcription,
    Chat
}
