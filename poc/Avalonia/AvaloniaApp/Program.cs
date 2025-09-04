using Avalonia;
using Avalonia.Threading;
using System;
using DotNetEnv;

namespace AvaloniaApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Add global exception handling
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            // Log or handle the unhandled exception
            Console.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            // You can add logging here, e.g., to a file or external service
        };

        Env.Load();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
