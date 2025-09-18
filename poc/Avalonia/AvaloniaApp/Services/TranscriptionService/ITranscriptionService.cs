using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Services.TranscriptionService;

/// <summary>
/// Interface for services that perform speech-to-text transcription
/// </summary>
public interface ITranscriptionService : IDisposable
{
    /// <summary>
    /// Initialize the transcription service with the specified language
    /// </summary>
    /// <param name="language">Language code (e.g., "en")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeAsync(string language, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Transcribe audio data
    /// </summary>
    /// <param name="audioData">Raw audio data in PCM format</param>
    /// <param name="sampleRate">Sample rate of the audio</param>
    /// <param name="bitsPerSample">Bits per sample (typically 16)</param>
    /// <param name="channels">Number of channels (typically 1 for mono)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A collection of transcription results</returns>
    Task TranscribeAudioAsync(byte[] audioData, 
        int sampleRate = 16000, 
        int bitsPerSample = 16, 
        int channels = 1, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event fired when the status of the transcription service changes
    /// </summary>
    event Action<string>? StatusChanged;

    /// <summary>
    /// Event fired when a transcription result is produced by the service.
    /// Handlers receive the produced <see cref="TranscriptionResult"/>.
    /// </summary>
    event Action<TranscriptionResult>? TranscriptionReceived;
}

/// <summary>
/// Result of an audio transcription
/// </summary>
public class TranscriptionResult
{
    /// <summary>
    /// The transcribed text
    /// </summary>
    public string Text { get; }
    
    /// <summary>
    /// Source of the transcription (e.g., "mic", "speaker")
    /// </summary>
    public string Source { get; }
    
    /// <summary>
    /// Timestamp of the transcription
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// Additional metadata about the transcription
    /// </summary>
    public Dictionary<string, object>? Metadata { get; }
    
    public TranscriptionResult(string text, string source, DateTime timestamp, Dictionary<string, object>? metadata = null)
    {
        Text = text;
        Source = source;
        Timestamp = timestamp;
        Metadata = metadata;
    }
}
