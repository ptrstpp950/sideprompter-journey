using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Logging;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.Logger;

namespace AvaloniaApp.Services.TranscriptionService;

/// <summary>
/// Implementation of ITranscriptionService using Whisper.net
/// </summary>
public class WhisperTranscriptionService : ITranscriptionService
{
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;
    private bool _disposed;
    private string _currentLanguage = "en";
    private readonly GgmlType _modelType;
    private readonly string _modelDirectory;

    public event Action<string>? StatusChanged;
    public event Action<TranscriptionResult>? TranscriptionReceived;

    /// <summary>
    /// Create a new instance of WhisperTranscriptionService
    /// </summary>
    /// <param name="modelType">Type of Whisper model to use</param>
    public WhisperTranscriptionService(GgmlType modelType = GgmlType.Base)
    {
        _modelType = modelType;
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // Points to ~/Library/Application Support on Mac
        _modelDirectory = Path.Combine(appSupport, "SidePrompter");
        if (!Directory.Exists(Path.GetDirectoryName(_modelDirectory)))
        {
            StatusChanged?.Invoke($"Creating directory for Whisper model: {Path.GetDirectoryName(_modelDirectory)}");
            Directory.CreateDirectory(Path.GetDirectoryName(_modelDirectory)!);
        }
    }

    /// <summary>
    /// Ensures the Whisper model binary for the specified type exists on disk, downloading it if needed.
    /// Returns the absolute path to the model file.
    /// </summary>
    public static async Task<string> EnsureModelDownloadedAsync(GgmlType modelType, Action<string>? status = null, CancellationToken cancellationToken = default)
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var modelDir = Path.Combine(appSupport, "SidePrompter");
        if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
        var modelName = $"ggml-{modelType}.bin";
        var modelPath = Path.Combine(modelDir, modelName);
        if (File.Exists(modelPath))
        {
            status?.Invoke($"Model '{modelName}' already present.");
            return modelPath;
        }
        status?.Invoke($"Downloading Whisper model '{modelName}'...");

        var partialPath = modelPath + ".partial";
        try
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { /* ignore */ }
            }

            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType, cancellationToken: cancellationToken);
            await using var fileStream = File.Create(partialPath);
            long? total = null; try { total = modelStream.Length; } catch { }
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await modelStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total.HasValue)
                {
                    var pct = (double)written / total.Value * 100d;
                    if (pct % 2 < 0.5)
                        status?.Invoke($"Downloading Whisper model '{modelName}'... {pct:0.#}%");
                }
            }
            await fileStream.FlushAsync(cancellationToken);
            // Explicitly dispose before attempting to move so Windows unlocks the handle.
            await fileStream.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            // Move partial to final if not canceled. If another process already put it there, discard ours.
            if (!File.Exists(modelPath))
            {
                const int maxMoveAttempts = 5;
                for (int attempt = 1; attempt <= maxMoveAttempts; attempt++)
                {
                    try
                    {
                        File.Move(partialPath, modelPath, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < maxMoveAttempts)
                    {
                        await Task.Delay(150, cancellationToken); // brief backoff for transient locks
                    }
                }
            }
            else
            {
                try { File.Delete(partialPath); } catch { }
            }

            status?.Invoke("Model downloaded successfully.");
            return modelPath;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                    status?.Invoke("Download cancelled. Removed partial file.");
                }
            }
            catch { }
            throw; // rethrow for caller to handle
        }
        catch (Exception ex)
        {
            status?.Invoke($"Model download failed: {ex.Message}");
            throw;
        }
        finally
        {
            // On any failure ensure partial is cleaned
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        }
    }

    /// <summary>
    /// Initialize the Whisper transcription engine with the specified language
    /// </summary>
    public async Task InitializeAsync(string language, CancellationToken cancellationToken = default)
    {
        if (_whisperProcessor != null && _currentLanguage == language)
            return; // Already initialized with the same language
            
        // Clean up any existing resources
        DisposeResources();
        
        _currentLanguage = language;
        
        var modelName = $"ggml-{_modelType}.bin";
        var modelPath = Path.GetFullPath(Path.Combine(_modelDirectory, modelName));

        // Create the models directory if it doesn't exist
        if (!Directory.Exists(Path.GetDirectoryName(modelPath)))
        {
            StatusChanged?.Invoke($"Creating directory for Whisper model: {Path.GetDirectoryName(modelPath)}");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        }

        // Download the model if it doesn't exist
        if (!File.Exists(modelPath))
        {
            await EnsureModelDownloadedAsync(_modelType, StatusChanged, cancellationToken);
        }

        /*LogProvider.AddConsoleLogging(minLevel: WhisperLogLevel.Debug);
        LogProvider.AddLogger((level, message) =>
        {
            StatusChanged?.Invoke($"[Whisper Log][{level}]: {message}");
        });*/
        // Initialize the Whisper model
        _whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions() { UseGpu = true });
        _whisperProcessor = _whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Environment.ProcessorCount)
            .Build();
        
        StatusChanged?.Invoke($"Whisper model initialized with language: {language}");
    }

    /// <summary>
    /// Transcribe raw audio data
    /// </summary>
    public async Task TranscribeAudioAsync(
        byte[] audioData, 
        int sampleRate = 16000, 
        int bitsPerSample = 16, 
        int channels = 1, 
        CancellationToken cancellationToken = default)
    {
        if (_whisperProcessor == null)
            throw new InvalidOperationException("Whisper transcription service not initialized. Call InitializeAsync first.");

        if (audioData.Length == 0 || IsAllZeros(audioData))
        {
            StatusChanged?.Invoke($"No valid audio data provided - length: {audioData.Length} or IsAllZeros - skipping transcription.");
        }
        // Convert raw PCM data to WAV format
        using var stream = new MemoryStream();
        await using var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
        
        // Write the raw PCM data directly
        writer.Write(audioData, 0, audioData.Length);
        writer.Flush();
        
        // Reset stream position for reading
        stream.Position = 0;
        // Process with Whisper
        await foreach (var whisperResult in _whisperProcessor.ProcessAsync(stream, cancellationToken))
        {
            if (IsEmptyOrSound(whisperResult.Text))
            {
                StatusChanged?.Invoke($"Skipping empty or sound effect transcription: '{whisperResult.Text}'");
                continue;
            }
            TranscriptionReceived?.Invoke(new TranscriptionResult(
                whisperResult.Text,
                "audio",
                DateTime.UtcNow
            ));
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
    /// Check if text is empty or just represents sound effects
    /// </summary>
    private static bool IsEmptyOrSound(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
            
        text = text.Trim();
        
        // Sound effects are typically wrapped in square brackets
        if (text.StartsWith('[') && text.EndsWith(']'))
            return true;
            
        return false;
    }
    
    private void DisposeResources()
    {
        _whisperProcessor?.DisposeAsync().AsTask().Wait();
        _whisperProcessor = null;
        _whisperFactory?.Dispose();
        _whisperFactory = null;
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        DisposeResources();
    }
}
