using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;
using AvaloniaApp.Settings;
using OpenAI;
using AvaloniaApp.Services;

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
    private readonly bool _isSettingsMode;
    private ComboBox _provider = null!;
    private TextBox _endpoint = null!;
    private TextBox _apiKey = null!;
    private ListBox _modelList = null!;
    private TextBox _modelSearch = null!;
    private TextBlock _status = null!;
    private List<string> _availableModels = new();
    private static readonly HttpClient _http = new();

    public ChatSettingsPage(AppSettings settings, bool isSettingsMode) : base("Chat", "💬", new StackPanel { Spacing = 8 })
    {
        _settings = settings;
        _isSettingsMode = isSettingsMode;

        // The base constructor gives us a StackPanel as View. We'll place a ScrollViewer inside it
        var outer = (StackPanel)View;

        var pageScroll = new ScrollViewer();
        pageScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        var content = new StackPanel { Spacing = 8 };
        pageScroll.Content = content;
        outer.Children.Add(pageScroll);

        // Instruction panel (hidden by default)
        var instrPanel = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        instrPanel.Children.Add(new TextBlock { Text = "Use LLM for free in Side Prompter", FontSize = 16 });
        instrPanel.Children.Add(new Separator());
        instrPanel.Children.Add(new TextBlock { Text = "A step-by-step guide on how to get a FREE API key to use OpenRouter models:" });
        var ol = new StackPanel { Spacing = 6 };
        ol.Children.Add(new TextBlock { Text = "1. Sign up for an account - Visit OpenRouter and create an account:", TextWrapping = TextWrapping.Wrap });
        var link1 = new TextBlock { Text = "https://openrouter.ai/", Foreground = TryFindBrush("SystemAccentColor") ?? Brushes.Blue };
        link1.PointerPressed += (_, _) => OpenUrl("https://openrouter.ai/");
        ol.Children.Add(link1);
        ol.Children.Add(new TextBlock { Text = "2. Navigate to the API Keys section:", TextWrapping = TextWrapping.Wrap });
        var link2 = new TextBlock { Text = "https://openrouter.ai/settings/keys", Foreground = TryFindBrush("SystemAccentColor") ?? Brushes.Blue };
        link2.PointerPressed += (_, _) => OpenUrl("https://openrouter.ai/settings/keys");
        ol.Children.Add(link2);
        ol.Children.Add(new TextBlock { Text = "3. Generate New API Key - Click 'Create Key' and save it somewhere safe." });
        ol.Children.Add(new TextBlock { Text = "4. Enter your API key into SidePrompter settings below." });
        ol.Children.Add(new TextBlock { Text = "5. When choosing a model, search for \"free\" like:" });
        ol.Children.Add(new TextBlock { Text = "   - `openai/gpt-oss-120b:free` to use free version of the ChatGPT model." });
        ol.Children.Add(new TextBlock { Text = "   - `microsoft/mai-ds-r1:free` DeepSeek-R1 developed by the Microsoft AI team." });
        ol.Children.Add(new TextBlock { Text = "   - `x-ai/grok-4-fast:free` to use free version of the Grok model." });
        instrPanel.Children.Add(ol);
        instrPanel.Children.Add(new Separator());

        // Help button at the top that toggles the instruction panel
        var helpStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        helpStack.Children.Add(new TextBlock { Text = "Need help setting up LLM?", FontWeight = FontWeight.Bold });
        var helpBtn = new Button
        {
            Content = "I need help to setup LLM",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(8, 4)
        };

        // Try to use the app's theme accent colors (Avalonia Fluent/Flat resource keys)
        IBrush? TryFindBrush(string key)
        {
            var theme = Application.Current?.ActualThemeVariant;
            if (Application.Current?.TryFindResource(key, theme, out var r) == true)
            {
                return r switch
                {
                    IBrush b => b,
                    Color c => new SolidColorBrush(c),
                    _ => null
                };
            }
            return null;
        }

        var accent = TryFindBrush("SystemAccentColor") ?? TryFindBrush("SystemAccentColorDark1");
        var accentFg = TryFindBrush("SystemAccentColorForeground") ?? Brushes.White;
        if (accent != null) helpBtn.Background = accent;
        helpBtn.Foreground = accentFg;
        helpStack.Children.Add(helpBtn);

        helpBtn.Click += (_, _) =>
        {
            if (!content.Children.Contains(instrPanel)) content.Children.Insert(1, instrPanel);
            else content.Children.Remove(instrPanel);
        };

        // Build main UI below the help button so fields are visible immediately
        content.Children.Add(helpStack);
        BuildMainUI(content);

        // Preselect OpenRouter in simple (wizard) mode and clear key placeholder
        if (!_isSettingsMode)
        {
            try
            {
                _provider.SelectedItem = "OpenRouter";
                SuggestEndpoint();
                _apiKey.Text = string.Empty;
                if (!string.IsNullOrWhiteSpace(_settings.ActiveChatProviderConfig?.Model))
                    _modelList.SelectedItem = _settings.ActiveChatProviderConfig.Model;
            }
            catch { }
        }
        else
        {
            SuggestEndpoint();
            LoadProviderApiKey();
        }
    }

    private void BuildMainUI(StackPanel root)
    {
        root.Children.Add(new TextBlock { Text = "Chat Completion Service", FontWeight = FontWeight.Bold });

        _provider = new ComboBox { ItemsSource = new[] { "OpenAI", "OpenRouter", "Ollama", "Other" }, SelectedIndex = 0, Width = 160 };
        if (!string.IsNullOrWhiteSpace(_settings.ChatProvider))
        {
            var idx = (_provider.ItemsSource as IEnumerable<string>)!.ToList().FindIndex(p => p.Equals(_settings.ChatProvider, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _provider.SelectedIndex = idx;
        }
        _provider.SelectionChanged += (_, _) => { SuggestEndpoint(); LoadProviderApiKey(); };

        _endpoint = new TextBox { Watermark = "API Base URL", Text = _settings.ActiveChatProviderConfig?.ApiBase ?? string.Empty };
        _apiKey = new TextBox { Watermark = "API Key", Text = _settings.ActiveChatProviderConfig?.ApiKey ?? string.Empty, PasswordChar = '•' };
        _modelList = new ListBox { SelectionMode = SelectionMode.Single, Height = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        _modelSearch = new TextBox { Watermark = "Search models", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        _modelList.SelectionChanged += (_, _) => { /* selection is the model; no separate textbox required */ };
        _modelSearch.KeyUp += (_, _) => FilterModels(_modelSearch.Text);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            // Added an extra row for the Fetch Models button between API Key and Model
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto")
        };
        void Row(int r, string label, Control c)
        {
            // increase vertical spacing between rows by adding top margin to controls and labels
            var lbl = new TextBlock { Text = label, Margin = new Avalonia.Thickness(0, 8, 8, 0), VerticalAlignment = (r == 4) ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center };
            grid.Children.Add(lbl); Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
            // ensure control has a small top margin so rows have visible gaps
            c.Margin = new Avalonia.Thickness(0, 6, 0, 0);
            grid.Children.Add(c); Grid.SetRow(c, r); Grid.SetColumn(c, 1);
        }
        Row(0, "Provider:", _provider);
        Row(1, "Endpoint:", _endpoint);
        Row(2, "API Key:", _apiKey);
        // Model list area: search box above the selectable list; selection is the chosen model
        var modelStack = new StackPanel { Spacing = 6, Orientation = Orientation.Vertical };
        // add a small search box above the list like in Prompts settings
        modelStack.Children.Add(_modelSearch);
        modelStack.Children.Add(_modelList);
        // Place the model stack in the last row (row 4)
        Row(4, "Model:", modelStack);
        root.Children.Add(grid);
        // Create the Fetch Models button and place it between API Key and Model in the grid (row 3)
        var fetchBtn = new Button { Content = "Fetch Models", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        fetchBtn.Click += async (_, _) => await FetchModels();
        grid.Children.Add(fetchBtn); Grid.SetRow(fetchBtn, 3); Grid.SetColumn(fetchBtn, 1);

        // Keep Verify button below the grid
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var testBtn = new Button { Content = "Verify" }; testBtn.Click += async (_, _) => await VerifyChat();
        btnRow.Children.Add(testBtn);
        root.Children.Add(btnRow);

        _status = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 12 };
        root.Children.Add(_status);
    }

    private void OpenUrl(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch { }
    }

    private void LoadProviderApiKey()
    {
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(provider)) { _apiKey.Text = string.Empty; return; }
        // Try to load provider-specific key from settings.ChatApiKeys if available
        try
        {
            // normalize provider key to lower-case for storage and lookup
            var providerKey = provider;
            if (_settings.ChatProviders != null && _settings.ChatProviders.TryGetValue(providerKey, out var cfg))
            {
                _apiKey.Text = cfg.ApiKey ?? string.Empty;
                _endpoint.Text = cfg.ApiBase ?? _endpoint.Text;
                // Try to preselect model from provider config if available.
                // Ensure the model is visible in the list even if models haven't been fetched yet.
                if (!string.IsNullOrWhiteSpace(cfg.Model))
                {
                    try
                    {
                        // If we have some available models, ensure the saved model is present in the list
                        if (_availableModels != null && _availableModels.Count > 0)
                        {
                            if (!_availableModels.Contains(cfg.Model))
                            {
                                // show the saved model at the top so it's visible to the user
                                var items = new List<string> { cfg.Model };
                                items.AddRange(_availableModels);
                                _modelList.ItemsSource = items;
                            }
                            else
                            {
                                _modelList.ItemsSource = _availableModels;
                            }
                        }
                        else
                        {
                            // no fetched models yet — show the saved model so user sees it
                            _modelList.ItemsSource = new List<string> { cfg.Model };
                        }

                        _modelList.SelectedItem = cfg.Model;
                    }
                    catch { /* fail gracefully */ }
                }
                return;
            }
            else
            {
                _apiKey.Text = string.Empty;
                _modelList.ItemsSource = null;
                _modelList.SelectedItem = null;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void SuggestEndpoint()
    {
        var provider = _provider.SelectedItem?.ToString()?.ToLowerInvariant() ?? string.Empty;
        switch (provider)
        {
            case "openai":
                // Use base API URL; endpoints are appended elsewhere in the code
                _endpoint.Text = "https://api.openai.com/v1";
                _endpoint.IsEnabled = false;
                break;
            case "openrouter":
                _endpoint.Text = "https://openrouter.ai/api/v1";
                _endpoint.IsEnabled = false;
                break;
            case "ollama":
                _endpoint.Text = "http://localhost:11434/v1";
                _endpoint.IsEnabled = false;
                break;
            default:
                // "Other" or unknown provider: allow user to configure the full base URL
                _endpoint.IsEnabled = true;
                if (!string.IsNullOrWhiteSpace(_settings.ActiveChatProviderConfig?.ApiBase))
                {
                    _endpoint.Text = _settings.ActiveChatProviderConfig.ApiBase;
                }
                break;
        }
    }

    private async System.Threading.Tasks.Task FetchModels()
    {
        try
        {
            var apiKey = _apiKey.Text?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "-";
            }
            var endpoint = _endpoint.Text?.Trim() ?? string.Empty;

            var modelResponse = await new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),

                })
                .GetOpenAIModelClient()
                .GetModelsAsync();
            var models = modelResponse.Value.ToList();

            _availableModels = models.Select(m => m.Id).ToList();
            FilterModels(null);
            _status.Text = _availableModels.Count == 0 ? "No models" : $"{_availableModels.Count} models";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void FilterModels(string? filter)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                _modelList.ItemsSource = _availableModels;
                return;
            }
            var lower = filter.ToLowerInvariant();
            var matches = _availableModels.Where(m => m.ToLowerInvariant().Contains(lower)).ToList();
            _modelList.ItemsSource = matches;
        }
        catch { }
    }

    private async System.Threading.Tasks.Task VerifyChat()
    {
        if (!ValidateAndSave(out var err)) { _status.Text = err; return; }
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;
        _status.Text = "Testing...";
        try
        {
            var endpoint = _endpoint.Text?.Trim() ?? string.Empty;
            var apiKey = _apiKey.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "-";
            }

            var selectedModel = (_modelList.SelectedItem as string) ?? _settings.ActiveChatProviderConfig?.Model ?? string.Empty;
            var service = new ChatCompletionService(endpoint, apiKey, selectedModel);

            var result = await service.TestAsync("Ping");

            _status.Text = result.StartsWith("Error:") ? result : "Success";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        var endpoint = _endpoint.Text?.Trim() ?? string.Empty;
        var model = (_modelList.SelectedItem as string)?.Trim() ?? _modelSearch.Text?.Trim() ?? string.Empty;
        var provider = _provider.SelectedItem?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _)) { errorMessage = "Enter valid endpoint"; return false; }
        if (string.IsNullOrWhiteSpace(model)) { errorMessage = "Enter model"; return false; }

        var needsKey = provider.Equals("openai", StringComparison.OrdinalIgnoreCase) || provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);
        if (needsKey && string.IsNullOrWhiteSpace(_apiKey.Text)) { errorMessage = "API key required"; return false; }

        // Save provider-specific config
        try
        {
            var cfg = new ChatProviderConfig
            {
                ProviderName = provider,
                ApiBase = endpoint,
                ApiKey = string.IsNullOrWhiteSpace(_apiKey.Text) ? string.Empty : _apiKey.Text.Trim(),
                Model = model
            };
            _settings.ChatProviders ??= new Dictionary<string, ChatProviderConfig>();
            _settings.ChatProviders[provider] = cfg;
            _settings.ChatProvider = provider;
        }
        catch { }
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
