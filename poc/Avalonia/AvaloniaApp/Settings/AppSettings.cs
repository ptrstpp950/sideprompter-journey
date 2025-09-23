using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Whisper.net.Ggml;

namespace AvaloniaApp.Settings;

public class Prompt
{
    public string Title { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;

    public HeroIconsAvalonia.Enums.IconType? Icon { get; set; } = HeroIconsAvalonia.Enums.IconType.QuestionMarkCircle;

    public string GetHash()
    {
        return Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(Title + PromptText)));
    }
}

public class AppSettings
{
    public List<string> Languages { get; set; } = new() { };
    public string WhisperModel { get; set; } = GgmlType.Base.ToString();
    public bool SetupCompleted { get; set; }
    public DateTime? FirstConfiguredUtc { get; set; }
    public string ChatApiBase { get; set; } = string.Empty; // e.g. https://api.openai.com/v1 or local server
    public string ChatApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = string.Empty; // e.g. gpt-4o-mini, meta-llama, etc.
    public string ChatProvider { get; set; } = string.Empty; // optional friendly label (OpenAI, Ollama, Azure, Groq, etc.)
    public ObservableCollection<Prompt> Prompts { get; set; } = new();

    [JsonIgnore]
    public GgmlType WhisperModelType => Enum.TryParse<GgmlType>(WhisperModel, out var t) ? t : GgmlType.Base;
}