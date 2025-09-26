using System;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace AvaloniaApp.Services.Update;

public interface IAppUpdateService
{
    Task<VelopackAsset?> CheckForUpdatesAsync(bool includePrerelease = false);
    Task<bool> DownloadAndApplyUpdatesAsync(VelopackAsset? updateInfo);
}

public class AppUpdateService : IAppUpdateService
{
    private readonly ILogger _log;
    private readonly string _baseUrl;
    private readonly string _channel;
    private readonly UpdateManager _updateManager;

    public AppUpdateService(ILogger log)
    {
        _log = log;
        _baseUrl = Environment.GetEnvironmentVariable("SIDEPROMPTER_UPDATE_FEED") ?? "https://cdn.sideprompter.com/win/";
        _channel = Environment.GetEnvironmentVariable("SIDEPROMPTER_UPDATE_CHANNEL") ?? "stable";
        
        // Initialize UpdateManager with the CDN URL
        var source = new SimpleWebSource(_baseUrl);
        _updateManager = new UpdateManager(source);
    }

    public async Task<VelopackAsset?> CheckForUpdatesAsync(bool includePrerelease = false)
    {
        try
        {
            _log.Information("Checking for updates from: {UpdateUrl}", _baseUrl);
            
            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            
            if (updateInfo == null)
            {
                _log.Information("No updates available");
                return null;
            }
            
            _log.Information("Update available: {Version}", updateInfo.TargetFullRelease.Version);
            return updateInfo.TargetFullRelease;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check for updates from {UpdateUrl}", _baseUrl);
            return null;
        }
    }

    public async Task<bool> DownloadAndApplyUpdatesAsync(VelopackAsset? updateInfo)
    {
        if (updateInfo == null)
        {
            _log.Warning("No update info provided for download");
            return false;
        }
        
        try
        {
            _log.Information("Starting download of update: {Version}", updateInfo.Version);
            
            // Check for updates again to get the full UpdateInfo object
            var updateCheck = await _updateManager.CheckForUpdatesAsync();
            if (updateCheck == null)
            {
                _log.Warning("Update info not found during download attempt");
                return false;
            }
            
            // Download the update
            await _updateManager.DownloadUpdatesAsync(updateCheck);
            _log.Information("Update downloaded successfully");
            
            // Apply the update and restart the application
            _updateManager.ApplyUpdatesAndExit(updateCheck);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to download/apply update for version {Version}", updateInfo.Version);
            return false;
        }
    }
}
