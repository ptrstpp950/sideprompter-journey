using System;
using System.Threading.Tasks;
using AvaloniaApp.Services.TranscriptionService;
using Whisper.net.Ggml;

namespace AvaloniaApp
{
    // Example usage of the refactored services
    public class TranscriptionServiceExample
    {
        public static async Task RunExampleAsync()
        {
            // 1. Create the Whisper transcription service
            using var transcriptionService = new WhisperTranscriptionService(
                GgmlType.Tiny, // Model type
                "./models"     // Model directory
            );

            // 2. Create microphone options (optional)
            var micOptions = new MicrophoneOptions
            {
                SampleRate = 16000,
                ChunkDurationMs = 100
            };

            // 3. Create the microphone transcription service with injected transcription service
            using var microphoneService = new MicrophoneTranscriptionService(
                transcriptionService,
                micOptions
            );

            // 4. Subscribe to events
            microphoneService.TranscriptionReceived += transcription =>
            {
                Console.WriteLine($"Transcription: {transcription}");
            };

            microphoneService.StatusChanged += status =>
            {
                Console.WriteLine($"Status: {status}");
            };

            microphoneService.LogReceived += log =>
            {
                Console.WriteLine($"Log: [{log.MessageType}] {log.Message}");
            };

            // 5. Start processing
            await microphoneService.StartProcessing("en");

            // 6. Wait for user input to stop
            Console.WriteLine("Press Enter to stop recording...");
            Console.ReadLine();

            // 7. Stop processing
            await microphoneService.StopProcessing();
        }
    }
}
