using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Logging;
using NAudio.Wave;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.Logger;

namespace AvaloniaApp.Services.TranscriptionService;

/// <summary>
/// Implementation of ITranscriptionService using Whisper.net
/// </summary>
public class WhisperTranscriptionService : ITranscriptionService
{
    private static SemaphoreSlim InitSemaphore = new SemaphoreSlim(1, 1);
    private static SemaphoreSlim ProcessWhisperSemaphore = new SemaphoreSlim(1, 1);
    private static string CurrentLanguage = "en";
    private static WhisperProcessor? WhisperProcessor;
    private static WhisperFactory? WhisperFactoryInstance;
    
    private readonly ILogger _logger;
    
    
    private bool _disposed;
    private readonly GgmlType _modelType;
    private readonly string _modelDirectory;
    /// <summary>
    /// Whether to attempt using GPU acceleration when creating the Whisper factory.
    /// Default is false to avoid native crashes on systems without proper GPU support.
    /// </summary>
    public bool UseGpu { get; set; } = false;

    // Chunking and VAD configuration (tunable)
    public double ChunkDurationSeconds { get; set; } = 20.0; // 10-15s recommended, default 12s
    public double ChunkOverlapSeconds { get; set; } = 1.5;    // 1-2s overlap, default 1.5s
    public bool EnableVad { get; set; } = true;               // Energy-based VAD for intelligent cuts
    public double VadFrameDurationMs { get; set; } = 30.0;    // Typical 20-30ms frame
    public double VadMinSilenceMs { get; set; } = 200.0;      // Require >=200ms silence for a cut
    public double VadSearchWindowMs { get; set; } = 750.0;    // Search window around target boundary

    public event Action<string>? StatusChanged;
    public event Action<TranscriptionResult>? TranscriptionReceived;

    /// <summary>
    /// Create a new instance of WhisperTranscriptionService
    /// </summary>
    /// <param name="modelType">Type of Whisper model to use</param>
    // Preserve existing convenience constructor used in the codebase
    public WhisperTranscriptionService(GgmlType modelType = GgmlType.Base) : this(Serilog.Log.Logger, modelType)
    {
    }

    public WhisperTranscriptionService(ILogger logger, GgmlType modelType = GgmlType.Base)
    {
        _logger = logger ?? Serilog.Log.Logger;
        _modelType = modelType;
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // Points to ~/Library/Application Support on Mac
        _modelDirectory = Path.Combine(appSupport, "SidePrompter");
        _logger.Debug("WhisperTranscriptionService ctor: modelType={ModelType}, modelDirectory={ModelDirectory}", _modelType, _modelDirectory);
        if (!Directory.Exists(Path.GetDirectoryName(_modelDirectory)))
        {
            var dir = Path.GetDirectoryName(_modelDirectory);
            StatusChanged?.Invoke($"Creating directory for Whisper model: {dir}");
            _logger.Information("Creating directory for Whisper model: {Directory}", dir);
            Directory.CreateDirectory(dir!);
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
    /// <param name="language">Language code (e.g. "en")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task InitializeAsync(string language, CancellationToken cancellationToken = default)
    {
        await InitSemaphore.WaitAsync(cancellationToken);
        try
        {
            await InitializeNotThreadSafeAsync(language, cancellationToken);
        }
        finally
        {
            InitSemaphore.Release();
        }
    }
    private async Task InitializeNotThreadSafeAsync(string language, CancellationToken cancellationToken = default)
    {
        if (WhisperProcessor != null && CurrentLanguage == language)
            return; // Already initialized with the same language

        // Clean up any existing resources
        DisposeResources();

        CurrentLanguage = language;
        _logger.Information("Initializing Whisper model for language {Language}", language);

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
            _logger.Information("Model not found at {ModelPath}, starting download", modelPath);
            try
            {
                await EnsureModelDownloadedAsync(_modelType, (s) => { StatusChanged?.Invoke(s); _logger.Debug(s); }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download Whisper model {ModelPath}", modelPath);
                throw;
            }
        }

        /*LogProvider.AddConsoleLogging(minLevel: WhisperLogLevel.Debug);
        LogProvider.AddLogger((level, message) =>
        {
            StatusChanged?.Invoke($"[Whisper Log][{level}]: {message}");
        });*/
        // Initialize the Whisper model
        try
        {
            // Create a WhisperFactory and keep it alive for the lifetime of the processor.
            // Disposing the factory immediately can free native resources used by the processor
            // and lead to heap corruption. Use the instance-level UseGpu flag.
            WhisperFactoryInstance = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions() { UseGpu = UseGpu });
            var threadCount = Math.Max(2, Environment.ProcessorCount / 2);
            _logger.Information("Whisper thread count capped to {ThreadCount} (of {Total} available)", threadCount, Environment.ProcessorCount);
            WhisperProcessor = WhisperFactoryInstance.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(threadCount)
                .Build();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create WhisperFactory from path {ModelPath}", modelPath);
            throw;
        }


        StatusChanged?.Invoke($"Whisper model initialized with language: {language}");
        _logger.Information("Whisper model initialized for language {Language} using model {ModelPath}", language, modelPath);
    }

    /// <summary>
    /// Transcribe raw audio data
    /// </summary>
    public async Task TranscribeAudioAsync(
        byte[] audioData,
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        string source = "unknown",
        CancellationToken cancellationToken = default)
    {
        await ProcessWhisperSemaphore.WaitAsync(cancellationToken);
        try
        {
            await TranscribeAudioNotThreadSafeAsync(audioData, sampleRate, bitsPerSample, channels, source, cancellationToken);
        }   
        finally
        {
            try { ProcessWhisperSemaphore.Release(); } catch (SemaphoreFullException) { /* already released */ }
        }
    }

    public async Task TranscribeAudioNotThreadSafeAsync(
        byte[] audioData,
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        string source = "unknown",
        CancellationToken cancellationToken = default)
    {
        if (WhisperProcessor == null)
        {
            _logger.Error($"{source} TranscribeAudioAsync called before InitializeAsync");
            throw new InvalidOperationException("Whisper transcription service not initialized. Call InitializeAsync first.");
        }

        if (audioData.Length == 0 || IsAllZeros(audioData))
        {
            StatusChanged?.Invoke($"{source}: No valid audio data provided - length: {audioData.Length} or IsAllZeros - skipping transcription.");
            _logger.Debug("Source: {Source} - Skipping transcription due to empty or all-zero audio (length={Length})", source, audioData.Length);
            return;
        }

        // Validate and compute sizes
        if (sampleRate <= 0 || channels <= 0 || bitsPerSample <= 0)
            throw new ArgumentException("Invalid audio format parameters.");

        int bytesPerSample = bitsPerSample / 8;
        int frameSizeBytes = bytesPerSample * channels; // one multi-channel frame
        if (frameSizeBytes <= 0 || audioData.Length < frameSizeBytes)
        {
            StatusChanged?.Invoke(source + ": Audio data too short for processing.");
            return;
        }

        int totalFrames = audioData.Length / frameSizeBytes;
        int targetChunkFrames = (int)Math.Max(1, Math.Round(ChunkDurationSeconds * sampleRate));
        int overlapFrames = (int)Math.Max(0, Math.Round(ChunkOverlapSeconds * sampleRate));
        // Ensure overlap is less than chunk size
        overlapFrames = Math.Min(overlapFrames, Math.Max(0, targetChunkFrames - 1));

        // Prepare VAD if possible (only implemented for 16-bit PCM)
        short[]? monoPcm = null;
        bool vadAvailable = EnableVad && bitsPerSample == 16;
        bool[]? speechMask = null;
        List<(int start, int end)>? silenceSegments = null; // in frame indices
        int vadFrameSize = 0; // in frames
        int minSilenceFrames = 0;
        int searchWindowFrames = 0;

        if (vadAvailable)
        {
            try
            {
                monoPcm = ToMonoInt16(audioData, channels);
                vadFrameSize = Math.Max(1, (int)Math.Round(sampleRate * (VadFrameDurationMs / 1000.0)));
                minSilenceFrames = Math.Max(1, (int)Math.Round((VadMinSilenceMs / 1000.0) * sampleRate / vadFrameSize));
                searchWindowFrames = Math.Max(1, (int)Math.Round((VadSearchWindowMs / 1000.0) * sampleRate / vadFrameSize));

                var rms = ComputeRmsPerFrame(monoPcm, vadFrameSize);
                var thrOn = ComputeDynamicThreshold(rms);
                var thrOff = Math.Max(thrOn * 0.6, 0.01); // hysteresis
                speechMask = BuildSpeechMask(rms, thrOn, thrOff);
                silenceSegments = BuildSilenceSegments(speechMask, minSilenceFrames);
                _logger.Debug("{source} VAD enabled: vadFrameSize={VadFrameSize}, minSilenceFrames={MinSilence}, searchWindowFrames={SearchWindow}", source, vadFrameSize, minSilenceFrames, searchWindowFrames);
            }
            catch (Exception ex)
            {
                vadAvailable = false;
                StatusChanged?.Invoke($"{source}: VAD disabled due to error: {ex.Message}");
                _logger.Warning(ex, "{Source} VAD disabled due to error", source);
            }
        }

        // Iterate chunks with overlap and optional VAD cut adjustment
        int startFrame = 0;
        int chunkIndex = 0;
        while (startFrame < totalFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int desiredEnd = startFrame + targetChunkFrames;
            int endFrame;
            if (desiredEnd >= totalFrames)
            {
                endFrame = totalFrames;
            }
            else if (vadAvailable && silenceSegments != null && speechMask != null)
            {
                // Find a silence-centered cut near desired end
                endFrame = FindSilenceCutNear(desiredEnd, silenceSegments, vadFrameSize, searchWindowFrames, totalFrames);
                // Ensure progress: never go backward or too close to start
                if (endFrame <= startFrame + 1)
                    endFrame = Math.Min(totalFrames, startFrame + targetChunkFrames);
            }
            else
            {
                endFrame = desiredEnd;
            }

            int startByte = startFrame * frameSizeBytes;
            int endByteExclusive = Math.Min(audioData.Length, endFrame * frameSizeBytes);
            int byteCount = Math.Max(0, endByteExclusive - startByte);
            if (byteCount <= 0)
                break;

            StatusChanged?.Invoke($"{source}: Processing chunk {++chunkIndex}: frames {startFrame}..{endFrame} ({(double)(endFrame - startFrame) / sampleRate:0.00}s)");
            _logger.Debug("{Source} Processing chunk {ChunkIndex}: frames {Start}..{End} duration={Seconds}s", source, chunkIndex, startFrame, endFrame, (double)(endFrame - startFrame) / sampleRate);

            await using var stream = new MemoryStream();
            await using var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
            writer.Write(audioData, startByte, byteCount);
            writer.Flush();
            stream.Position = 0;

            try
            {
                await foreach (var whisperResult in WhisperProcessor.ProcessAsync(stream, cancellationToken))
                {
                    if (IsEmptyOrSound(whisperResult.Text))
                    {
                        StatusChanged?.Invoke($"Skipping empty or sound effect transcription: '{whisperResult.Text}'");
                        _logger.Debug("Skipped transcription result: {Text}", whisperResult.Text);
                        continue;
                    }
                    _logger.Information("{Source} Transcription chunk result: {Text}", source, whisperResult.Text);
                    TranscriptionReceived?.Invoke(new TranscriptionResult(
                        whisperResult.Text,
                        "audio",
                        DateTime.UtcNow
                    ));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Log and surface the error but continue processing other chunks
                _logger.Error(ex, "{Source} Error while processing audio chunk with Whisper", source);
                StatusChanged?.Invoke($"{source}: Whisper processing error: {ex.Message}");
            }


            if (endFrame >= totalFrames)
                break;

            // Move start forward with overlap
            startFrame = Math.Max(endFrame - overlapFrames, startFrame + 1);
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
        bool acquired = false;
        try
        {
            _logger.Debug("Disposing Whisper resources");
            // Ensure no processing is in-flight before disposing native resources
            ProcessWhisperSemaphore.Wait();
            acquired = true;

            try
            {
                WhisperProcessor?.DisposeAsync().AsTask().Wait();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error while disposing WhisperProcessor");
            }

            try
            {
                WhisperFactoryInstance?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error while disposing WhisperFactoryInstance");
            }

            WhisperProcessor = null;
            WhisperFactoryInstance = null;
        }
        finally
        {
            if (acquired)
            {
                try { ProcessWhisperSemaphore.Release(); } catch { }
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeResources();
    }

    // ---- VAD and chunk helpers ----

    private static short[] ToMonoInt16(byte[] pcm, int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (pcm.Length % (2 * channels) != 0) throw new ArgumentException("PCM buffer length is not aligned to 16-bit frames.");

        int totalFrames = pcm.Length / (2 * channels);
        var mono = new short[totalFrames];
        // Little-endian 16-bit signed
        for (int f = 0; f < totalFrames; f++)
        {
            int baseIdx = f * 2 * channels;
            int acc = 0;
            for (int c = 0; c < channels; c++)
            {
                int lo = pcm[baseIdx + (c * 2) + 0];
                int hi = pcm[baseIdx + (c * 2) + 1];
                short sample = (short)(lo | (hi << 8));
                acc += sample;
            }
            mono[f] = (short)(acc / channels);
        }
        return mono;
    }

    private static double[] ComputeRmsPerFrame(short[] mono, int frameSizeSamples)
    {
        int totalFrames = (int)Math.Ceiling(mono.Length / (double)frameSizeSamples);
        var rms = new double[totalFrames];
        int idx = 0;
        for (int i = 0; i < totalFrames; i++)
        {
            long sumSq = 0;
            int count = 0;
            for (int j = 0; j < frameSizeSamples && idx < mono.Length; j++, idx++)
            {
                int s = mono[idx];
                sumSq += (long)s * s;
                count++;
            }
            double meanSq = count > 0 ? (double)sumSq / count : 0.0;
            // Normalize to 0..1 range for 16-bit PCM
            rms[i] = Math.Sqrt(meanSq) / 32768.0;
        }
        return rms;
    }

    private static double ComputeDynamicThreshold(double[] rms)
    {
        if (rms.Length == 0) return 0.02; // fallback
        // Use 20th percentile as noise floor and scale
        var copy = new double[rms.Length];
        Array.Copy(rms, copy, rms.Length);
        Array.Sort(copy);
        int idx = (int)Math.Floor(0.20 * (copy.Length - 1));
        double noise = Math.Max(1e-6, copy[Math.Clamp(idx, 0, copy.Length - 1)]);
        return Math.Max(0.02, noise * 3.0);
    }

    private static bool[] BuildSpeechMask(double[] rms, double thrOn, double thrOff)
    {
        var speech = new bool[rms.Length];
        bool inSpeech = false;
        for (int i = 0; i < rms.Length; i++)
        {
            double v = rms[i];
            if (!inSpeech)
            {
                if (v >= thrOn) inSpeech = true;
            }
            else
            {
                if (v <= thrOff) inSpeech = false;
            }
            speech[i] = inSpeech;
        }
        return speech;
    }

    private static List<(int start, int end)> BuildSilenceSegments(bool[] speechMask, int minSilenceFrames)
    {
        var segments = new List<(int start, int end)>();
        int start = -1;
        for (int i = 0; i < speechMask.Length; i++)
        {
            if (!speechMask[i])
            {
                if (start == -1) start = i;
            }
            else
            {
                if (start != -1)
                {
                    int end = i; // exclusive
                    if (end - start >= minSilenceFrames)
                        segments.Add((start, end));
                    start = -1;
                }
            }
        }
        if (start != -1)
        {
            int end = speechMask.Length;
            if (end - start >= minSilenceFrames)
                segments.Add((start, end));
        }
        return segments;
    }

    private static int FindSilenceCutNear(int desiredFrame /* in mono frames */,
                                          List<(int start, int end)> silenceSegments,
                                          int vadFrameSize /* samples per VAD frame == frames of mono samples */,
                                          int searchWindowFrames /* in VAD frames */,
                                          int totalFrames /* total mono frames */)
    {
        // Map desired frame to VAD frame index
        int desiredVadFrame = Math.Clamp(desiredFrame / vadFrameSize, 0, int.MaxValue);
        int winStart = Math.Max(0, desiredVadFrame - searchWindowFrames);
        int winEnd = desiredVadFrame + searchWindowFrames;

        int bestCutVadFrame = -1;
        int bestDist = int.MaxValue;
        foreach (var (s, e) in silenceSegments)
        {
            // If segment intersects the search window
            if (e < winStart || s > winEnd) continue;
            int center = s + (e - s) / 2;
            int dist = Math.Abs(center - desiredVadFrame);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestCutVadFrame = center;
            }
        }

        int cutFrame;
        if (bestCutVadFrame >= 0)
        {
            cutFrame = bestCutVadFrame * vadFrameSize;
        }
        else
        {
            cutFrame = desiredFrame; // fallback to desired
        }
        return Math.Clamp(cutFrame, 0, totalFrames);
    }
}
