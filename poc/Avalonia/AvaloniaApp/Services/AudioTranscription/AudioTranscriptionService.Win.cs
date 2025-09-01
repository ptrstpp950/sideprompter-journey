#if WINDOWS
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AvaloniaApp.Services.AudioTranscription;

public class AudioTranscriptionServiceWin : IAudioTranscriptionService
{
    private readonly ITranscriptionService _transcriptionService;
    private AudioCapture? _micCapture;
    private AudioCapture? _speakerCapture;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _enableMicrophoneCapture = true; // Default to true for backward compatibility
    private bool _enableSpeakerCapture = true;

    public event Action<TranscriptionMessage>? TranscriptionReceived;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning { get; private set; }

    public AudioTranscriptionServiceWin(ITranscriptionService transcriptionService, 
                                bool enableMicrophoneCapture = true, 
                                bool enableSpeakerCapture = true)
    {
        _transcriptionService = transcriptionService;
        _enableMicrophoneCapture = enableMicrophoneCapture;
        _enableSpeakerCapture = enableSpeakerCapture;
        
        // Log the configuration
        Log(MessageType.Info, $"Audio capture configuration: Microphone={_enableMicrophoneCapture}, Speaker={_enableSpeakerCapture}");
    }

    public Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }
        
        // Ensure at least one audio source is enabled
        if (!_enableMicrophoneCapture && !_enableSpeakerCapture)
        {
            Log(MessageType.Error, "Cannot start processing: both microphone and speaker capture are disabled.");
            StatusChanged?.Invoke("Error: No audio sources enabled.");
            return Task.CompletedTask;
        }

        IsRunning = true;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cancellationTokenSource.Token;

        var initTask = _transcriptionService.InitializeAsync(language, token);

        // Start microphone capture if enabled
        if (_enableMicrophoneCapture)
        {
            try
            {
                var waveIn = new WasapiCapture();
                _micCapture = new AudioCapture(waveIn, TranscriptionMessageType.Mic, _transcriptionService, token);
                _micCapture.LogReceived += OnLogReceived;
                _micCapture.TranscriptionReceived += OnTranscriptionReceived;
                _micCapture.Start();
                Log(MessageType.Info, "Microphone capture started.");
            }
            catch (Exception ex)
            {
                Log(MessageType.Error, "Failed to start microphone capture.", ex);
            }
        }
        else
        {
            Log(MessageType.Info, "Microphone capture disabled by configuration.");
        }

        // Start speaker capture if enabled
        if (_enableSpeakerCapture)
        {
            try
            {
                var waveOut = new WasapiLoopbackCapture();
                _speakerCapture = new AudioCapture(waveOut, TranscriptionMessageType.Speaker, _transcriptionService, token);
                _speakerCapture.LogReceived += OnLogReceived;
                _speakerCapture.TranscriptionReceived += OnTranscriptionReceived;
                _speakerCapture.Start();
                Log(MessageType.Info, "Speaker loopback capture started.");
            }
            catch (Exception ex)
            {
                Log(MessageType.Error, "Failed to start speaker loopback capture.", ex);
            }
        }
        else
        {
            Log(MessageType.Info, "Speaker capture disabled by configuration.");
        }
        
        StatusChanged?.Invoke("Audio processing started.");
        return initTask;
    }

    public Task StopProcessing()
    {
        if (!IsRunning)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource?.Cancel();

        // Stop only if they were started
        if (_enableMicrophoneCapture && _micCapture != null)
        {
            _micCapture.Stop();
        }
        
        if (_enableSpeakerCapture && _speakerCapture != null)
        {
            _speakerCapture.Stop();
        }

        IsRunning = false;
        StatusChanged?.Invoke("Audio processing stopped.");
        return Task.CompletedTask;
    }

    private void OnTranscriptionReceived(TranscriptionMessage message)
    { 
        TranscriptionReceived?.Invoke(message);
    }

    private void OnLogReceived(LogMessage log)
    {
        LogReceived?.Invoke(log);
    }

    /// <summary>
    /// Configure which audio sources to capture.
    /// </summary>
    /// <param name="enableMicrophone">Whether to capture from the microphone</param>
    /// <param name="enableSpeaker">Whether to capture from the speaker output</param>
    /// <returns>True if configuration was changed, false if not (because service is running)</returns>
    public bool ConfigureAudioCapture(bool enableMicrophone, bool enableSpeaker)
    {
        // Cannot change configuration while running
        if (IsRunning)
        {
            Log(MessageType.Error, "Cannot change audio capture configuration while service is running.");
            return false;
        }
        
        _enableMicrophoneCapture = enableMicrophone;
        _enableSpeakerCapture = enableSpeaker;
        
        Log(MessageType.Info, $"Audio capture configuration updated: Microphone={_enableMicrophoneCapture}, Speaker={_enableSpeakerCapture}");
        return true;
    }
    
    private void Log(MessageType type, string message, object? context = null)
    {
        LogReceived?.Invoke(new LogMessage { MessageType = type, Message = message, Timestamp = DateTime.Now, Context = context });
    }

    public void Dispose()
    {
        StopProcessing();
        
        // Dispose only if they were created
        if (_enableMicrophoneCapture && _micCapture != null)
        {
            _micCapture.Dispose();
            _micCapture = null;
        }
        
        if (_enableSpeakerCapture && _speakerCapture != null)
        {
            _speakerCapture.Dispose();
            _speakerCapture = null;
        }
        
        _cancellationTokenSource?.Dispose();
    }

    private class AudioCapture : IDisposable
    {
        private readonly IWaveIn _waveIn;
        private readonly TranscriptionMessageType _messageType;
        private readonly ITranscriptionService _transcriptionService;
        private readonly CancellationToken _cancellationToken;
        private readonly WaveFormat _resampleFormat = new(16000, 16, 1);
        private readonly ConcurrentQueue<byte[]> _audioChunks = new();
        private Task? _processingTask;

        public event Action<TranscriptionMessage>? TranscriptionReceived;
        public event Action<LogMessage>? LogReceived;

        public AudioCapture(IWaveIn waveIn, TranscriptionMessageType messageType, ITranscriptionService transcriptionService, CancellationToken cancellationToken)
        {
            _waveIn = waveIn;
            _messageType = messageType;
            _transcriptionService = transcriptionService;
            _cancellationToken = cancellationToken;
            _waveIn.DataAvailable += OnDataAvailable;
        }

        public void Start()
        {
            _processingTask = Task.Run(ProcessChunks, _cancellationToken);
            _waveIn.StartRecording();
            Log(MessageType.StreamStart, $"Started capturing from {_messageType}.");
        }

        public void Stop()
        {
            _waveIn.StopRecording();
            Log(MessageType.StreamStop, $"Stopped capturing from {_messageType}.");
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                var chunk = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
                _audioChunks.Enqueue(chunk);
            }
        }

        private async Task ProcessChunks()
        {
            using var processingStream = new MemoryStream();
            var lastProcessTime = DateTime.UtcNow;

            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    while (_audioChunks.TryDequeue(out var chunk))
                    {
                        processingStream.Write(chunk, 0, chunk.Length);
                    }

                    var shouldProcess = (DateTime.UtcNow - lastProcessTime > TimeSpan.FromSeconds(5) && processingStream.Length > 0);

                    if (shouldProcess)
                    {
                        var audioToProcess = processingStream.ToArray();
                        processingStream.SetLength(0);
                        lastProcessTime = DateTime.UtcNow;

                        _ = Transcribe(audioToProcess);
                    }
                    else
                    {
                        await Task.Delay(100, _cancellationToken);
                    }
                }

                if (processingStream.Length > 0)
                {
                    await Transcribe(processingStream.ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                Log(MessageType.Error, $"Error in audio processing loop for {_messageType}.", ex);
            }
        }

        private async Task Transcribe(byte[] pcmData)
        {
            try
            {
                //Log(MessageType.Info, $"Transcribing {_messageType} audio chunk of size {pcmData.Length} bytes.");

                await using var rawStream = new RawSourceWaveStream(pcmData, 0, pcmData.Length, _waveIn.WaveFormat);
                using var resampler = new MediaFoundationResampler(rawStream, _resampleFormat);
                using var ms = new MemoryStream();
                
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, bytesRead);
                }

                var resampledAudio = ms.ToArray();

                if (resampledAudio.Length == 0)
                {
                    Log(MessageType.Info, $"Resampled audio for {_messageType} is empty. Skipping transcription.");
                    return;
                }

                if (IsAllZeros(resampledAudio))
                {
                    Log(MessageType.Info, $"Resampled audio for {_messageType} is silent. Skipping transcription.");
                    return;
                }

                var results = await _transcriptionService.TranscribeAudioAsync(resampledAudio, _resampleFormat.SampleRate, _resampleFormat.BitsPerSample, _resampleFormat.Channels, _cancellationToken);

                foreach (var result in results)
                {
                    var message = new TranscriptionMessage
                    {
                        MessageType = _messageType,
                        Message = result.Text
                    };
                    TranscriptionReceived?.Invoke(message);
                }
            }
            catch (Exception ex)
            {
                Log(MessageType.Error, $"Transcription failed for {_messageType}.", ex);
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

        private void Log(MessageType type, string message, object? context = null)
        {
            LogReceived?.Invoke(new LogMessage { MessageType = type, Message = message, Timestamp = DateTime.Now, Context = context });
        }

        public void Dispose()
        {
            _waveIn.Dispose();
            _processingTask?.Dispose();
        }
    }
}
#endif