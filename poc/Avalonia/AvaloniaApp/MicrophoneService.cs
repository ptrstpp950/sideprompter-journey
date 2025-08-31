using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PortAudioSharp;
using System.Runtime.InteropServices;

namespace AvaloniaApp;

/// <summary>
/// Options for configuring Microphone capture
/// </summary>
public class MicrophoneOptions
{
    /// <summary>
    /// Sample rate for audio capture.
    /// </summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>
    /// Chunk duration in milliseconds for audio data events.
    /// </summary>
    public int ChunkDurationMs { get; set; } = 100; // 100ms chunks

    /// <summary>
    /// Device index to use for capture. -1 for default.
    /// </summary>
    public int DeviceIndex { get; set; } = -1;
}

// Re-using AudioChunk from AudioTeeService.cs. Consider moving to a shared file.
// public class AudioChunk ...

// Re-using LogMessage from AudioTeeService.cs. Consider moving to a shared file.
// public class LogMessage ...

/// <summary>
/// C# wrapper for microphone audio capture using PortAudioSharp2
/// </summary>
public class MicrophoneService : IDisposable
{
    private readonly MicrophoneOptions _options;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private bool _disposed;
    private Stream? _stream;
    private List<byte> _buffer = new List<byte>();
    private int _chunkSize;

    /// <summary>
    /// Fired when audio data is received
    /// </summary>
    public event EventHandler<AudioChunk>? DataReceived;

    /// <summary>
    /// Fired when audio capture starts
    /// </summary>
    public event EventHandler? Started;

    /// <summary>
    /// Fired when audio capture stops
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// Fired when an error occurs
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Fired when a log message is received
    /// </summary>
    public event EventHandler<LogMessage>? LogReceived;

    public bool IsRunning => _isRunning;

    public MicrophoneService(MicrophoneOptions? options = null)
    {
        _options = options ?? new MicrophoneOptions();
        PortAudio.Initialize();
    }

    /// <summary>
    /// Start audio capture
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("MicrophoneService is already running");

        if (_disposed)
            throw new ObjectDisposedException(nameof(MicrophoneService));

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        Log(MessageType.Info, "Starting microphone capture...");

        try
        {
            var deviceIndex = _options.DeviceIndex == -1 ? PortAudio.DefaultInputDevice : _options.DeviceIndex;
            var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
            
            Log(MessageType.Info, $"Using device: {deviceInfo.name}");

            var inputParameters = new StreamParameters
            {
                device = deviceIndex,
                channelCount = 1,
                sampleFormat = SampleFormat.Int16, // 16-bit integer
                suggestedLatency = deviceInfo.defaultLowInputLatency
            };

            // Calculate chunk size in bytes
            // 16-bit = 2 bytes per sample
            _chunkSize = _options.SampleRate * _options.ChunkDurationMs / 1000 * 2; 

            _stream = new Stream(inputParameters, null, _options.SampleRate, 0, StreamFlags.NoFlag, AudioCallback, null);
            _stream.Start();

            _isRunning = true;
            Started?.Invoke(this, EventArgs.Empty);
            Log(MessageType.StreamStart, "Microphone capture started.");
        }
        catch (Exception ex)
        {
            Log(MessageType.Error, $"Failed to start microphone capture: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop audio capture
    /// </summary>
    public Task StopAsync()
    {
        if (!_isRunning || _stream == null)
            return Task.CompletedTask;

        Log(MessageType.Info, "Stopping microphone capture...");
        _cancellationTokenSource?.Cancel();

        try
        {
            _stream.Stop();
            _stream.Close();
            _stream.Dispose();
            _stream = null;
        }
        catch (Exception ex)
        {
            Log(MessageType.Error, $"Error stopping microphone capture: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            _isRunning = false;
            Stopped?.Invoke(this, EventArgs.Empty);
            Log(MessageType.StreamStop, "Microphone capture stopped.");
        }

        return Task.CompletedTask;
    }

    private StreamCallbackResult AudioCallback(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (_cancellationTokenSource?.IsCancellationRequested == true)
        {
            return StreamCallbackResult.Complete;
        }

        if (input == IntPtr.Zero)
        {
            return StreamCallbackResult.Continue;
        }

        var byteCount = (int)(frameCount * sizeof(short));
        var data = new byte[byteCount];
        Marshal.Copy(input, data, 0, byteCount);

        _buffer.AddRange(data);

        while (_buffer.Count >= _chunkSize)
        {
            var chunkData = new byte[_chunkSize];
            _buffer.CopyTo(0, chunkData, 0, _chunkSize);
            _buffer.RemoveRange(0, _chunkSize);

            var audioChunk = new AudioChunk(chunkData, DateTime.UtcNow);
            DataReceived?.Invoke(this, audioChunk);
        }

        return StreamCallbackResult.Continue;
    }
    
    private void Log(MessageType type, string message, Dictionary<string, object>? context = null)
    {
        var logMessage = new LogMessage
        {
            Timestamp = DateTime.UtcNow,
            MessageType = type,
            Message = message,
            Context = context
        };
        LogReceived?.Invoke(this, logMessage);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        StopAsync().Wait();

        PortAudio.Terminate();
    }
}
