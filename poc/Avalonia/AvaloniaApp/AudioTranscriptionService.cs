using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AvaloniaApp;

public class AudioTranscriptionService : IAudioTranscriptionService
{
    private readonly ITranscriptionService _transcriptionService;
    private CancellationTokenSource? _cancellationTokenSource;
    private List<string> _transcriptionHistory = new List<string>();

    public event Action<string>? TranscriptionReceived;
    public event Action<LogMessage>? LogReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _cancellationTokenSource != null;

    public AudioTranscriptionService(ITranscriptionService transcriptionService)
    {
        _transcriptionService = transcriptionService;
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

    public async Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            StatusChanged?.Invoke("Initializing transcription service...");
            
            // Initialize transcription service
            await _transcriptionService.InitializeAsync(language, _cancellationTokenSource.Token);
            
            await RunTranscriptionLoop(_cancellationTokenSource.Token, language);
        }
        catch (Exception ex)
        {
            TranscriptionReceived?.Invoke($"[Error] {ex.Message}");
        }
    }

    public Task StopProcessing()
    {
        _cancellationTokenSource?.Cancel();
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        (_transcriptionService as IDisposable)?.Dispose();
        _transcriptionHistory.Clear();
    }

    private async Task RunTranscriptionLoop(CancellationToken cancellationToken, string lang)
    {
        _transcriptionHistory.Clear();

        // Audio Capture (split mic and speaker)
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

        StatusChanged?.Invoke($"Starting transcription in language '{lang}' (mic/speaker split)...");

        var bufferSize = 16000 * 10;
        var micBuffer = new float[bufferSize];
        var micOffset = 0;
        var speakerBuffer = new float[bufferSize];
        var speakerOffset = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Action<string> log = s =>
                {
                    TranscriptionReceived?.Invoke(s);
                };
                var micResultTask = ReadFromSource(micSampler, "[m]", micBuffer, bufferSize,
                    cancellationToken, _transcriptionHistory, micOffset, log);
                var speakerResultTask = ReadFromSource(speakerSampler, "[o]", speakerBuffer, bufferSize,
                    cancellationToken, _transcriptionHistory, speakerOffset, log);

                await Task.WhenAll(micResultTask, speakerResultTask);
                var micResult = micResultTask.Result;
                var speakerResult = speakerResultTask.Result;
                micOffset = micResult.StreamOffset;
                speakerOffset = speakerResult.StreamOffset;

                await Task.Delay(100, cancellationToken);
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

    private async Task<StreamOffsetStruct> ReadFromSource(
             ISampleProvider sampler,
             string prefix,
             float[] buffer, int bufferSize,
             CancellationToken cancellationToken,
             IList<string> transcriptions, int streamOffset, Action<string> logOutput)
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

        // Convert stream to byte array for the transcription service
        byte[] audioData = stream.ToArray();
        
        // Process with the transcription service
        var results = await _transcriptionService.TranscribeAudioAsync(audioData);
        
        foreach (var result in results)
        {
            if (IsEmptyOrSound(result.Text))
                continue;

            var transcription = $"{prefix} {result.Text}";
            
            // Only add if not a duplicate of the last transcription
            if (transcriptions.Count > 0 && transcriptions[^1].EndsWith(result.Text))
                continue;
                
            transcriptions.Add(transcription);
            logOutput(transcription);
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