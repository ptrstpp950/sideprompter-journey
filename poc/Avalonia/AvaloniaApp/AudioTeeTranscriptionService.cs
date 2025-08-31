using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace AvaloniaApp;

/// <summary>
/// Service for integrating AudioTee with transcription
/// </summary>
public class AudioTeeTranscriptionService : IAudioTranscriptionService
{
    private readonly AudioTeeService _audioTeeService;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;

    public event Action<string>? TranscriptionReceived;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;
    
    public bool IsRunning => _cancellationTokenSource != null;

    public AudioTeeTranscriptionService(AudioTeeOptions? audioOptions = null)
    {
        _audioTeeService = new AudioTeeService(audioOptions);
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
            _audioBuffer.AddRange(chunk.Data.ToArray());
        }
        
        // You could implement real-time transcription here
        // For now, just notify that data was received
        //StatusChanged?.Invoke($"Received {chunk.Data.Length} bytes of audio data");
    }

    private void OnAudioTeeError(object? sender, Exception error)
    {
        TranscriptionReceived?.Invoke($"[AudioTee Error] {error.Message}");
    }

    private void OnAudioTeeLog(object? sender, LogMessage log)
    {
        LogReceived?.Invoke(log);
        
        // Optionally forward important logs as messages
        if (log.MessageType == MessageType.Error)
        {
            TranscriptionReceived?.Invoke($"[AudioTee] {log.Message}");
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
            StatusChanged?.Invoke("Initializing Whisper model...");
            
            // Initialize Whisper model
            await InitializeWhisperAsync(language, _cancellationTokenSource.Token);
            
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
            TranscriptionReceived?.Invoke($"[Error] Failed to start processing: {ex.Message}");
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
            TranscriptionReceived?.Invoke($"[Error] Error stopping processing: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            // Clean up Whisper resources
            _whisperProcessor?.Dispose();
            _whisperProcessor = null;
            _whisperFactory?.Dispose();
            _whisperFactory = null;
        }
    }

    /// <summary>
    /// Initialize the Whisper model for transcription
    /// </summary>
    private async Task InitializeWhisperAsync(string language, CancellationToken cancellationToken)
    {
        var type = GgmlType.Tiny;
        var modelName = "ggml-" + type + ".bin";
        var modelPath = Path.GetFullPath(Path.Combine("./models", modelName));

        if (!Directory.Exists(Path.GetDirectoryName(modelPath)))
        {
            StatusChanged?.Invoke($"Creating directory for Whisper model: {Path.GetDirectoryName(modelPath)}");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        }

        if (!File.Exists(modelPath))
        {
            StatusChanged?.Invoke($"Downloading Whisper model '{modelName}' to '{modelPath}' ...");
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, cancellationToken: cancellationToken);
            await using var fileStream = File.Create(modelPath);
            await modelStream.CopyToAsync(fileStream, cancellationToken);
            StatusChanged?.Invoke("Model downloaded.");
        }

        _whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions() { UseGpu = true });
        _whisperProcessor = _whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .Build();
            
        StatusChanged?.Invoke("Whisper model initialized");
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
                TranscriptionReceived?.Invoke($"[Error] Error processing audio: {ex.Message}");
            }
        }
    }

    private async Task ProcessAudioChunk(byte[] audioData, string language)
    {
        try
        {
            if (_whisperProcessor == null)
            {
                TranscriptionReceived?.Invoke("[Error] Whisper processor not initialized");
                return;
            }

            // Skip processing if audio data is too small or all zeros
            if (audioData.Length < 1000 || IsAllZeros(audioData))
            {
                return;
            }

            StatusChanged?.Invoke($"Transcribing {audioData.Length} bytes of audio...");
            
            // Convert raw PCM data to WAV format
            using var stream = new MemoryStream();
            
            // AudioTee outputs 16-bit signed PCM at the specified sample rate
            // Default sample rate from AudioTee is typically 44100 or 48000, but we configured it for 16000
            var sampleRate = 16000; // This should match your AudioTeeOptions.SampleRate
            var channels = 1; // AudioTee outputs mono
            var bitsPerSample = 16;
            
            using var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
            
            // Write the raw PCM data directly
            writer.Write(audioData, 0, audioData.Length);
            writer.Flush();
            
            // Reset stream position for reading
            stream.Position = 0;

            // Process with Whisper
            var hasTranscription = false;
            await foreach (var result in _whisperProcessor.ProcessAsync(stream, CancellationToken.None))
            {
                if (IsEmptyOrSound(result.Text))
                    continue;

                hasTranscription = true;
                var transcription = $"[AudioTee] {result.Text}";
                TranscriptionReceived?.Invoke(transcription);
            }
            
            if (hasTranscription)
            {
                StatusChanged?.Invoke("Transcription completed");
            }
        }
        catch (Exception ex)
        {
            TranscriptionReceived?.Invoke($"[Error] Transcription failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if audio data is all zeros (silence)
    /// </summary>
    private static bool IsAllZeros(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if transcription result is empty or just sound indicators
    /// </summary>
    private static bool IsEmptyOrSound(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
            return true;
        return text.Length == 0;
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
        _whisperProcessor?.Dispose();
        _whisperFactory?.Dispose();
        _audioTeeService?.Dispose();
    }
}
