using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AvaloniaApp.Settings;

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly AppSettingsJsonContext Context = new();

    public static string GetSettingsDirectory()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appSupport, "SidePrompter");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetSettingsPath() => Path.Combine(GetSettingsDirectory(), "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize(json, Context.AppSettings) ?? new AppSettings();
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = GetSettingsPath();
            settings.FirstConfiguredUtc ??= DateTime.UtcNow;
            var json = JsonSerializer.Serialize(settings, Context.AppSettings);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Prompt))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}