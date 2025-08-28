using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp;

/// <summary>
/// Example service showing how to integrate AudioTee with transcription
/// </summary>
public class AudioTeeTranscriptionService : IDisposable
{
    private readonly AudioTeeService _audioTeeService;
    private readonly ChatCompletionService _chatCompletionService;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();

    public event Action<string>? MessageGenerated;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;

    public AudioTeeTranscriptionService(AudioTeeOptions? audioOptions = null)
    {
        _audioTeeService = new AudioTeeService(audioOptions);
        
        // TODO: Move configuration to a more appropriate place
        var endpoint = "https://openai-ptsp.openai.azure.com/";
        var deploymentName = "gpt-5-nano";
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "NO_API_KEY";
        _chatCompletionService = new ChatCompletionService(endpoint, apiKey, deploymentName);

        SetupAudioTeeEvents();
    }

    private void SetupAudioTeeEvents()
    {
        _audioTeeService.DataReceived += OnAudioDataReceived;
        _audioTeeService.Started += (s, e) => StatusChanged?.Invoke("AudioTee started");
        _audioTeeService.Stopped += (s, e) => StatusChanged?.Invoke("AudioTee stopped");
        _audioTeeService.ErrorOccurred += OnAudioTeeError;
        _audioTeeService.LogReceived += OnAudioTeeLog;
    }

    private void OnAudioDataReceived(object? sender, AudioChunk chunk)
    {
        lock (_bufferLock)
        {
            _audioBuffer.AddRange(chunk.Data.Span);
        }
        
        // You could implement real-time transcription here
        // For now, just notify that data was received
        //StatusChanged?.Invoke($"Received {chunk.Data.Length} bytes of audio data");
    }

    private void OnAudioTeeError(object? sender, Exception error)
    {
        MessageGenerated?.Invoke($"[AudioTee Error] {error.Message}");
    }

    private void OnAudioTeeLog(object? sender, LogMessage log)
    {
        LogReceived?.Invoke(log);
        
        // Optionally forward important logs as messages
        if (log.MessageType == MessageType.Error)
        {
            MessageGenerated?.Invoke($"[AudioTee] {log.Message}");
        }
    }

    
    /// <summary>
    /// Start capturing and processing audio
    /// </summary>
    public async Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            throw new InvalidOperationException("Processing is already running");

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        try
        {
            StatusChanged?.Invoke("Starting AudioTee capture...");
            
            // Start the AudioTee service
            await _audioTeeService.StartAsync(_cancellationTokenSource.Token);
            
            // Start a background task to periodically process accumulated audio
            _ = Task.Run(() => ProcessAudioPeriodically(language, _cancellationTokenSource.Token), 
                _cancellationTokenSource.Token);
            
            StatusChanged?.Invoke("Audio capture and processing started");
        }
        catch (Exception ex)
        {
            MessageGenerated?.Invoke($"[Error] Failed to start processing: {ex.Message}");
            _cancellationTokenSource = null;
            throw;
        }
    }

    /// <summary>
    /// Stop capturing and processing audio
    /// </summary>
    public async Task StopProcessing()
    {
        if (_cancellationTokenSource == null)
            return;

        try
        {
            StatusChanged?.Invoke("Stopping AudioTee capture...");
            
            _cancellationTokenSource.Cancel();
            await _audioTeeService.StopAsync();
            
            StatusChanged?.Invoke("Audio capture stopped");
        }
        catch (Exception ex)
        {
            MessageGenerated?.Invoke($"[Error] Error stopping processing: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task ProcessAudioPeriodically(string language, CancellationToken cancellationToken)
    {
        // Process accumulated audio every 3 seconds
        const int processIntervalMs = 3000;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(processIntervalMs, cancellationToken);
                
                byte[] audioToProcess;
                lock (_bufferLock)
                {
                    if (_audioBuffer.Count == 0)
                        continue;
                        
                    audioToProcess = _audioBuffer.ToArray();
                    _audioBuffer.Clear();
                }

                if (audioToProcess.Length > 0)
                {
                    await ProcessAudioChunk(audioToProcess, language);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                MessageGenerated?.Invoke($"[Error] Error processing audio: {ex.Message}");
            }
        }
    }

    private async Task ProcessAudioChunk(byte[] audioData, string language)
    {
        try
        {
            // TODO: Implement actual transcription using Whisper.NET or other service
            // For now, just log that we received data
            StatusChanged?.Invoke($"Processing {audioData.Length} bytes of audio...");
            
            // Placeholder for actual transcription logic
            // You would typically:
            // 1. Convert raw PCM data to WAV format
            // 2. Use Whisper.NET to transcribe
            // 3. Send to chat completion service if needed
            
            await Task.Delay(100); // Simulate processing time
        }
        catch (Exception ex)
        {
            MessageGenerated?.Invoke($"[Error] Transcription failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the current audio buffer size (for debugging)
    /// </summary>
    public int GetBufferSize()
    {
        lock (_bufferLock)
        {
            return _audioBuffer.Count;
        }
    }

    /// <summary>
    /// Clear the audio buffer
    /// </summary>
    public void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _audioBuffer.Clear();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _audioTeeService?.Dispose();
    }
}
