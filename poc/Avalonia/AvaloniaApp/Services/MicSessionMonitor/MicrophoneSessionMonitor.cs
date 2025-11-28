using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Serilog;


namespace AvaloniaApp.Services.MicSessionMonitor
{
    /// <summary>
    /// Monitors whether the microphone is currently in use by the system or by individual processes.
    /// On Windows this uses NAudio/MMDevice APIs to inspect active audio sessions.
    /// On other platforms this class currently provides a no-op implementation.
    ///
    /// Usage: create an instance, subscribe to <see cref="MicrophoneInUseChanged"/> and call <see cref="Start"/>.
    /// </summary>
    public class MicrophoneSessionMonitorWin : IMicrophoneSessionMonitor, IDisposable
    {
        /// <summary>
        /// Raised when a specific process started/stopped using the microphone.
        /// </summary>
        public event Action<int, string, bool>? ProcessMicrophoneUsageChanged;

        private bool _isDisposed;

        private Timer? _pollTimer;
        private readonly Dictionary<int, string> _reportedProcesses = new();

        private bool _isRunning;
        private readonly ILogger _logger;
        private byte _pollInProgress;

        public MicrophoneSessionMonitorWin(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Start monitoring microphone usage.
        /// </summary>
        public void Start()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MicrophoneSessionMonitorWin));
            if (_isRunning) return;

            try
            {
                // Start a small timer to poll audio peak value
                _pollTimer = new Timer(PollAudioPeak, null, 1000, 5000);
            }
            catch
            {
                // swallow - best-effort
            }

            _isRunning = true;
        }

        public Dictionary<int, string> GetProcessThatUsesMicrophone()
        {
            var processNames = new Dictionary<int, string>();
            var processIds = new List<int>();
            try
            {
                // Create a local enumerator and device to avoid sharing COM objects across threads
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? micDevice;
                try
                {
                    micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to get default capture device: {Message}", ex.Message);
                    return processNames;
                }

                if (micDevice == null)
                {
                    _logger.Debug("No capture device found.");
                    return processNames;
                }

                SessionCollection sessions;
                try
                {
                    sessions = micDevice.AudioSessionManager.Sessions;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to get Sessions collection from device: {Message}", ex.Message);
                    return processNames;
                }

                // Diagnostic info so we can see environment when enumerating
                try
                {
                    _logger.Debug(
                        "Enumerating audio sessions. Count={Count}, ThreadId={ThreadId}, Apartment={Apartment}",
                        sessions.Count,
                        Thread.CurrentThread.ManagedThreadId,
                        Thread.CurrentThread.GetApartmentState());
                }
                catch (Exception)
                {
                    // swallow
                }

                for (var i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        try
                        {
                            if (session.State == AudioSessionState.AudioSessionStateActive)
                            {
                                var processId = (int)session.GetProcessID;
                                if(processId != Environment.ProcessId)
                                    processIds.Add(processId);
                            }

                            Debug.WriteLine($"Existing session: {session.DisplayName}, state: {session.State}, pid: {session.GetProcessID}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Error reading session properties at index {Index}: {Message}", i, ex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Error getting session at index {Index}: {Message}", i, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error getting processes that use microphone: {ErrorMessage}", ex.Message);
            }

            return processIds.ToDictionary(id => id, id => Process.GetProcessById(id).ProcessName);

        }

        /// <summary>
        /// Stop monitoring.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _pollTimer?.Dispose();
            }
            catch
            {
                // swallow
            }
            _pollTimer = null;
            _reportedProcesses.Clear();

            _isRunning = false;
        }


        private void PollAudioPeak(object? state)
        {
            // Prevent reentrant execution of the poll handler
            if (Interlocked.Exchange(ref _pollInProgress, 1) == 1)
                return;

            try
            {
                var processesUsingMic = GetProcessThatUsesMicrophone();

                foreach (var proc in processesUsingMic)
                {
                    if (!_reportedProcesses.ContainsKey(proc.Key))
                    {
                        _reportedProcesses[proc.Key] = proc.Value;
                        ProcessMicrophoneUsageChanged?.Invoke(proc.Key, proc.Value, true);
                    }
                }

                foreach (var kv in _reportedProcesses.ToArray())
                {
                    if (!processesUsingMic.ContainsKey(kv.Key))
                    {
                        ProcessMicrophoneUsageChanged?.Invoke(kv.Key, kv.Value, false);
                        _reportedProcesses.Remove(kv.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error polling microphone peak: {ErrorMessage}", ex.Message);
            }
            finally
            {
                // Ensure the flag is always cleared so future polls can run
                try
                {
                    Interlocked.Exchange(ref _pollInProgress, 0);
                }
                catch
                {
                    // swallow
                }
            }
        }

        
        public void Dispose()
        {
            if (_isDisposed) return;
            Stop();

            _isDisposed = true;
        }
    }
}