using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;
using Deepgram;
using Deepgram.Models.Listen.v2.WebSocket;
using Deepgram.Clients.Interfaces.v2;
using DotNetEnv;

namespace AvaloniaApp.Services.TranscriptionService;

/// <summary>
/// Deepgram implementation of <see cref="ITranscriptionService"/> using WebSocket streaming.
/// 
/// Requirements:
/// - NuGet: Deepgram (official SDK)
/// - Environment variable DEEPGRAM_API_KEY must be set before use.
/// 
/// Notes:
/// - InitializeAsync connects a live WebSocket session with the configured model.
/// - TranscribeAudioAsync will send the provided PCM audio bytes to the live session
///   and returns any transcripts received within a small timeout window. For ongoing
///   live usage, prefer calling Send on captured audio chunks frequently.
/// </summary>
public sealed class DeepgramTranscriptionService : ITranscriptionService
{
    private readonly string _model;
    private readonly TimeSpan _resultFlushDelay;
    private IListenWebSocketClient? _client;
    private readonly ConcurrentQueue<TranscriptionResult> _results = new();
    private volatile bool _connected;
    private string _language = "en";

    public event Action<string>? StatusChanged;

    /// <summary>
    /// Creates a Deepgram-based transcription service.
    /// </summary>
    /// <param name="model">Deepgram model, e.g. "nova-3"</param>
    /// <param name="resultFlushDelay">How long to wait for results after sending audio in TranscribeAudioAsync.</param>
    public DeepgramTranscriptionService(string model = "nova-2", TimeSpan? resultFlushDelay = null)
    {
        _model = model;
        _resultFlushDelay = resultFlushDelay ?? TimeSpan.FromMilliseconds(300);
    }

    public async Task InitializeAsync(string language, CancellationToken cancellationToken = default)
    {
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;

    // Initialize library with default logging
    Library.Initialize();

        // Load API key from environment or .env
        try { Env.TraversePath().Load(); } catch { /* ignore if no .env */ }
        var apiKey = Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusChanged?.Invoke("Deepgram API key missing. Set DEEPGRAM_API_KEY environment variable or create a .env file.");
            throw new InvalidOperationException("Missing DEEPGRAM_API_KEY.");
        }

        // Create WebSocket client (SDK uses DEEPGRAM_API_KEY from environment)
        _client = ClientFactory.CreateListenWebSocketClient();

        // Subscribe to transcription results
        await _client.Subscribe(new EventHandler<ErrorResponse>((sender, e) =>
        {
            StatusChanged?.Invoke($"Deepgram error: {e.Message ?? "unknown"}");
        }));
        await _client.Subscribe(new EventHandler<ResultResponse>((sender, e) =>
        {
            try
            {
                var alt = e.Channel?.Alternatives?.FirstOrDefault();
                var text = alt?.Transcript;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _results.Enqueue(new TranscriptionResult(
                        text: text!,
                        source: "mic",
                        timestamp: DateTime.UtcNow,
                        metadata: new Dictionary<string, object>
                        {
                            ["confidence"] = alt?.Confidence ?? 0,
                            ["is_final"] = e?.IsFinal ?? false
                        }));
                }
            }
            catch
            {
                // Swallow parsing issues; keep stream alive
            }
        }));

        // Connect to Deepgram
        var schema = new LiveSchema
        {
            Model = _model,
            // Language per API docs; set as available for model
            Language = _language,
            // Expect 16-bit linear PCM
            Encoding = "linear16",
            SampleRate = 16000,
            Channels = 1,
            InterimResults = true,
            Punctuate = true,
            SmartFormat = true,
        };

        StatusChanged?.Invoke("Connecting to Deepgram...");
        try
        {
            await _client.Connect(schema);
            _connected = true;
            StatusChanged?.Invoke("Deepgram connected");
        }
        catch (Exception ex)
        {
            _connected = false;
            _client = null;
            var hint = "Check DEEPGRAM_API_KEY, model, and audio params (encoding=linear16, sample_rate=16000, channels=1).";
            StatusChanged?.Invoke($"Deepgram connection failed: {ex.Message}. {hint}");
            throw;
        }
    }

    public async Task<TranscriptionResult[]> TranscribeAudioAsync(
        byte[] audioData,
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || !_connected)
            throw new InvalidOperationException("DeepgramTranscriptionService not initialized. Call InitializeAsync first.");

        if (bitsPerSample != 16)
            throw new ArgumentException("Deepgram streaming expects 16-bit PCM (linear16).", nameof(bitsPerSample));

        // Send audio bytes. Deepgram expects raw PCM for encoding=linear16
        _client.Send(audioData);

        // Give the service some time to produce interim/final results for this chunk
        var delay = _resultFlushDelay;
        if (sampleRate != 16000 || channels != 1)
        {
            // If format differs, still send but wait a bit longer for processing
            delay += TimeSpan.FromMilliseconds(200);
        }

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // return whatever is queued so far
        }

        // Drain queue to array
        var list = new List<TranscriptionResult>();
        while (_results.TryDequeue(out var item))
        {
            list.Add(item);
        }
        return list.ToArray();
    }

    public void Dispose()
    {
        try
        {
            if (_client != null)
            {
                // Stop closes the socket server-side; ignore exceptions
                _client.Stop().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _connected = false;
            _client = null;
            StatusChanged?.Invoke("Deepgram disconnected");
        }
    }
}
