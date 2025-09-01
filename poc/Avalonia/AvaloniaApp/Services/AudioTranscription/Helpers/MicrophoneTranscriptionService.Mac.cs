using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;

namespace AvaloniaApp.Services.AudioTranscription.Helpers;

public class MicrophoneTranscriptionService : IAudioTranscriptionService
{
    private readonly MicrophoneService _microphoneService;
    private readonly ITranscriptionService _transcriptionService;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();

    public event Action<TranscriptionMessage>? TranscriptionReceived;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _cancellationTokenSource != null;

    public MicrophoneTranscriptionService(ITranscriptionService transcriptionService, MicrophoneOptions? microphoneOptions = null)
    {
        _transcriptionService = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));
        _microphoneService = new MicrophoneService(microphoneOptions);
        
        // Forward status change events from the transcription service
        _transcriptionService.StatusChanged += status => StatusChanged?.Invoke(status);
        
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
        LogReceived?.Invoke(new LogMessage{MessageType = MessageType.Error, Message = error.Message});
    }

    private void OnMicrophoneLog(object? sender, LogMessage log)
    {
        LogReceived?.Invoke(log);
    }

    public async Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            throw new InvalidOperationException("Processing is already running");

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        try
        {
            StatusChanged?.Invoke("Initializing transcription service...");
            await _transcriptionService.InitializeAsync(language, _cancellationTokenSource.Token);
            
            StatusChanged?.Invoke("Starting microphone capture...");
            await _microphoneService.StartAsync(_cancellationTokenSource.Token);
            
            _ = Task.Run(() => ProcessAudioPeriodically(_cancellationTokenSource.Token), 
                _cancellationTokenSource.Token);
            
            StatusChanged?.Invoke("Audio capture and processing started");
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke(new LogMessage{MessageType = MessageType.Error, Message = ex.Message});
            _cancellationTokenSource = null;
        }
    }

    public async Task StopProcessing()
    {
        if (_cancellationTokenSource == null)
            return;

        try
        {
            StatusChanged?.Invoke("Stopping microphone capture...");
            
            await _cancellationTokenSource.CancelAsync();
            await _microphoneService.StopAsync();
            
            StatusChanged?.Invoke("Audio capture stopped");
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke(new LogMessage{MessageType = MessageType.Error, Message = ex.Message});
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task ProcessAudioPeriodically(CancellationToken cancellationToken)
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
                    await ProcessAudioChunk(audioToProcess);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke(new LogMessage{MessageType = MessageType.Error, Message = ex.Message});
            }
        }
    }

    private async Task ProcessAudioChunk(byte[] audioData)
    {
        try
        {
            if (audioData.Length < 1000)
            {
                return;
            }

            StatusChanged?.Invoke($"Transcribing {audioData.Length} bytes of audio...");
            
            // Use the transcription service
            var sampleRate = 16000; // This should match your MicrophoneOptions.SampleRate
            var channels = 1;
            var bitsPerSample = 16;

            var results = await _transcriptionService.TranscribeAudioAsync(
                audioData, 
                sampleRate, 
                bitsPerSample, 
                channels);
                
            if (results.Length > 0)
            {
                foreach (var result in results)
                {
                    TranscriptionReceived?.Invoke(
                        new TranscriptionMessage { MessageType = TranscriptionMessageType.Mic, Message = result.Text });
                }
                
                StatusChanged?.Invoke("Transcription completed");
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke(new LogMessage{MessageType = MessageType.Error, Message = ex.Message});
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _microphoneService.Dispose();
    }
}
