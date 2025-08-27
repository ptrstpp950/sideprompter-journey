using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenAI.Chat;
using Whisper.net;
using Whisper.net.Ggml;

namespace AvaloniaApp;

public class AudioTranscriptionService
{
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ChatCompletionService _chatCompletionService;

    public event Action<string>? MessageGenerated;

    public AudioTranscriptionService()
    {
        // TODO: Move configuration to a more appropriate place
        var endpoint = "https://openai-ptsp.openai.azure.com/";
        var deploymentName = "gpt-5-nano";
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "NO_API_KEY";
        _chatCompletionService = new ChatCompletionService(endpoint, apiKey, deploymentName);
    }

    private static bool IsEmptyOrSound(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
            return true;
        return text.Length == 0;
    }

    public async Task StartProcessing(string language)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            await RunTranscriptionLoop(_cancellationTokenSource.Token, language);
        }
        catch (Exception ex)
        {
            MessageGenerated?.Invoke($"[e] Error: {ex.Message}");
        }
    }

    public void StopProcessing()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task RunTranscriptionLoop(CancellationToken cancellationToken, string lang)
    {
        
        // 1. Whisper Model Loading
        var type = GgmlType.Tiny;
        var modelName = "ggml-" + type + ".bin";
        var modelPath = Path.GetFullPath(Path.Combine("./models", modelName));

        var messages = _chatCompletionService.Initialize();

        if (!Directory.Exists(Path.GetDirectoryName(modelPath)))
        {
            MessageGenerated?.Invoke($"Creating directory for Whisper model: {Path.GetDirectoryName(modelPath)}");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        }

        if (!File.Exists(modelPath))
        {
            MessageGenerated?.Invoke($"Downloading Whisper model '{modelName}' to '{modelPath}' ...");
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, cancellationToken: cancellationToken);
            await using var fileStream = File.Create(modelPath);
            await modelStream.CopyToAsync(fileStream, cancellationToken);
            MessageGenerated?.Invoke("Model downloaded.");
        }

        using var whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions() { UseGpu = true });
        await using var processor = whisperFactory.CreateBuilder()
            .WithLanguage(lang)
            .Build();

        // 2. Audio Capture (split mic and speaker)
        using var micCapture = new WaveInEvent();
        micCapture.WaveFormat = new WaveFormat(16000, 16, 1);

        using var speakerCapture = new WasapiLoopbackCapture();
        speakerCapture.WaveFormat = new WaveFormat(16000, 16, 1);

        var micProvider = new BufferedWaveProvider(micCapture.WaveFormat) { DiscardOnBufferOverflow = true };
        micProvider.DiscardOnBufferOverflow = true;
        micCapture.DataAvailable += (s, e) => micProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

        var speakerProvider = new BufferedWaveProvider(speakerCapture.WaveFormat) { DiscardOnBufferOverflow = true };
        speakerProvider.DiscardOnBufferOverflow = true;
        speakerCapture.DataAvailable += (s, e) => speakerProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

        var micSampler = micProvider.ToSampleProvider();
        if (micSampler.WaveFormat.Channels > 1)
        {
            micSampler = new StereoToMonoSampleProvider(micSampler);
        }

        var speakerSampler = speakerProvider.ToSampleProvider();
        if (speakerSampler.WaveFormat.Channels > 1)
        {
            speakerSampler = new StereoToMonoSampleProvider(speakerSampler);
        }

        micCapture.StartRecording();
        speakerCapture.StartRecording();

        MessageGenerated?.Invoke($"Starting transcription in language '{lang}' (mic/speaker split)...");

        var bufferSize = 16000 * 10;
        var micBuffer = new float[bufferSize];
        var micOffset = 0;
        var speakerBuffer = new float[bufferSize];
        var speakerOffset = 0;
        var processed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Action<string> log = s =>
                {
                    MessageGenerated?.Invoke(s);
                };
                var micResultTask = ReadFromSource(micSampler, "[m]", micBuffer, bufferSize, processor,
                    cancellationToken, messages, micOffset, log);
                var speakerResultTask = ReadFromSource(speakerSampler, "[o]", speakerBuffer, bufferSize, processor,
                    cancellationToken, messages, speakerOffset, log);

                await Task.WhenAll(micResultTask, speakerResultTask);
                var micResult = micResultTask.Result;
                var speakerResult = speakerResultTask.Result;
                micOffset = micResult.StreamOffset;
                speakerOffset = speakerResult.StreamOffset;

                if (messages.Count == 0 || (messages.Count - processed) < 10)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                processed = messages.Count;

                _ = Task.Run(async () =>
                {
                    MessageGenerated?.Invoke("[s] Asking chat for questions");
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        var response = await _chatCompletionService.GetCompletionAsync(messages, timeoutCts.Token);
                        MessageGenerated?.Invoke(response);
                    }
                    catch (OperationCanceledException)
                    {
                        MessageGenerated?.Invoke("[e] Chat completion request timed out.");
                    }
                    catch (Exception ex)
                    {
                        MessageGenerated?.Invoke($"[e] Chat completion error: {ex.Message}");
                    }
                }, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }

        micCapture.StopRecording();
        speakerCapture.StopRecording();
    }

    private struct StreamOffsetStruct
    {
        public int StreamRead;
        public int StreamOffset;
    }

    private static async Task<StreamOffsetStruct> ReadFromSource(
             ISampleProvider sampler,
             string prefix,
             float[] buffer, int bufferSize, WhisperProcessor processor,
             CancellationToken cancellationTokenSource,
             IList<ChatMessage> messages, int streamOffset, Action<string> logOutput)
    {
        // MIC
        var temp = new float[16000];
        var read = sampler.Read(temp, 0, temp.Length);
        if (read <= 0)
            return new StreamOffsetStruct()
            {
                StreamOffset = streamOffset,
                StreamRead = read
            };

        Array.Copy(temp, 0, buffer, streamOffset, read);
        streamOffset += read;
        if (streamOffset < bufferSize)
            return new StreamOffsetStruct()
            {
                StreamOffset = streamOffset,
                StreamRead = read
            };
        
        var stream = new MemoryStream();
        var writer = new WaveFileWriter(stream, new WaveFormat(16000, 16, 1));
        for (var i = 0; i < streamOffset; i++)
        {
            var pcm = (short)(buffer[i] * 32767);
            writer.WriteByte((byte)(pcm & 0xFF));
            writer.WriteByte((byte)((pcm >> 8) & 0xFF));
        }

        writer.Flush();
        stream.Position = 0;

        await foreach (var result in processor.ProcessAsync(stream,
                           cancellationTokenSource))
        {
            if (IsEmptyOrSound(result.Text))
                continue;

            var msg = $"{prefix} {result.Text}";
            // Only add if not a duplicate of the last message
            if (messages.Count != 0 && messages[^1] is UserChatMessage last &&
                last.Content[0].Text.EndsWith(result.Text)) continue;
            messages.Add(new UserChatMessage(msg));
            logOutput(msg);

        }

        await writer.DisposeAsync();
        await stream.DisposeAsync();
        streamOffset = 0;

        return new StreamOffsetStruct()
        {
            StreamOffset = streamOffset,
            StreamRead = read
        };
    }
}