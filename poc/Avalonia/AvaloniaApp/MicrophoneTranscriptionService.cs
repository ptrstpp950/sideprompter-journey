using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace AvaloniaApp;

public class MicrophoneTranscriptionService : IAudioTranscriptionService
{
    private readonly MicrophoneService _microphoneService;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;

    public event Action<string>? TranscriptionReceived;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _cancellationTokenSource != null;

    public MicrophoneTranscriptionService(MicrophoneOptions? microphoneOptions = null)
    {
        _microphoneService = new MicrophoneService(microphoneOptions);
        SetupMicrophoneEvents();
    }

    private void SetupMicrophoneEvents()
    {
        _microphoneService.DataReceived += OnAudioDataReceived;
        _microphoneService.Started += (s, e) => StatusChanged?.Invoke("Microphone started");
        _microphoneService.Stopped += (s, e) => StatusChanged?.Invoke("Microphone stopped");
        _microphoneService.ErrorOccurred += OnMicrophoneError;
        _microphoneService.LogReceived += OnMicrophoneLog;
    }

    private void OnAudioDataReceived(object? sender, AudioChunk chunk)
    {
        lock (_bufferLock)
        {
            _audioBuffer.AddRange(chunk.Data.ToArray());
        }
    }

    private void OnMicrophoneError(object? sender, Exception error)
    {
        TranscriptionReceived?.Invoke($"[Microphone Error] {error.Message}");
    }

    private void OnMicrophoneLog(object? sender, LogMessage log)
    {
        LogReceived?.Invoke(log);
        
        if (log.MessageType == MessageType.Error)
        {
            TranscriptionReceived?.Invoke($"[Microphone] {log.Message}");
        }
    }

    public async Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            throw new InvalidOperationException("Processing is already running");

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        try
        {
            StatusChanged?.Invoke("Initializing Whisper model...");
            await InitializeWhisperAsync(language, _cancellationTokenSource.Token);
            
            StatusChanged?.Invoke("Starting Microphone capture...");
            await _microphoneService.StartAsync(_cancellationTokenSource.Token);
            
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

    public async Task StopProcessing()
    {
        if (_cancellationTokenSource == null)
            return;

        try
        {
            StatusChanged?.Invoke("Stopping Microphone capture...");
            
            await _cancellationTokenSource.CancelAsync();
            await _microphoneService.StopAsync();
            
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
            
            _whisperProcessor?.DisposeAsync();
            _whisperProcessor = null;
            _whisperFactory?.Dispose();
            _whisperFactory = null;
        }
    }

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

            if (audioData.Length < 1000 || IsAllZeros(audioData))
            {
                return;
            }

            StatusChanged?.Invoke($"Transcribing {audioData.Length} bytes of audio...");
            
            using var stream = new MemoryStream();
            
            var sampleRate = 16000; // This should match your MicrophoneOptions.SampleRate
            var channels = 1;
            var bitsPerSample = 16;
            
            using var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
            
            writer.Write(audioData, 0, audioData.Length);
            writer.Flush();
            
            stream.Position = 0;

            var hasTranscription = false;
            await foreach (var result in _whisperProcessor.ProcessAsync(stream, CancellationToken.None))
            {
                if (IsEmptyOrSound(result.Text))
                    continue;

                hasTranscription = true;
                var transcription = $"[Mic] {result.Text}";
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

    private static bool IsAllZeros(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
                return false;
        }
        return true;
    }

    private static bool IsEmptyOrSound(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
            return true;
        return text.Length == 0;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _whisperProcessor?.Dispose();
        _whisperFactory?.Dispose();
        _microphoneService?.Dispose();
    }
}
