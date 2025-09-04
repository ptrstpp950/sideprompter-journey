#if WINDOWS
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;
using PortAudioSharp;
using Stream = PortAudioSharp.Stream;

namespace AvaloniaApp.Services.AudioTranscription;

public class AudioTranscriptionServiceAlternativeWin : IAudioTranscriptionService
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

    public AudioTranscriptionServiceAlternativeWin(ITranscriptionService transcriptionService, 
                                bool enableMicrophoneCapture = true, 
                                bool enableSpeakerCapture = true)
    {
        _transcriptionService = transcriptionService;
        _enableMicrophoneCapture = enableMicrophoneCapture;
        _enableSpeakerCapture = enableSpeakerCapture;
        
        // Log the configuration
        Log(MessageType.Info, $"Audio capture configuration: Microphone={_enableMicrophoneCapture}, Speaker={_enableSpeakerCapture}");
        PortAudio.Initialize();
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
                var micDeviceIndex = PortAudio.DefaultInputDevice;
                if (micDeviceIndex == PortAudio.NoDevice)
                {
                    throw new InvalidOperationException("No default input device found.");
                }
                _micCapture = new AudioCapture(micDeviceIndex, TranscriptionMessageType.Mic, _transcriptionService, token);
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
                var speakerDeviceIndex = FindWasapiLoopbackDevice();
                if (speakerDeviceIndex == PortAudio.NoDevice)
                {
                     throw new InvalidOperationException("No WASAPI loopback device found.");
                }
                _speakerCapture = new AudioCapture(speakerDeviceIndex, TranscriptionMessageType.Speaker, _transcriptionService, token);
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
    
    private int FindWasapiLoopbackDevice()
    {
        var deviceCount = PortAudio.DeviceCount;
        for (int i = 0; i < deviceCount; i++)
        {
            var deviceInfo = PortAudio.GetDeviceInfo(i);
            if (deviceInfo.maxInputChannels > 0 &&
                deviceInfo.name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        
        var defaultOutputDeviceIndex = PortAudio.DefaultOutputDevice;
        if (defaultOutputDeviceIndex != PortAudio.NoDevice)
        {
            var defaultOutputInfo = PortAudio.GetDeviceInfo(defaultOutputDeviceIndex);
            for (int i = 0; i < deviceCount; i++)
            {
                 var deviceInfo = PortAudio.GetDeviceInfo(i);
                 if (deviceInfo.name.Contains(defaultOutputInfo.name)) //&& deviceInfo.name.Contains("Loopback"))
                 {
                     return i;
                 }
            }
        }


        return PortAudio.NoDevice;
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
        PortAudio.Terminate();
    }

    private class AudioCapture : IDisposable
    {
        private readonly int _deviceIndex;
        private readonly TranscriptionMessageType _messageType;
        private readonly ITranscriptionService _transcriptionService;
        private readonly CancellationToken _cancellationToken;
        private readonly ConcurrentQueue<byte[]> _audioChunks = new();
        private Task? _processingTask;
        private Stream? _stream;
        private readonly int _sampleRate = 16000;
        private readonly int _channels = 1;
        private readonly SampleFormat _sampleFormat = SampleFormat.Int16;

        public event Action<TranscriptionMessage>? TranscriptionReceived;
        public event Action<LogMessage>? LogReceived;

        public AudioCapture(int deviceIndex, TranscriptionMessageType messageType, ITranscriptionService transcriptionService, CancellationToken cancellationToken)
        {
            _deviceIndex = deviceIndex;
            _messageType = messageType;
            _transcriptionService = transcriptionService;
            _cancellationToken = cancellationToken;
        }

        public void Start()
        {
            var deviceInfo = PortAudio.GetDeviceInfo(_deviceIndex);
            var parameters = new StreamParameters
            {
                device = _deviceIndex,
                channelCount = _channels,
                sampleFormat = _sampleFormat,
                suggestedLatency = deviceInfo.defaultLowInputLatency
            };

            _stream = new Stream(parameters, null, _sampleRate, 0, StreamFlags.ClipOff, AudioCallback, null);
            _stream.Start();
            _processingTask = Task.Run(ProcessChunks, _cancellationToken);
            Log(MessageType.StreamStart, $"Started capturing from {_messageType}.");
        }

        public void Stop()
        {
            _stream?.Stop();
            Log(MessageType.StreamStop, $"Stopped capturing from {_messageType}.");
        }

        private Stream.Callback AudioCallback => (IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData) =>
        {
            if (frameCount > 0)
            {
                var chunk = new byte[frameCount * _channels * 2]; // 2 bytes for Int16
                Marshal.Copy(input, chunk, 0, chunk.Length);
                _audioChunks.Enqueue(chunk);
            }
            return StreamCallbackResult.Continue;
        };

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
                if (pcmData.Length == 0)
                {
                    Log(MessageType.Info, $"Audio for {_messageType} is empty. Skipping transcription.");
                    return;
                }

                if (IsAllZeros(pcmData))
                {
                    Log(MessageType.Info, $"Audio for {_messageType} is silent. Skipping transcription.");
                    return;
                }

                var results = await _transcriptionService.TranscribeAudioAsync(pcmData, _sampleRate, 16, _channels, _cancellationToken);

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
            _stream?.Close();
            _stream?.Dispose();
            _processingTask?.Dispose();
        }
    }
}
#endif

