using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public class ChatRequestMessage
{
    public string role { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
}

public class ChatRequestBody
{
    public string model { get; set; } = string.Empty;
    public ChatRequestMessage[] messages { get; set; } = Array.Empty<ChatRequestMessage>();
    public int? max_tokens { get; set; }
}

[JsonSerializable(typeof(ChatRequestBody))]
internal partial class ChatJsonContext : JsonSerializerContext
{
}

public class ChatSettingsPage : SettingsPageViewModel
{
    private readonly AppSettings _settings;
    private readonly ComboBox _provider;
    private readonly TextBox _endpoint;
    private readonly TextBox _apiKey;
    private readonly ComboBox _modelList;
    private readonly TextBox _modelInput;
    private readonly TextBlock _status;
    private static readonly HttpClient _http = new();

    public ChatSettingsPage(AppSettings settings) : base("Chat", "💬", new StackPanel { Spacing = 8 })
    {
        _settings = settings;
        var root = (StackPanel)View;
        root.Children.Add(new TextBlock { Text = "Chat Completion Service", FontWeight = FontWeight.Bold });

        _provider = new ComboBox { ItemsSource = new[] { "OpenAI", "OpenRouter", "Ollama", "Other" }, SelectedIndex = 0, Width = 160 };
        if (!string.IsNullOrWhiteSpace(settings.ChatProvider))
        {
            var idx = (_provider.ItemsSource as IEnumerable<string>)!.ToList().FindIndex(p => p.Equals(settings.ChatProvider, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _provider.SelectedIndex = idx;
        }
        _provider.SelectionChanged += (_, _) => SuggestEndpoint();

        _endpoint = new TextBox { Watermark = "API Base URL", Text = settings.ChatApiBase };
        _apiKey = new TextBox { Watermark = "API Key", Text = settings.ChatApiKey, PasswordChar = '•' };
        _modelList = new ComboBox { Width = 260 };
        _modelInput = new TextBox { Watermark = "Model", Text = settings.ChatModel };
        _modelList.SelectionChanged += (_, _) => { if (_modelList.SelectedItem is string m) _modelInput.Text = m; };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };
        void Row(int r, string label, Control c)
        {
            var lbl = new TextBlock { Text = label, Margin = new Avalonia.Thickness(0,4,8,0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            grid.Children.Add(lbl); Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
            grid.Children.Add(c); Grid.SetRow(c, r); Grid.SetColumn(c, 1);
        }
        Row(0, "Provider:", _provider);
        Row(1, "Endpoint:", _endpoint);
        Row(2, "API Key:", _apiKey);
        var modelStack = new StackPanel { Spacing = 4 };
        modelStack.Children.Add(_modelList);
        modelStack.Children.Add(_modelInput);
        Row(3, "Model:", modelStack);
        root.Children.Add(grid);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var fetchBtn = new Button { Content = "Fetch Models" }; fetchBtn.Click += async (_, _) => await FetchModels();
        var testBtn = new Button { Content = "Verify" }; testBtn.Click += async (_, _) => await VerifyChat();
        btnRow.Children.Add(fetchBtn); btnRow.Children.Add(testBtn);
        root.Children.Add(btnRow);

        _status = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 12 };
        root.Children.Add(_status);

        SuggestEndpoint();
    }

    private void SuggestEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ChatApiBase)) return;
        var provider = _provider.SelectedItem?.ToString()?.ToLowerInvariant();
        _endpoint.Text = provider switch
        {
            "openai" => "https://api.openai.com/v1/chat/completions",
            "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
            "ollama" => "http://localhost:11434/api/chat",
            _ => _endpoint.Text
        };
    }

    private async System.Threading.Tasks.Task FetchModels()
    {
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;
        var baseUrl = _endpoint.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) { _status.Text = "Invalid endpoint"; return; }
        _status.Text = "Fetching models...";
        try
        {
            using var req = BuildModelsRequest(provider, baseUrl);
            if (!string.IsNullOrWhiteSpace(_apiKey.Text) && (provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey.Text.Trim());
                if (provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)) req.Headers.Add("HTTP-Referer", "https://sideprompter.local");
            }
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { _status.Text = $"Error: {resp.StatusCode}"; return; }
            var list = ParseModels(provider, json);
            _modelList.ItemsSource = list;
            _status.Text = list.Count == 0 ? "No models" : $"{list.Count} models";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private static HttpRequestMessage BuildModelsRequest(string provider, string baseUrl)
    {
        string path = provider.ToLowerInvariant() switch
        {
            "openai" or "openrouter" => Combine(baseUrl, "/models"),
            "ollama" => Combine(baseUrl, "/api/tags"),
            _ => Combine(baseUrl, "/models")
        };
        return new HttpRequestMessage(HttpMethod.Get, path);
    }

    private static string Combine(string a,string b){ if(a.EndsWith('/')) a=a.TrimEnd('/'); return a+b; }

    private static List<string> ParseModels(string provider, string json)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var lower = provider.ToLowerInvariant();
            if (lower is "openai" or "openrouter")
            {
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in data.EnumerateArray())
                    {
                        if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) result.Add(id.GetString()!);
                    }
                }
            }
            else if (lower == "ollama")
            {
                if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in arr.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var id) && id.ValueKind == JsonValueKind.String) result.Add(id.GetString()!);
                    }
                }
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    private async System.Threading.Tasks.Task VerifyChat()
    {
        if (!ValidateAndSave(out var err)) { _status.Text = err; return; }
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;
        _status.Text = "Testing...";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Combine(_settings.ChatApiBase, provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "/api/chat" : "/chat/completions"));
            var key = _settings.ChatApiKey?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && (provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                if (provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)) req.Headers.Add("HTTP-Referer", "https://sideprompter.local");
            }
            ChatRequestBody body;
            if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                body = new ChatRequestBody
                {
                    model = _settings.ChatModel,
                    messages = new[] { new ChatRequestMessage { role = "user", content = "ping" } }
                };
            }
            else
            {
                body = new ChatRequestBody
                {
                    model = _settings.ChatModel,
                    messages = new[] { new ChatRequestMessage { role = "user", content = "ping" } },
                    max_tokens = 4
                };
            }
            req.Content = new StringContent(JsonSerializer.Serialize(body, ChatJsonContext.Default.ChatRequestBody), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { _status.Text = $"Failed: {resp.StatusCode}"; return; }
            _status.Text = "OK";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        var endpoint = _endpoint.Text?.Trim() ?? string.Empty;
        var model = _modelInput.Text?.Trim() ?? string.Empty;
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _)) { errorMessage = "Enter valid endpoint"; return false; }
        if (string.IsNullOrWhiteSpace(model)) { errorMessage = "Enter model"; return false; }
        var needsKey = provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);
        if (needsKey && string.IsNullOrWhiteSpace(_apiKey.Text)) { errorMessage = "API key required"; return false; }
        _settings.ChatApiBase = endpoint; _settings.ChatModel = model; _settings.ChatProvider = provider; if (!string.IsNullOrWhiteSpace(_apiKey.Text)) _settings.ChatApiKey = _apiKey.Text.Trim();
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
