using Avalonia;
using System;
using System.IO;
using DotNetEnv;
using Serilog;

namespace AvaloniaApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var logDirectory = GetSettingsDirectory();
        var logFilePath = Path.Combine(logDirectory, "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                flushToDiskInterval: TimeSpan.Zero
            )
            .CreateLogger();
 
        try
        {
            Log.Information("Starting application");
            Env.Load();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
            //TODO: think why without this the app hangs on exit
            Environment.Exit(0);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static string GetSettingsDirectory()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appSupport, "SidePrompter");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }
}
