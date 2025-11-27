using System;


namespace AvaloniaApp.Services.MicSessionMonitor
{
    public interface IMicrophoneSessionMonitor
    {
        /// <summary>
        /// Raised when a specific process started/stopped using the microphone.
        /// </summary>
        event Action<int, string, bool>? ProcessMicrophoneUsageChanged;

        /// <summary>
        /// Start monitoring microphone usage.
        /// </summary>
        void Start();

        /// <summary>
        /// Stop monitoring.
        /// </summary>
        void Stop();
    }
}