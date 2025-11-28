using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AvaloniaApp.Services.MicSessionMonitor
{
    public class MicrophoneSessionMonitorMac : IMicrophoneSessionMonitor, IDisposable
    {
        public event Action<int, string, bool>? ProcessMicrophoneUsageChanged;

        private readonly ILogger _logger;
        private Process? _process;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly object _lock = new();

        public MicrophoneSessionMonitorMac(ILogger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            EnsureNotDisposed();
            lock (_lock)
            {
                if (_process != null && !_process.HasExited)
                    return;

                StartProcess();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopProcess();
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MicrophoneSessionMonitorMac));
        }

        private void StartProcess()
        {
            _cts = new CancellationTokenSource();

            try
            {
                var binary = GetBinaryPath();

                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.Exited += OnProcessExited;

                if (!_process.Start())
                {
                    _process = null;
                    throw new InvalidOperationException("Failed to start microphoneMonitor process");
                }

                // Read stdout for events
                _ = Task.Run(() => ReadStdoutLoopAsync(_process, _cts.Token), _cts.Token);
                
                // Read stderr for logs (optional, but good for debugging)
                _ = Task.Run(() => ReadStderrLoopAsync(_process, _cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MicrophoneSessionMonitorMac: failed to start process");
                throw;
            }
        }

        private void StopProcess()
        {
            _cts?.Cancel();
            
            if (_process != null)
            {
                _process.Exited -= OnProcessExited;
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit(1000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error killing microphoneMonitor");
                }
                _process.Dispose();
                _process = null;
            }

            _cts?.Dispose();
            _cts = null;
        }

        private string GetBinaryPath()
        {
            // Look for bundled app resources first (app bundle layout)
            if (OperatingSystem.IsMacOS())
            {
                var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "..", "Resources", "libs", "microphoneMonitor", "microphoneMonitor");
                if (File.Exists(resourcesPath))
                    return resourcesPath;
            }

            // Try base directory libs
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var libsPath = Path.Combine(baseDir, "libs", "microphoneMonitor", "microphoneMonitor");
            if (File.Exists(libsPath))
                return libsPath;

            // Try current working directory layout (development)
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "libs", "microphoneMonitor", "microphoneMonitor");
            if (File.Exists(cwdPath))
                return cwdPath;

            // Last resort: rely on PATH
            return "microphoneMonitor";
        }

        private async Task ReadStdoutLoopAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                var reader = process.StandardOutput;
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(line))
                        continue;

                    HandleOutputLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, "MicrophoneSessionMonitorMac stdout error");
            }
        }

        private async Task ReadStderrLoopAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                var reader = process.StandardError;
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.Debug("microphoneMonitor log: {Line}", line);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, "MicrophoneSessionMonitorMac stderr error");
            }
        }

        private void HandleOutputLine(string line)
        {
            try
            {
                // Expected format: {"type": "event", "pid": 123, "process": "Zoom", "active": true}
                var eventData = JsonSerializer.Deserialize<MicEvent>(line);
                if (eventData != null && eventData.Type == "event")
                {
                    ProcessMicrophoneUsageChanged?.Invoke(eventData.Pid, eventData.Process, eventData.Active);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MicrophoneSessionMonitorMac parse error. Line: {Line}", line);
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            _logger.Information("microphoneMonitor process exited");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            StopProcess();
        }

        private class MicEvent
        {
            [System.Text.Json.Serialization.JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("pid")]
            public int Pid { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("process")]
            public string Process { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("active")]
            public bool Active { get; set; }
        }
    }
}
