using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace AvaloniaApp.Settings;

public static class LanguageDetectionService
{
    public static List<string> GetPreferredLanguageCodes()
    {
        var set = new List<string>();

        void Add(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return;
            var code = Normalize(lang);
            if (code == null) return;
            if (!set.Contains(code)) set.Add(code);
        }

        // Common cultures
        Add(CultureInfo.CurrentUICulture.Name);
        Add(CultureInfo.CurrentCulture.Name);
        Add(CultureInfo.InstalledUICulture.Name);

        // Environment variable
        Add(Environment.GetEnvironmentVariable("LANG"));

        // Windows specific registry preferred languages
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\\International\\User Profile");
                if (key != null)
                {
                    var langs = key.GetValue("Languages") as string[];
                    if (langs != null)
                    {
                        foreach (var l in langs) Add(l);
                    }
                }
            }
            catch { /* ignore */ }
        }

        // Filter to whisper supported
        var supported = WhisperLanguages.All.Select(l => l.Code).ToHashSet();
        var filtered = set.Where(supported.Contains).ToList();
        if (filtered.Count == 0)
            filtered.Add("en");
        return filtered;
    }

    private static string? Normalize(string lang)
    {
        lang = lang.Trim().ToLowerInvariant();
        // examples: "en-US", "pl_pl", etc.
        var idx = lang.IndexOfAny(new[] { '-', '_' });
        if (idx > 0) lang = lang[..idx];
        if (lang.Length == 0) return null;
        return lang;
    }
}