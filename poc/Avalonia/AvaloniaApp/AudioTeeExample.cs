using System;
using System.Threading.Tasks;

namespace AvaloniaApp;

/// <summary>
/// Example showing how to use AudioTeeService in your application
/// </summary>
public class AudioTeeExample
{
    private AudioTeeService? _audioTeeService;
    private AudioTeeTranscriptionService? _transcriptionService;

    /// <summary>
    /// Example of basic AudioTee usage
    /// </summary>
    public async Task BasicUsageExample()
    {
        // Configure AudioTee options
        var options = new AudioTeeOptions
        {
            SampleRate = 16000,          // 16kHz for speech recognition
            ChunkDurationMs = 1000,      // 1 second chunks
            Mute = false                 // Don't mute system audio
        };

        // Create the service
        _audioTeeService = new AudioTeeService(options);

        // Subscribe to events
        _audioTeeService.DataReceived += OnAudioDataReceived;
        _audioTeeService.Started += OnAudioTeeStarted;
        _audioTeeService.Stopped += OnAudioTeeStopped;
        _audioTeeService.ErrorOccurred += OnAudioTeeError;
        _audioTeeService.LogReceived += OnAudioTeeLogReceived;

        try
        {
            Console.WriteLine("Starting AudioTee...");
            await _audioTeeService.StartAsync();

            // Let it run for 10 seconds
            await Task.Delay(10000);

            Console.WriteLine("Stopping AudioTee...");
            await _audioTeeService.StopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            _audioTeeService?.Dispose();
        }
    }

    /// <summary>
    /// Example of using AudioTee with transcription
    /// </summary>
    public async Task TranscriptionExample()
    {
        // Configure AudioTee for speech recognition
        var audioOptions = new AudioTeeOptions
        {
            SampleRate = 16000,     // Optimal for speech recognition
            ChunkDurationMs = 2000  // 2 second chunks for better transcription
        };

        // Create the transcription service
        _transcriptionService = new AudioTeeTranscriptionService(audioOptions);

        // Subscribe to events
        _transcriptionService.MessageGenerated += OnTranscriptionMessage;
        _transcriptionService.StatusChanged += OnStatusChanged;
        _transcriptionService.LogReceived += OnLogReceived;

        try
        {
            Console.WriteLine("Starting audio transcription...");
            await _transcriptionService.StartProcessing("en"); // English

            // Let it run for 30 seconds
            await Task.Delay(30000);

            Console.WriteLine("Stopping audio transcription...");
            await _transcriptionService.StopProcessing();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Transcription error: {ex.Message}");
        }
        finally
        {
            _transcriptionService?.Dispose();
        }
    }

    /// <summary>
    /// Example of filtering specific applications
    /// </summary>
    public async Task FilteredCaptureExample()
    {
        // Only capture audio from specific processes (e.g., video call apps)
        var options = new AudioTeeOptions
        {
            SampleRate = 16000,
            ChunkDurationMs = 1000,
            // IncludeProcesses = new[] { 1234, 5678 }, // Specific process IDs
            // ExcludeProcesses = new[] { 9999 }        // Exclude system sounds
        };

        _audioTeeService = new AudioTeeService(options);
        _audioTeeService.DataReceived += OnAudioDataReceived;

        try
        {
            await _audioTeeService.StartAsync();
            await Task.Delay(15000); // Run for 15 seconds
            await _audioTeeService.StopAsync();
        }
        finally
        {
            _audioTeeService?.Dispose();
        }
    }

    #region Event Handlers

    private void OnAudioDataReceived(object? sender, AudioChunk chunk)
    {
        Console.WriteLine($"Received {chunk.Data.Length} bytes of audio data at {chunk.Timestamp}");
        
        // Here you could:
        // - Save to file
        // - Send to speech recognition
        // - Process in real-time
        // - Buffer for later processing
    }

    private void OnAudioTeeStarted(object? sender, EventArgs e)
    {
        Console.WriteLine("AudioTee capture started");
    }

    private void OnAudioTeeStopped(object? sender, EventArgs e)
    {
        Console.WriteLine("AudioTee capture stopped");
    }

    private void OnAudioTeeError(object? sender, Exception error)
    {
        Console.WriteLine($"AudioTee error: {error.Message}");
    }

    private void OnAudioTeeLogReceived(object? sender, LogMessage log)
    {
        Console.WriteLine($"AudioTee log [{log.MessageType}]: {log.Message}");
    }

    private void OnTranscriptionMessage(string message)
    {
        Console.WriteLine($"Transcription: {message}");
    }

    private void OnStatusChanged(string status)
    {
        Console.WriteLine($"Status: {status}");
    }

    private void OnLogReceived(LogMessage log)
    {
        Console.WriteLine($"Log [{log.MessageType}] {log.Timestamp}: {log.Message}");
    }

    #endregion

    /// <summary>
    /// Helper method to get running processes (for filtering)
    /// </summary>
    public static void ListRunningProcesses()
    {
        var processes = System.Diagnostics.Process.GetProcesses();
        Console.WriteLine("Running processes:");
        
        foreach (var process in processes)
        {
            try
            {
                Console.WriteLine($"PID: {process.Id}, Name: {process.ProcessName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading process {process.Id}: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Extension methods for easier AudioTee usage
/// </summary>
public static class AudioTeeExtensions
{
    /// <summary>
    /// Create AudioTee service with common speech recognition settings
    /// </summary>
    public static AudioTeeService CreateForSpeechRecognition()
    {
        return new AudioTeeService(new AudioTeeOptions
        {
            SampleRate = 16000,
            ChunkDurationMs = 1000
        });
    }

    /// <summary>
    /// Create AudioTee service with high quality audio settings
    /// </summary>
    public static AudioTeeService CreateForHighQuality()
    {
        return new AudioTeeService(new AudioTeeOptions
        {
            SampleRate = 44100,
            ChunkDurationMs = 500
        });
    }
}
