using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Whisper.net.Ggml;

namespace AvaloniaApp.Settings;

public class AppSettings
{
    public List<string> Languages { get; set; } = new() { "en" };
    public string WhisperModel { get; set; } = GgmlType.Base.ToString();
    public bool SetupCompleted { get; set; }
    public DateTime? FirstConfiguredUtc { get; set; }

    [JsonIgnore]
    public GgmlType WhisperModelType => Enum.TryParse<GgmlType>(WhisperModel, out var t) ? t : GgmlType.Base;
}