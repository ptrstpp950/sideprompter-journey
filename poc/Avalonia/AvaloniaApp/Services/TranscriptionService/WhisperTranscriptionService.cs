using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

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

    /// <summary>
    /// Create a new instance of WhisperTranscriptionService
    /// </summary>
    /// <param name="modelType">Type of Whisper model to use</param>
    /// <param name="modelDirectory">Directory to store the model files (defaults to "./models")</param>
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
            StatusChanged?.Invoke($"Downloading Whisper model '{modelName}' to '{modelPath}'...");
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(_modelType, cancellationToken: cancellationToken);
            await using var fileStream = File.Create(modelPath);
            await modelStream.CopyToAsync(fileStream, cancellationToken);
            StatusChanged?.Invoke("Model downloaded successfully.");
        }

        // Initialize the Whisper model
        _whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions() { UseGpu = true });
        _whisperProcessor = _whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .Build();
            
        StatusChanged?.Invoke($"Whisper model initialized with language: {language}");
    }

    /// <summary>
    /// Transcribe raw audio data
    /// </summary>
    public async Task<TranscriptionResult[]> TranscribeAudioAsync(
        byte[] audioData, 
        int sampleRate = 16000, 
        int bitsPerSample = 16, 
        int channels = 1, 
        CancellationToken cancellationToken = default)
    {
        if (_whisperProcessor == null)
            throw new InvalidOperationException("Whisper transcription service not initialized. Call InitializeAsync first.");
            
        if (audioData.Length == 0 || IsAllZeros(audioData))
            return Array.Empty<TranscriptionResult>();

        // Convert raw PCM data to WAV format
        using var stream = new MemoryStream();
        using var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
        
        // Write the raw PCM data directly
        writer.Write(audioData, 0, audioData.Length);
        writer.Flush();
        
        // Reset stream position for reading
        stream.Position = 0;

        var results = new List<TranscriptionResult>();
        
        // Process with Whisper
        await foreach (var whisperResult in _whisperProcessor.ProcessAsync(stream, cancellationToken))
        {
            if (IsEmptyOrSound(whisperResult.Text))
                continue;
                
            results.Add(new TranscriptionResult(
                whisperResult.Text,
                "audio",
                DateTime.UtcNow
            ));
        }
        
        return results.ToArray();
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
