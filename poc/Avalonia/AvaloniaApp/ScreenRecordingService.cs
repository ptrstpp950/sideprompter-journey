using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class ScreenRecordingService
{
    private CancellationTokenSource _cancellationTokenSource;
    private Task _recordingTask;
    private const string OutputDirectory = "ScreenRecordings";

    public void Start()
    {
        Directory.CreateDirectory(OutputDirectory);
        _cancellationTokenSource = new CancellationTokenSource();
        _recordingTask = Task.Run(() => RecordLoop(_cancellationTokenSource.Token));
    }

    private async Task RecordLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var fileName = $"recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mov";
            var fullPath = Path.Combine(OutputDirectory, fileName);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/sbin/screencapture",
                    Arguments = $"-V 30 \"{fullPath}\"", // Record a 30-second video
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(token);
        }
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }
}