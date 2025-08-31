using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp;

/// <summary>
/// Interface for audio transcription services that capture and transcribe audio
/// </summary>
public interface IAudioTranscriptionService : IDisposable
{
    /// <summary>
    /// Event fired when a transcription is received
    /// </summary>
    event Action<string>? TranscriptionReceived;
    
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
