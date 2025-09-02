using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Whisper.net.Ggml;
using AvaloniaApp.Settings;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Whisper.net;
using Avalonia.Threading;
using AvaloniaApp.Services.TranscriptionService;

namespace AvaloniaApp;

public partial class SetupWizard : Window
{
    private int _stepIndex;
    private readonly List<Func<Control>> _steps;
    private readonly AppSettings _settings;

    // Step controls state
    private TextBox? _languageAutoBox;
    private ListBox? _languageSuggestions;
    private StackPanel? _selectedLanguagesPanel;
    private ComboBox? _modelCombo;
    private TextBlock? _modelDescription;
    private List<ModelOption> _currentModelOptions = new();
    private TextBlock? _modelDownloadStatus;
    private ProgressBar? _modelDownloadProgress;
    private CancellationTokenSource? _modelDownloadCts; // reserved if we add cancellation later
    // macOS step controls
    private TextBlock? _macStatusText;
    private ProgressBar? _macProgressBar;
    // Chat completion step controls
    private TextBox? _chatEndpointBox;
    private TextBox? _chatApiKeyBox;
    private ComboBox? _chatModelListCombo;
    private TextBox? _chatModelInput;
    private ComboBox? _chatProviderCombo;
    private TextBlock? _chatValidationMessage;
    private Button? _chatFetchModelsButton;
    private Button? _chatVerifyButton;
    private CancellationTokenSource? _chatModelsCts;

    private static readonly HttpClient _httpClient = new();

    private class ModelOption
    {
        public string Key { get; init; } = string.Empty;          // e.g. "base.en" or "base"
    public string Display { get; set; } = string.Empty;      // full display line (mutable for post-build labeling)
        public string Parameters { get; init; } = string.Empty;   // e.g. 74 M
        public string Vram { get; init; } = string.Empty;         // e.g. ~1 GB
        public string Speed { get; init; } = string.Empty;        // e.g. ~7x
        public bool EnglishOnly { get; init; }
        public string? GgmlName { get; init; } // e.g. BaseEn, Base
        public override string ToString() => Display;
    }

    public SetupWizard() : this(new AppSettings()) {}

    public SetupWizard(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        _steps = new List<Func<Control>>
        {
            BuildLanguagesStep,
            BuildModelStep,
            BuildChatCompletionStep,
            BuildMacExtraStep,
            BuildDonationStep
        };
        // If languages not yet chosen (fresh run), seed with OS preferred
        var osPreferred = LanguageDetectionService.GetPreferredLanguageCodes();
        if (_settings.Languages == null || _settings.Languages.Count == 0)
        {
            _settings.Languages = osPreferred.ToList();
            SettingsService.Save(_settings);
        }
        else
        {
            // Ensure OS preferred are included (auto-select) without duplicating
            var changed = false;
            foreach (var p in osPreferred)
            {
                if (!_settings.Languages.Contains(p))
                {
                    _settings.Languages.Add(p);
                    changed = true;
                }
            }
            if (changed) SettingsService.Save(_settings);
        }
        LoadStep();
    }

    private Control BuildLanguagesStep()
    {
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock { Text = "Choose languages to enable (at least one):", FontWeight = FontWeight.Bold });

        // Selected tags panel
    _selectedLanguagesPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        // Use WrapPanel-like behavior by nesting inside ScrollViewer if overflow
        var selectedScroll = new ScrollViewer { Height = 70, Content = _selectedLanguagesPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Selected:" });
        root.Children.Add(selectedScroll);

        // Autocomplete box
        _languageAutoBox = new TextBox { Watermark = "Type to search language (press Enter to add)..." };
        _languageAutoBox.TextChanged += (_, _) => RefreshSuggestionList();
        _languageAutoBox.KeyUp += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                TryAddCurrentAutocompleteEntry();
            }
            if (e.Key == Avalonia.Input.Key.Down)
            {
                if (_languageSuggestions != null)
                {
                    _languageSuggestions.Focus();
                    if (_languageSuggestions.SelectedIndex < 0 && _languageSuggestions.ItemCount > 0)
                        _languageSuggestions.SelectedIndex = 0;
                    else if (_languageSuggestions.SelectedIndex < _languageSuggestions.ItemCount - 1)
                        _languageSuggestions.SelectedIndex += 1;
                }
            }
        };
        root.Children.Add(_languageAutoBox);

        _languageSuggestions = new ListBox { Height = 180 }; // suggestion list
        _languageSuggestions.DoubleTapped += (_, _) => AddSelectedSuggestion();
        _languageSuggestions.KeyUp += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) AddSelectedSuggestion();
        };
        root.Children.Add(_languageSuggestions);

        // Actions
    var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    var selectAll = new Button { Content = "All" };
    selectAll.Click += (_, _) => { _settings.Languages = WhisperLanguages.All.Select(l => l.Code).ToList(); RefreshSelectedTags(); RefreshSuggestionList(); };
    var clearBtn = new Button { Content = "Clear" };
    clearBtn.Click += (_, _) => { _settings.Languages.Clear(); RefreshSelectedTags(); RefreshSuggestionList(); };
    actions.Children.Add(selectAll);
    actions.Children.Add(clearBtn);
        root.Children.Add(actions);
        root.Children.Add(new TextBlock { Text = "Tip: Enter adds top match. Bold items are OS preferred. English (en) recommended.", FontStyle = FontStyle.Italic, FontSize = 12 });

        RefreshSelectedTags();
        RefreshSuggestionList();
        return root;
    }

    private void TryAddCurrentAutocompleteEntry()
    {
        if (_languageAutoBox == null) return;
        var text = _languageAutoBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        // Accept either code or name partial if unique
        var match = WhisperLanguages.All.FirstOrDefault(l => string.Equals(l.Code, text, StringComparison.OrdinalIgnoreCase));
        if (match == default)
        {
            // try by name contains
            var matches = WhisperLanguages.All.Where(l => l.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) match = matches[0];
        }
        if (match != default)
        {
            if (!_settings.Languages.Contains(match.Code))
            {
                _settings.Languages.Add(match.Code);
                RefreshSelectedTags();
            }
            _languageAutoBox.Text = string.Empty;
            RefreshSuggestionList();
        }
    }

    private void AddSelectedSuggestion()
    {
        if (_languageSuggestions?.SelectedItem is Control c && c.Tag is string code)
        {
            if (!_settings.Languages.Contains(code))
            {
                _settings.Languages.Add(code);
                RefreshSelectedTags();
            }
            RefreshSuggestionList();
        }
    }

    private void RefreshSelectedTags()
    {
        if (_selectedLanguagesPanel == null) return;
        _selectedLanguagesPanel.Children.Clear();
        var defaults = LanguageDetectionService.GetPreferredLanguageCodes();
        foreach (var code in _settings.Languages.ToList())
        {
            var name = WhisperLanguages.All.FirstOrDefault(l => l.Code == code).Name ?? code;
            var b = new Button
            {
                Content = $"{name} ({code})",
                Tag = code,
                Margin = new Thickness(2,2,2,2),
                FontWeight = defaults.Contains(code) ? FontWeight.Bold : FontWeight.Normal
            };
            b.Click += (_, _) =>
            {
                _settings.Languages.Remove(code);
                RefreshSelectedTags();
                RefreshSuggestionList();
            };
            _selectedLanguagesPanel.Children.Add(b);
        }
    }

    private void RefreshSuggestionList()
    {
        if (_languageSuggestions == null) return;
        var filter = _languageAutoBox?.Text?.Trim().ToLowerInvariant();
        IEnumerable<(string Code, string Name)> langs = WhisperLanguages.All;
        if (!string.IsNullOrWhiteSpace(filter))
            langs = langs.Where(l => l.Code.Contains(filter!) || l.Name.ToLowerInvariant().Contains(filter!));
        // exclude already selected
        langs = langs.Where(l => !_settings.Languages.Contains(l.Code)).Take(50);
        var defaults = LanguageDetectionService.GetPreferredLanguageCodes();
        var list = new List<Control>();
        foreach (var (code, name) in langs)
        {
            var tb = new TextBlock { Text = $"{name} ({code})", FontWeight = defaults.Contains(code) ? FontWeight.Bold : FontWeight.Normal };
            var border = new Border { Child = tb, Padding = new Thickness(4,2,4,2), Tag = code };
            list.Add(border);
        }
        _languageSuggestions.ItemsSource = list;
        if (!list.Any())
        {
            _languageSuggestions.ItemsSource = new[] { new TextBlock { Text = "No matches", FontStyle = FontStyle.Italic } };
        }
    }

    private Control BuildModelStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Choose Whisper model to download:", FontWeight = FontWeight.Bold });

        var englishOnly = _settings.Languages.All(l => l == "en");
        _currentModelOptions = BuildModelOptions(englishOnly);

        _modelCombo = new ComboBox
        {
            ItemsSource = _currentModelOptions,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            MinWidth = 420
        };

        // Try to restore previous selection
        ModelOption? preselect = null;
        if (Enum.TryParse<GgmlType>(_settings.WhisperModel, out var existing))
        {
            preselect = _currentModelOptions.FirstOrDefault(m => string.Equals(m.GgmlName, existing.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        _modelCombo.SelectedItem = preselect ?? _currentModelOptions.FirstOrDefault(o => o.Key.StartsWith("base"));
    _modelCombo.SelectionChanged += (_, _) => { UpdateModelDescription(); };

        _modelDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) };
        panel.Children.Add(_modelCombo);
        panel.Children.Add(_modelDescription);
        _modelDownloadStatus = new TextBlock { Text = string.Empty, FontSize = 12, Margin = new Thickness(0,4,0,0) };
        _modelDownloadProgress = new ProgressBar { IsIndeterminate = false, Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0,2,0,0), IsVisible = false };
        panel.Children.Add(_modelDownloadStatus);
        panel.Children.Add(_modelDownloadProgress);
        panel.Children.Add(new TextBlock
        {
            Text = englishOnly
                ? "English-only models (.en) are slightly faster & smaller. Switch to multilingual by adding another language in previous step."
                : "Multilingual models support all chosen languages. For only English you could get faster .en variants.",
            FontStyle = FontStyle.Italic,
            FontSize = 12,
            Margin = new Thickness(0,4,0,0)
        });
        UpdateModelDescription();
        return panel;
    }

    private Control BuildChatCompletionStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Configure Chat Completion Service", FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Provide an OpenAI or OpenAI-compatible endpoint (OpenAI, Azure OpenAI, Ollama, Groq, etc.).", TextWrapping = TextWrapping.Wrap, FontSize = 12 });

    var providerList = new[] { "OpenAI", "OpenRouter", "Ollama", "Other" };
        _chatProviderCombo = new ComboBox { ItemsSource = providerList, SelectedIndex = 0, Width = 160 };
        if (!string.IsNullOrWhiteSpace(_settings.ChatProvider))
        {
            var idx = Array.FindIndex(providerList, x => x.Equals(_settings.ChatProvider, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _chatProviderCombo.SelectedIndex = idx; else _chatProviderCombo.SelectedItem = _settings.ChatProvider;
        }

    _chatEndpointBox = new TextBox { Watermark = "API Base URL (https://...)" , Text = _settings.ChatApiBase };
        _chatApiKeyBox = new TextBox { Watermark = "API Key (kept local)", Text = _settings.ChatApiKey, PasswordChar = '•' };
    _chatModelInput = new TextBox { Watermark = "Model (e.g. gpt-4o-mini, llama3, phi3, etc.)", Text = _settings.ChatModel };
    _chatModelListCombo = new ComboBox { Width = 260, PlaceholderText = "(fetch models)" };
        _chatValidationMessage = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 12 };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };
        void AddRow(int r, string label, Control control)
        {
            var lbl = new TextBlock { Text = label, Margin = new Thickness(0,4,8,0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            grid.Children.Add(lbl); Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
            grid.Children.Add(control); Grid.SetRow(control, r); Grid.SetColumn(control, 1);
        }
        AddRow(0, "Provider:", _chatProviderCombo);
        AddRow(1, "Endpoint:", _chatEndpointBox);
        AddRow(2, "API Key:", _chatApiKeyBox);
    var modelStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
    // Order: fetchable list first, then editable text box
    modelStack.Children.Add(_chatModelListCombo);
    modelStack.Children.Add(_chatModelInput);
    AddRow(3, "Model:", modelStack);
        panel.Children.Add(grid);
        // Fetch models button + hint
    var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    _chatFetchModelsButton = new Button { Content = "Fetch Models" };
    _chatFetchModelsButton.Click += async (_, _) => await FetchModelsAsync();
    _chatVerifyButton = new Button { Content = "Verify" };
    _chatVerifyButton.Click += async (_, _) => await VerifyChatAsync();
    actionRow.Children.Add(_chatFetchModelsButton);
    actionRow.Children.Add(_chatVerifyButton);
    actionRow.Children.Add(new TextBlock { Text = "(Fetch list / test message)", FontSize = 11, FontStyle = FontStyle.Italic });
    panel.Children.Add(actionRow);
        panel.Children.Add(new TextBlock { Text = "Key is stored locally (settings.json).", FontSize = 11, FontStyle = FontStyle.Italic });
        panel.Children.Add(_chatValidationMessage);

        // Pre-fill endpoint defaults when provider changes
        _chatProviderCombo.SelectionChanged += (_, _) => SuggestEndpoint();
        SuggestEndpoint();
        _chatModelListCombo.SelectionChanged += (_, _) =>
        {
            if (_chatModelListCombo.SelectedItem is string m && _chatModelInput != null)
            {
                _chatModelInput.Text = m;
            }
        };
        return panel;
    }

    private void SuggestEndpoint()
    {
        if (_chatProviderCombo?.SelectedItem is not string provider || _chatEndpointBox == null) return;
        if (!string.IsNullOrWhiteSpace(_settings.ChatApiBase)) return; // don't overwrite user value if already set
        var lower = provider.ToLowerInvariant();
        string suggestion = lower switch
        {
            "openai" => "https://api.openai.com/v1",
            "openrouter" => "https://openrouter.ai/api/v1",
            "ollama" => "http://localhost:11434", // default local
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(suggestion)) _chatEndpointBox.Text = suggestion;
    }

    private async Task FetchModelsAsync()
    {
    if (_chatEndpointBox == null || _chatProviderCombo == null || _chatValidationMessage == null || _chatModelListCombo == null || _chatModelInput == null) return;
        var provider = _chatProviderCombo.SelectedItem?.ToString() ?? string.Empty;
        var baseUrl = _chatEndpointBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl)) { _chatValidationMessage.Text = "Enter endpoint first."; return; }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) { _chatValidationMessage.Text = "Invalid endpoint."; return; }

        _chatModelsCts?.Cancel();
        _chatModelsCts = new CancellationTokenSource();
        var ct = _chatModelsCts.Token;

        _chatFetchModelsButton!.IsEnabled = false;
        _chatValidationMessage.Text = "Fetching models...";
        try
        {
            var request = BuildModelsRequest(provider, baseUrl);
            if (request == null)
            {
                _chatValidationMessage.Text = "Model listing not implemented for this provider.";
                return;
            }
            // Add api key header if present
            var key = _chatApiKeyBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
                else if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
            }
            using var resp = await _httpClient.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _chatValidationMessage.Text = $"Models fetch failed: {resp.StatusCode}";
                return;
            }
            var models = ParseModels(provider, body);
            if (models.Count == 0)
            {
                _chatValidationMessage.Text = "No models found.";
                return;
            }
            _chatModelListCombo.ItemsSource = models;
            if (_chatModelInput.Text == string.Empty && _chatModelListCombo.ItemCount > 0)
            {
                _chatModelListCombo.SelectedIndex = 0;
                if (_chatModelListCombo.SelectedItem is string first) _chatModelInput.Text = first;
            }
            _chatValidationMessage.Text = $"Loaded {models.Count} models.";
        }
        catch (OperationCanceledException)
        {
            _chatValidationMessage.Text = "Model fetch canceled.";
        }
        catch (Exception ex)
        {
            _chatValidationMessage.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _chatFetchModelsButton!.IsEnabled = true;
        }
    }

    private static HttpRequestMessage? BuildModelsRequest(string provider, string baseUrl)
    {
        var lower = provider.ToLowerInvariant();
        try
        {
            return lower switch
            {
                "openai" => new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, "/models")),
                "openrouter" => new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, "/models")),
                "ollama" => new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, "/api/tags")),
                _ => null
            };
        }
        catch { return null; }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (baseUrl.EndsWith('/')) baseUrl = baseUrl.TrimEnd('/');
        return baseUrl + path;
    }

    private static List<string> ParseModels(string provider, string json)
    {
        var result = new List<string>();
        var lower = provider.ToLowerInvariant();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (lower == "openai" || lower == "openrouter")
            {
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                            result.Add(idProp.GetString()!);
                    }
                }
            }
            else if (lower == "ollama")
            {
                if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in models.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                            result.Add(nameProp.GetString()!);
                    }
                }
            }
        }
        catch { }
        // Deduplicate & sort
        return result.Distinct(StringComparer.Ordinal).OrderBy(m => m).ToList();
    }

    private async Task VerifyChatAsync()
    {
        if (_chatEndpointBox == null || _chatApiKeyBox == null || _chatModelInput == null || _chatProviderCombo == null || _chatValidationMessage == null) return;
        if (!ValidateAndPersistChatSettings()) return; // ensures settings saved & validation messages shown
        _chatVerifyButton!.IsEnabled = false;
        _chatValidationMessage.Text = "Sending test message...";
        try
        {
            // Minimal inline test without pulling full ChatCompletionService if environment variables not yet set.
            var endpoint = _chatEndpointBox.Text!.Trim();
            var model = _chatModelInput.Text!.Trim();
            var key = _chatApiKeyBox.Text?.Trim();
            var provider = _chatProviderCombo.SelectedItem?.ToString() ?? string.Empty;
            // Build OpenAI-compatible request body
            var url = provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                ? CombineUrl(endpoint, "/api/chat")
                : CombineUrl(endpoint, "/chat/completions");
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(key) && (provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)))
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
            }
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                // OpenRouter encourages setting Referer and X-Title but optional
                req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://sideprompter.local/setup");
                req.Headers.TryAddWithoutValidation("X-Title", "SidePrompter Setup");
            }
            // Payload differs for Ollama vs OpenAI style
            object payload;
            if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                payload = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "Testing chat. Present yourself" } },
                    stream = false
                };
            }
            else
            {
                payload = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "Testing chat. Present yourself" } },
                    temperature = 0.2
                };
            }
            var json = JsonSerializer.Serialize(payload);
            req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _chatValidationMessage.Text = $"Verify failed: {resp.StatusCode}";
                return;
            }
            var answer = ExtractAssistantReply(provider, body);
            if (string.IsNullOrWhiteSpace(answer)) answer = "(No reply parsed)";
            _chatValidationMessage.Text = "Success.";
            // Reuse chat log facility via a simple global style event? For now show inline appended.
            _chatValidationMessage.Text += " Received reply.";
            // Append truncated preview
            if (answer.Length > 240) answer = answer[..240] + "...";
            _chatValidationMessage.Text += $" \nReply: {answer}";
        }
        catch (Exception ex)
        {
            _chatValidationMessage.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _chatVerifyButton!.IsEnabled = true;
        }
    }

    private static string ExtractAssistantReply(string provider, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                // Ollama chat response: { message: { role: "assistant", content: "..." }, ... }
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    return c.GetString()!;
            }
            else
            {
                // OpenAI style
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    var first = choices.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        return content.GetString()!;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static bool GgmlContains(string name)
    {
        return Enum.GetNames(typeof(GgmlType)).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    private List<ModelOption> BuildModelOptions(bool englishOnly)
    {
        // Table provided by user
        // Size / Params / English-only / Multilingual / VRAM / Relative speed
        // tiny 39M tiny.en tiny ~1 GB ~10x
        // base 74M base.en base ~1 GB ~7x
        // small 244M small.en small ~2 GB ~4x
        // medium 769M medium.en medium ~5 GB ~2x
        // large 1550M N/A large ~10 GB 1x
        // turbo 809M N/A turbo ~6 GB ~8x

        var list = new List<ModelOption>();
        void Add(string key, string paramsText, string vram, string speed, bool enOnlyVariant, string ggml)
        {
            if (!GgmlContains(ggml)) return; // skip if not available in current Whisper.net version
            list.Add(new ModelOption
            {
                Key = key,
                Parameters = paramsText,
                Vram = vram,
                Speed = speed,
                EnglishOnly = enOnlyVariant,
                GgmlName = ggml,
                Display = key // placeholder; we'll replace with simple description afterwards
            });
        }


        if (englishOnly)
        {
            Add("tiny.en", "39M", "~1 GB", "~10x", true, "TinyEn");
            Add("base.en", "74M", "~1 GB", "~7x", true, "BaseEn");
            Add("small.en", "244M", "~2 GB", "~4x", true, "SmallEn");
            Add("medium.en", "769M", "~5 GB", "~2x", true, "MediumEn");
            // large (no English-only variant) fallback to latest large multi
            if (GgmlContains("LargeV3")) Add("large", "1550M", "~10 GB", "1x", false, "LargeV3");
            else if (GgmlContains("LargeV2")) Add("large", "1550M", "~10 GB", "1x", false, "LargeV2");
            // turbo (if exists) - some forks expose Turbo, skip if not present
            if (GgmlContains("Turbo")) Add("turbo", "809M", "~6 GB", "~8x", false, "Turbo");
        }
        else
        {
            Add("tiny", "39M", "~1 GB", "~10x", false, "Tiny");
            Add("base", "74M", "~1 GB", "~7x", false, "Base");
            Add("small", "244M", "~2 GB", "~4x", false, "Small");
            Add("medium", "769M", "~5 GB", "~2x", false, "Medium");
            if (GgmlContains("LargeV3")) Add("large", "1550M", "~10 GB", "1x", false, "LargeV3");
            else if (GgmlContains("LargeV2")) Add("large", "1550M", "~10 GB", "1x", false, "LargeV2");
            if (GgmlContains("Turbo")) Add("turbo", "809M", "~6 GB", "~8x", false, "Turbo");
        }

        // Replace Display with simple description + optional best tag
        foreach (var mo in list)
        {
            var summary = SummarizeModel(mo.Key, mo.EnglishOnly);
            var bestTag = mo.Key.StartsWith("base", StringComparison.OrdinalIgnoreCase) ? " (the best option)" : string.Empty;
            mo.Display = $"{mo.Key} - {summary}{bestTag}";
        }
        return list;
    }

    private void UpdateModelDescription()
    {
        if (_modelCombo?.SelectedItem is ModelOption opt)
        {
            _modelDescription!.Text = BuildLongDescription(opt);
        }
    }

    private async Task<bool> DownloadSelectedModelIfNeededAsync()
    {
        if (!(_modelCombo?.SelectedItem is ModelOption opt) || string.IsNullOrWhiteSpace(opt.GgmlName)) return true; // nothing to do
        if (!Enum.TryParse<GgmlType>(opt.GgmlName, out var modelType)) return true;

        // Persist selection (also done later in validation, but we store early in case of failure logs)
        _settings.WhisperModel = modelType.ToString();
        SettingsService.Save(_settings);

        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var modelDir = Path.Combine(appSupport, "SidePrompter");
        var modelName = $"ggml-{modelType}.bin";
        var modelPath = Path.Combine(modelDir, modelName);
        if (File.Exists(modelPath))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_modelDownloadStatus != null) _modelDownloadStatus.Text = $"Model '{opt.Key}' already downloaded.";
            });
            return true;
        }

        // UI prep
        Dispatcher.UIThread.Post(() =>
        {
            if (_modelDownloadProgress != null)
            {
                _modelDownloadProgress.IsVisible = true;
                _modelDownloadProgress.IsIndeterminate = true;
            }
            if (_modelDownloadStatus != null) _modelDownloadStatus.Text = $"Downloading model '{opt.Key}'...";
        });

        try
        {
            await WhisperTranscriptionService.EnsureModelDownloadedAsync(modelType, msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_modelDownloadStatus != null) _modelDownloadStatus.Text = msg;
                });
            });
            Dispatcher.UIThread.Post(() => { if (_modelDownloadProgress != null) _modelDownloadProgress.IsVisible = false; });
            return true;
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_modelDownloadStatus != null) _modelDownloadStatus.Text = $"Download failed: {ex.Message}";
                if (_modelDownloadProgress != null) _modelDownloadProgress.IsVisible = false;
            });
            return false;
        }
    }

    private static string SummarizeModel(string key, bool englishOnly)
    {
        var lower = key.ToLowerInvariant();
        var normalized = lower.EndsWith(".en") ? lower[..^3] : lower;
        string summary = normalized switch
        {
            "tiny" => "Tiny: fastest, lowest accuracy; drafts only.",
            "base" => "Base: default balance speed vs accuracy.",
            "small" => "Small: better accuracy, slightly slower.",
            "medium" => "Medium: high accuracy, much slower & more RAM.",
            "large" => "Large: highest accuracy, very slow on CPU (GPU recommended).",
            "turbo" => "Turbo: near-large accuracy but faster; GPU recommended.",
            _ => "Model variant."
        };
        if (englishOnly) summary += " (EN only)";
        return summary;
    }

    private static string BuildLongDescription(ModelOption opt)
    {
        var keyLower = opt.Key.ToLowerInvariant();
        var normalized = keyLower.EndsWith(".en") ? keyLower[..^3] : keyLower;
        string download = EstimateDownload(opt.Parameters);
        string enNote = opt.EnglishOnly ? " This is an English‑only variant, a little smaller & faster than the multilingual one." : string.Empty;
        string body = normalized switch
        {
            "tiny" => $"Tiny model: quickest to run and light on resources, but accuracy is the lowest. Perfect for rough real‑time drafts or quick checks on low‑power machines. Download about {download}; needs roughly {opt.Vram} free RAM/VRAM. If you care about accuracy, move up to Base.",
            "base" => $"Base model: a balanced default for everyday transcription. Better accuracy than Tiny while still fairly quick on modern CPUs. Download about {download}; expect around {opt.Vram} usage. Good starting point. Try Small if you want more accuracy, or Tiny if you need extra speed.",
            "small" => $"Small model: noticeably higher accuracy than Base with a modest speed hit. Solid choice if you transcribe varied speakers or accents. Download about {download}; needs roughly {opt.Vram}. If you still see mistakes and can wait longer, try Medium.",
            "medium" => $"Medium model: high accuracy for most scenarios (podcasts, meetings, multi‑speaker). Slower, so best when quality matters more than turnaround. Download about {download}; memory use around {opt.Vram}. If you want the very best (and have a GPU), Large is next.",
            "large" => $"Large model: highest accuracy available here. Slow on CPU; a GPU is strongly recommended for practical speeds. Download about {download}; may use up to {opt.Vram}. Great for long, important recordings where accuracy is critical.",
            "turbo" => $"Turbo model: aims for near‑Large accuracy but tuned for much faster inference on a capable GPU. Download about {download}; expects roughly {opt.Vram}. Falls back to slower CPU if no GPU. Useful when you need quality plus speed.",
            _ => $"{opt.Key} model variant: download about {download}."
        };
        if ((normalized == "large" || normalized == "turbo") && !body.Contains("GPU", StringComparison.OrdinalIgnoreCase))
        {
            body += " A GPU is recommended.";
        }
        return ($"{opt.Key} — {body}{enNote}").Trim();
    }

    private static string EstimateDownload(string parameters)
    {
        // Predefined approximations for fp16-equivalent sizes (params * 2 bytes), rounded and simplified.
        return parameters switch
        {
            "39M" => "~75-80 MB",
            "74M" => "~140-150 MB",
            "244M" => "~470-500 MB",
            "769M" => "~1.5 GB",
            "809M" => "~1.6 GB",
            "1550M" => "~3.0-3.2 GB",
            _ => "(size varies)"
        };
    }
    private Control BuildMacExtraStep()
    {
    var panel = new StackPanel { Spacing = 8 };
#if MACOS || OSX || MACCATALYST
    panel.Children.Add(new TextBlock { Text = "macOS setup", FontWeight = FontWeight.Bold });
    panel.Children.Add(new TextBlock { Text = "This step will guide additional macOS specific permissions or setup in future updates.", TextWrapping = TextWrapping.Wrap });
    _macStatusText = new TextBlock { Text = "Pending...", FontStyle = FontStyle.Italic, FontSize = 12 };
    _macProgressBar = new ProgressBar { IsVisible = false, Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0,4,0,0) };
    panel.Children.Add(_macStatusText);
    panel.Children.Add(_macProgressBar);
    panel.Children.Add(new TextBlock { Text = "(Buttons for granting screen/audio permissions etc. will appear here later.)", FontSize = 11, FontStyle = FontStyle.Italic });
#else
    panel.Children.Add(new TextBlock { Text = "macOS extra step (not applicable on this platform).", FontStyle = FontStyle.Italic });
#endif
    return panel;
    }

    private Control BuildDonationStep()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "Thanks for trying SidePrompter!", FontSize = 20, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "If you find this useful, consider buying me a coffee:", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        var link = new TextBlock { Text = "https://buymeacoffee.com/sideprompter", Foreground = Avalonia.Media.Brushes.DodgerBlue, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        link.PointerReleased += (_, _) =>
        {
            try
            {
                var url = "https://buymeacoffee.com/sideprompter";
                using var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                p.Start();
            }
            catch { }
        };
        panel.Children.Add(link);
        panel.Children.Add(new TextBlock { Text = "Click Finish to start using the app.", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        return panel;
    }

    private void LoadStep()
    {
        StepContent.Content = _steps[_stepIndex]();
        BackButton.IsEnabled = _stepIndex > 0;
        var lastIndex = _steps.Count - 1;
        NextButton.IsVisible = _stepIndex < lastIndex;
        FinishButton.IsVisible = _stepIndex == lastIndex;
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_stepIndex > 0)
        {
            _stepIndex--;
            LoadStep();
        }
    }

    private async void Next_Click(object? sender, RoutedEventArgs e)
    {
        // If we're on the model step, download before advancing
        if (_stepIndex == 1)
        {
            if (!ValidateAndPersistCurrentStep()) return;
            // Disable buttons during download
            var oldNextEnabled = NextButton.IsEnabled;
            var oldBackEnabled = BackButton.IsEnabled;
            NextButton.IsEnabled = false; BackButton.IsEnabled = false;
            var ok = await DownloadSelectedModelIfNeededAsync();
            NextButton.IsEnabled = oldNextEnabled; BackButton.IsEnabled = oldBackEnabled;
            if (!ok) return; // stay on step if failed
        }
        else
        {
            if (!ValidateAndPersistCurrentStep()) return;
        }

        if (_stepIndex < _steps.Count - 1)
        {
            _stepIndex++;
            LoadStep();
        }
    }

    private void Finish_Click(object? sender, RoutedEventArgs e)
    {
        if (!ValidateAndPersistCurrentStep()) return;
        _settings.SetupCompleted = true;
        SettingsService.Save(_settings);
        Close(true);
    }

    private bool ValidateAndPersistCurrentStep()
    {
        switch (_stepIndex)
        {
            case 0:
                if (_settings.Languages.Count == 0) return false;
                _settings.Languages = _settings.Languages.Distinct().ToList();
                SettingsService.Save(_settings);
                break;
            case 1:
                if (_modelCombo?.SelectedItem is ModelOption opt && !string.IsNullOrWhiteSpace(opt.GgmlName))
                {
                    // Store the enum name if available
                    if (Enum.TryParse<GgmlType>(opt.GgmlName, out var parsed))
                    {
                        _settings.WhisperModel = parsed.ToString();
                        SettingsService.Save(_settings);
                    }
                }
                break;
            case 2:
                if (!ValidateAndPersistChatSettings()) return false;
                break;
            case 3:
                // mac extra step - nothing yet
                break;
            case 4:
                break;
        }
        return true;
    }

    private bool ValidateAndPersistChatSettings()
    {
    if (_chatEndpointBox == null || _chatApiKeyBox == null || _chatModelInput == null) return true;
        var endpoint = _chatEndpointBox.Text?.Trim() ?? string.Empty;
        var key = _chatApiKeyBox.Text?.Trim() ?? string.Empty;
    var model = _chatModelInput.Text?.Trim() ?? string.Empty;
    var provider = _chatProviderCombo?.SelectedItem?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _) || !(endpoint.StartsWith("http://") || endpoint.StartsWith("https://")))
        {
            _chatValidationMessage!.Text = "Enter valid HTTP(S) endpoint.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            _chatValidationMessage!.Text = "Model required.";
            return false;
        }
    var pLower = provider.ToLowerInvariant();
    var needsKey = pLower.Contains("openai") || pLower.Contains("openrouter");
        if (needsKey && string.IsNullOrWhiteSpace(key))
        {
            _chatValidationMessage!.Text = "API key required for this provider.";
            return false;
        }
        _chatValidationMessage!.Text = string.Empty;

        _settings.ChatApiBase = endpoint;
        _settings.ChatApiKey = key;
    _settings.ChatModel = model;
        _settings.ChatProvider = provider;
        SettingsService.Save(_settings);
        return true;
    }

#if MACOS || OSX || MACCATALYST
    // Helper to update mac setup status (can be called by future mac-specific services)
    private void UpdateMacSetupStatus(string message, double? progress = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_macStatusText != null) _macStatusText.Text = message;
            if (_macProgressBar != null)
            {
                if (progress.HasValue)
                {
                    if (!_macProgressBar.IsVisible) _macProgressBar.IsVisible = true;
                    _macProgressBar.IsIndeterminate = false;
                    _macProgressBar.Maximum = 1;
                    _macProgressBar.Value = Math.Clamp(progress.Value, 0, 1);
                }
                else
                {
                    _macProgressBar.IsVisible = false;
                }
            }
        });
    }
#endif
}