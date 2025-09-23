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
    public static readonly string CurrentVersion = "0.0.1";
    public string? AppSettingsVersion { get; set; } // increment when breaking changes are made
    public List<string> Languages { get; set; } = new() { };
    public string WhisperModel { get; set; } = GgmlType.Base.ToString();
    public bool SetupCompleted { get; set; }
    public DateTime? FirstConfiguredUtc { get; set; }
    // New structured per-provider configuration. Key = provider name (friendly label), Value = config
    public Dictionary<string, ChatProviderConfig> ChatProviders { get; set; } = new();
    // Selected provider name (must match a key in ChatProviders). Kept so UI/logic can pick an active provider.
    public string ChatProvider { get; set; } = string.Empty;
    public ObservableCollection<Prompt> Prompts { get; set; } = new();

    [JsonIgnore]
    public GgmlType WhisperModelType => Enum.TryParse<GgmlType>(WhisperModel, out var t) ? t : GgmlType.Base;

    // Helper to get the active provider config. Preference order:
    // 1) ChatProviders[ChatProvider] if present
    // 2) If ChatProviders contains exactly one entry, return that
    [JsonIgnore]
    public ChatProviderConfig? ActiveChatProviderConfig
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ChatProvider) && ChatProviders != null && ChatProviders.TryGetValue(ChatProvider, out var cfg))
                return cfg;

            if (ChatProviders != null && ChatProviders.Count == 1)
            {
                foreach (var v in ChatProviders.Values)
                    return v;
            }

            return null;
        }
    }
}

public class ChatProviderConfig
{
    // Friendly provider name (OpenAI, Ollama, Azure, etc.)
    public string ProviderName { get; set; } = string.Empty;

    // Model identifier (gpt-4o-mini, meta-llama, etc.)
    public string Model { get; set; } = string.Empty;

    // API key for this provider
    public string ApiKey { get; set; } = string.Empty;

    // Optional API base URL (e.g. https://api.openai.com/v1) for providers that need it
    public string ApiBase { get; set; } = string.Empty;
}