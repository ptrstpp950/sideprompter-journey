using System;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Services.AudioTranscription.Helpers;
using AvaloniaApp.Services.TranscriptionService;

namespace AvaloniaApp.Services.AudioTranscription
{
    public class AudioTranscriptionServiceMac : IAudioTranscriptionService
    {
        private readonly IAudioTranscriptionService _micAudioTranscriptionService;
        private readonly IAudioTranscriptionService _speakerAudioTranscriptionService;
        public AudioTranscriptionServiceMac(ITranscriptionService transcriptionService)
        {
            _micAudioTranscriptionService = new MicrophoneTranscriptionService(transcriptionService);
            _micAudioTranscriptionService.TranscriptionReceived += (msg) => TranscriptionReceived?.Invoke(msg);
            _micAudioTranscriptionService.LogReceived += (log) => LogReceived?.Invoke(log);
            _micAudioTranscriptionService.StatusChanged += (status) => StatusChanged?.Invoke(status);

            _speakerAudioTranscriptionService = new AudioTranscriptionServiceMac(transcriptionService);
            _speakerAudioTranscriptionService.TranscriptionReceived += (msg) => TranscriptionReceived?.Invoke(msg);
            _speakerAudioTranscriptionService.LogReceived += (log) => LogReceived?.Invoke(log);
            _speakerAudioTranscriptionService.StatusChanged += (status) => StatusChanged?.Invoke(status);

        }
        public event Action<TranscriptionMessage>? TranscriptionReceived;
        public event Action<LogMessage>? LogReceived;
        public event Action<string>? StatusChanged;

        public bool IsRunning => _micAudioTranscriptionService.IsRunning || _speakerAudioTranscriptionService.IsRunning;

        public Task StartProcessing(string language = "en", CancellationToken cancellationToken = default)
        {
            var micTask = _micAudioTranscriptionService.StartProcessing(language, cancellationToken);
            var speakerTask = _speakerAudioTranscriptionService.StartProcessing(language, cancellationToken);
            return Task.WhenAll(micTask, speakerTask);
        }

        public Task StopProcessing()
        {
            var micTask = _micAudioTranscriptionService.StopProcessing();
            var speakerTask = _speakerAudioTranscriptionService.StopProcessing();
            return Task.WhenAll(micTask, speakerTask);
        }

        public void Dispose()
        {
            _micAudioTranscriptionService.Dispose();
            _speakerAudioTranscriptionService.Dispose();
        }
    }
}
