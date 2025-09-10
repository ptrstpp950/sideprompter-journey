using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Avalonia.Input;

namespace AvaloniaApp.Services.HotKey
{
    /// <summary>
    /// macOS implementation that wraps the Swift hotkey-listener binary.
    /// It starts the native helper, reads stdout lines and invokes the actions
    /// registered via the IHotKeyService methods.
    /// </summary>
    public class HotKeyServiceMac : IHotKeyService
    {
        private Process? _process;
        private CancellationTokenSource? _cts;
        private Action? _aiHelpAction;
        private Action? _windowCaptureAction;
        private bool _disposed;

        public void RegisterAiHelpNeededHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            EnsureNotDisposed();
            _aiHelpAction = action ?? throw new ArgumentNullException(nameof(action));
            EnsureProcessStarted();
        }

        public void RegisterWindowCaptureHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            EnsureNotDisposed();
            _windowCaptureAction = action ?? throw new ArgumentNullException(nameof(action));
            EnsureProcessStarted();
        }

        public void UnregisterStartRecordingHotKey()
        {
            _aiHelpAction = null;
        }

        public void UnregisterWindowCaptureHotKey()
        {
            _windowCaptureAction = null;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotKeyServiceMac));
        }

        private void EnsureProcessStarted()
        {
            if (_process != null && !_process.HasExited)
                return;

            _cts = new CancellationTokenSource();

            try
            {
                var binary = GetHotKeyListenerBinaryPath();

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
                    throw new InvalidOperationException("Failed to start hotkey-listener process");
                }

                // Fire-and-forget readers
                _ = Task.Run(() => ReadStderrLoopAsync(_process, _cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HotKeyService: failed to start process: {ex}");
                throw;
            }
        }

        private string GetHotKeyListenerBinaryPath()
        {
            // Look for bundled app resources first (app bundle layout)
            if (OperatingSystem.IsMacOS())
            {
                var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "..", "Resources", "libs", "hotkeyListener", "hotkeyListener");
                if (File.Exists(resourcesPath))
                    return resourcesPath;
            }

            // Try base directory libs
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var libsPath = Path.Combine(baseDir, "libs", "hotkeyListener", "hotkeyListener");
            if (File.Exists(libsPath))
                return libsPath;

            // Try current working directory layout (development)
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "libs", "hotkeyListener", "hotkeyListener");
            if (File.Exists(cwdPath))
                return cwdPath;

            // Last resort: rely on PATH
            return "hotkeyListener";
        }
        private async Task ReadStderrLoopAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                var reader = process.StandardError;
                if (reader is null)
                    return;

                using var _reader = reader;

                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var line = await _reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(line))
                        continue;

                    HandleOutputLine(line);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HotKeyService stderr error: {ex}");
            }
        }

        private void HandleOutputLine(string line)
        {
            if (!line.Contains("[Event]"))
            {
                Debug.WriteLine($"hotkey-listener debug info: {line}");
                return;
            }
            else
            {
                Debug.WriteLine($"hotkey-listener processing event: {line}");
            }
            // The provided Swift sample prints "CMD+?" and "OPTION+?" when hotkeys are pressed.
            // Map those to the registered actions. This can be extended to parse structured JSON.
            try
            {
                var trimmed = line.Trim();

                if (trimmed.Contains("CMD+?", StringComparison.OrdinalIgnoreCase))
                {
                    _aiHelpAction?.Invoke();
                    return;
                }

                if (trimmed.Contains("OPTION+?", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("OPT+?", StringComparison.OrdinalIgnoreCase))
                {
                    _windowCaptureAction?.Invoke();
                    return;
                }

                // If the helper prints something else, try to match heuristics
                if (trimmed.IndexOf("cmd", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf("?", StringComparison.Ordinal) >= 0)
                {
                    _aiHelpAction?.Invoke();
                    return;
                }

                if (trimmed.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf("?", StringComparison.Ordinal) >= 0)
                {
                    _windowCaptureAction?.Invoke();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HotKeyService dispatch error: {ex}");
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            Debug.WriteLine("hotkey-listener process exited");
            // process exited; keep state and allow restart on next Register call
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _cts?.Cancel();
            }
            catch { }

            if (_process != null)
            {
                _process.Exited -= OnProcessExited;

                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing hotkey-listener: {ex}");
                }

                _process.Dispose();
                _process = null;
            }

            _cts?.Dispose();
            _cts = null;
        }
    }
}
