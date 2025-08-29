using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp;

/// <summary>
/// Options for configuring AudioTee capture
/// </summary>
public class AudioTeeOptions
{
    /// <summary>
    /// Sample rate for audio capture (default: system default)
    /// </summary>
    public int? SampleRate { get; set; }

    /// <summary>
    /// Chunk duration in milliseconds for audio data events
    /// </summary>
    public int? ChunkDurationMs { get; set; }

    /// <summary>
    /// Whether to mute system audio during capture
    /// </summary>
    public bool Mute { get; set; } = false;

    /// <summary>
    /// Process IDs to include in capture (null for all)
    /// </summary>
    public IReadOnlyList<int>? IncludeProcesses { get; set; }

    /// <summary>
    /// Process IDs to exclude from capture
    /// </summary>
    public IReadOnlyList<int>? ExcludeProcesses { get; set; }
}

/// <summary>
/// Audio chunk data received from AudioTee
/// </summary>
public class AudioChunk
{
    public ReadOnlyMemory<byte> Data { get; }
    public DateTime Timestamp { get; }

    public AudioChunk(ReadOnlyMemory<byte> data, DateTime timestamp)
    {
        Data = data;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Log message types from AudioTee
/// </summary>
public enum MessageType
{
    Metadata,
    StreamStart,
    StreamStop,
    Info,
    Error,
    Debug
}

/// <summary>
/// Log levels
/// </summary>
public enum LogLevel
{
    Info,
    Debug,
    Error
}

/// <summary>
/// Log message from AudioTee
/// </summary>
public class LogMessage
{
    public DateTime Timestamp { get; set; }
    public MessageType MessageType { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Context { get; set; }
}

/// <summary>
/// C# wrapper for the AudioTee macOS audio capture binary
/// </summary>
public class AudioTeeService : IDisposable
{
    private Process? _process;
    private readonly AudioTeeOptions _options;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private bool _disposed;

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

    public bool IsRunning => _isRunning && _process?.HasExited == false;

    public AudioTeeService(AudioTeeOptions? options = null)
    {
        _options = options ?? new AudioTeeOptions();
    }

    /// <summary>
    /// Start audio capture
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("AudioTee is already running");

        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioTeeService));

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var binaryPath = GetAudioTeeBinaryPath();
            var arguments = BuildArguments();
            var argumentString = string.Join(" ", arguments);

            // Log the command being executed
            var commandLog = new LogMessage
            {
                Timestamp = DateTime.UtcNow,
                MessageType = MessageType.Info,
                Message = $"Starting AudioTee: {binaryPath} {argumentString}"
            };
            LogReceived?.Invoke(this, commandLog);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = argumentString,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = processStartInfo };
            
            // Disable output buffering if possible
            _process.StartInfo.StandardOutputEncoding = null;
            //_process.BeginOutputReadLine();
            
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start AudioTee process");
            }
            //_process.BeginOutputReadLine();
            //_process.BeginErrorReadLine();
            
            // Handle stdout for audio data
            _ = Task.Run(() => HandleStdoutAsync(_process, _cancellationTokenSource.Token), 
                _cancellationTokenSource.Token);

            // Handle stderr for logs
            _ = Task.Run(() => HandleStderrAsync(_process, _cancellationTokenSource.Token), 
                _cancellationTokenSource.Token);


            _isRunning = true;
            Started?.Invoke(this, EventArgs.Empty);

            // Wait for either task to complete or cancellation
            //await _process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _isRunning = false;
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>
    /// Stop audio capture
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning || _process == null)
            return;

        _cancellationTokenSource?.Cancel();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            _isRunning = false;
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private string GetAudioTeeBinaryPath()
    {
        // For macOS app bundles, check Resources first
        if (OperatingSystem.IsMacOS())
        {
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                "..", "Resources", "libs", "audioteejs", "bin", "audiotee");
            if (File.Exists(resourcesPath))
                return resourcesPath;
        }

        // For regular builds, try the libs directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var libsPath = Path.Combine(baseDir, "libs", "audioteejs", "bin", "audiotee");
        
        if (File.Exists(libsPath))
            return libsPath;

        // Fallback to looking in the current directory structure
        var currentDir = Directory.GetCurrentDirectory();
        var projectPath = Path.Combine(currentDir, "libs", "audioteejs", "bin", "audiotee");
        
        if (File.Exists(projectPath))
            return projectPath;

        // Last resort - assume it's in PATH
        return "audiotee";
    }

    private List<string> BuildArguments()
    {
        var args = new List<string>();

        if (_options.SampleRate.HasValue)
        {
            args.Add("--sample-rate");
            args.Add(_options.SampleRate.Value.ToString());
        }

        if (_options.ChunkDurationMs.HasValue)
        {
            args.Add("--chunk-duration");
            // Convert milliseconds to seconds for AudioTee
            args.Add((_options.ChunkDurationMs.Value / 1000.0).ToString("F1"));
        }

        if (_options.Mute)
        {
            args.Add("--mute");
        }

        if (_options.IncludeProcesses?.Count > 0)
        {
            args.Add("--include-processes");
            args.Add(string.Join(",", _options.IncludeProcesses));
        }

        if (_options.ExcludeProcesses?.Count > 0)
        {
            args.Add("--exclude-processes");
            args.Add(string.Join(",", _options.ExcludeProcesses));
        }

        return args;
    }

    private async Task HandleStdoutAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[10*1024];
            
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                // Clear the buffer to ensure we're not reading stale data
                Array.Clear(buffer, 0, buffer.Length);
                
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    // End of stream or no data available, wait a bit and continue
                    await Task.Delay(10, cancellationToken);
                    continue;
                }
                
                // Create audio chunk with a copy of the data
                var audioData = new byte[bytesRead];
                Array.Copy(buffer, 0, audioData, 0, bytesRead);

                var chunk = new AudioChunk(audioData, DateTime.UtcNow);
                
                DataReceived?.Invoke(this, chunk);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private async Task HandleStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = process.StandardError;
            
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;

                try
                {
                    // Try to parse as JSON log message
                    var logMessage = JsonSerializer.Deserialize<LogMessage>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (logMessage != null)
                    {
                        LogReceived?.Invoke(this, logMessage);
                    }
                }
                catch (JsonException)
                {
                    // If it's not JSON, treat as plain text log
                    var plainLogMessage = new LogMessage
                    {
                        Timestamp = DateTime.UtcNow,
                        MessageType = MessageType.Info,
                        Message = line
                    };
                    LogReceived?.Invoke(this, plainLogMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _isRunning = false;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        if (_process != null)
        {
            _process.Exited -= OnProcessExited;
            
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill();
                    _process.WaitForExit(5000); // Wait up to 5 seconds
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
            
            _process.Dispose();
        }
    }
}
