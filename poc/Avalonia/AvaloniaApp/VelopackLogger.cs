using System;
using System.Text;
using Serilog;
using Velopack.Logging;

namespace AvaloniaApp;

// ReSharper disable once IdentifierTypo
public class VelopackLogger(ILogger logger) : IVelopackLogger
{
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
    {
        message ??= "Null message";

        switch (logLevel)
        {
            case VelopackLogLevel.Critical:
                logger.Error(message, exception);
                break;
            case VelopackLogLevel.Information:
                logger.Information(message, exception);
                break;
            case VelopackLogLevel.Warning:
                logger.Warning(message, exception);
                break;
            case VelopackLogLevel.Debug:
                logger.Debug(message, exception);
                break;
            case VelopackLogLevel.Trace:
                logger.Verbose(message, exception);
                break;
            case VelopackLogLevel.Error:
                logger.Error(message, exception);
                break;
            default:
                logger.Information(message, exception);
                break;
        }

    }
}