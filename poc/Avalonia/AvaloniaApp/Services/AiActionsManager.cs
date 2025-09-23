using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HeroIconsAvalonia.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaApp.ViewModel;
using AvaloniaApp.Settings;
using AvaloniaApp.Services.Chat;
using Serilog;

namespace AvaloniaApp.Services;

public class AiActionsManager : IDisposable
{

    private readonly Window _owner;
    private readonly ChatViewModel _chatViewModel;
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<ChatCompletionService?> _chatCompletionServiceAccessor;

    public AiActionsManager(Window owner, ChatViewModel chatViewModel, AppSettings settings, Func<ChatCompletionService?> chatCompletionServiceAccessor, ILogger logger)
    {
        _owner = owner;
        _chatViewModel = chatViewModel;
        _settings = settings;
        _chatCompletionServiceAccessor = chatCompletionServiceAccessor;
        _logger = logger;
    }


    private static object? FindInThemeDictionaries(string key)
    {
        var app = Application.Current;
        if (app == null) return null;

        var theme = app.ActualThemeVariant; // ThemeVariant.Light or ThemeVariant.Dark

        // Iterate resource dictionaries that may contain theme dictionaries.
        foreach (var style in app.Styles.OfType<ResourceDictionary>())
        {
            if (style.ThemeDictionaries != null
                && style.ThemeDictionaries.TryGetValue(theme, out var themeDict)
                && themeDict is ResourceDictionary resourceDictionary
                && resourceDictionary.ContainsKey(key))
            {
                return resourceDictionary[key];
            }

            // Some ResourceDictionaries may also have the key directly (not in ThemeDictionaries)
            if (style.ContainsKey(key))
                return style[key];
        }

        // fallback to TryFindResource if you want broader lookup
        app.TryFindResource(key, out var res);
        return res;
    }

    private static IBrush? TryGetBrush(string key)
    {
        var theme = Application.Current?.ActualThemeVariant;
        if (Application.Current?.TryFindResource(key, theme, out var res) == true)
        {
            var result = res switch
            {
                IBrush b => b,
                Color c => new SolidColorBrush(c),
                _ => null
            };
            return result;
        }
        return new SolidColorBrush(Colors.Pink);
    }

    public void InitializeButtons(Avalonia.Controls.Primitives.UniformGrid panel)
    {
        if (panel == null) return;
        panel.Children.Clear();

        // Constrain the panel to the owner window width so buttons don't exceed window bounds.
        try
        {
            if (_owner != null)
            {
                panel.MaxWidth = _owner.Bounds.Width;

                var desiredButtonWidth = 150.0;

                // Update MaxWidth when window size changes
                _owner.SizeChanged += (s, e) =>
                {
                    try
                    {
                        panel.MaxWidth = _owner.Bounds.Width;
                        // If the host is a UniformGrid, recompute Columns on resize
                        var cols = Math.Max(1, (int)(_owner.Bounds.Width / desiredButtonWidth));
                        panel.Columns = cols;
                    }
                    catch
                    {
                        _logger.Warning("Failed to set AiActionsManager button panel columns based on window width in SizeChanged event.");
                    }
                };

                // If the host is a UniformGrid, set Columns dynamically based on window width
                try
                {
                    // desired approximate button width (including spacing)
                    var cols = Math.Max(1, (int)(_owner.Bounds.Width / desiredButtonWidth));
                    panel.Columns = cols;
                }
                catch
                {
                    _logger.Warning("Failed to set AiActionsManager button panel columns based on window width initialization.");
                }
            }
        }
        catch
        {
            _logger.Warning("Failed to set AiActionsManager button panel max width based on window width unexpectedly.");
        }

        var buttonDefs = _settings.Prompts.Select(p => (Tag: p.Title, Text: p.Title, Tooltip: p.Title)).ToArray();

        var theme = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        var contentForeground = theme == ThemeVariant.Light ? TryGetBrush("SystemBaseHighColor") : TryGetBrush("SystemBaseHighColor");

            foreach (var def in buttonDefs)
        {
            var btn = new Button { Classes = { "HeaderChip" }, Height = 36, Tag = def.Tag, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            ToolTip.SetTip(btn, def.Tooltip);
            // Wrap click so we can show per-button loading state while the async action runs
            btn.Click += async (_, _) => await HandleButtonClickAsync(def.Tag, btn);

            var sp = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

            //TODO: Replace with appropriate icons from HeroIcons.Avalonia
            var icon = new HeroIcon { Width = 16, Height = 16, Kind = HeroIconsAvalonia.Enums.IconKind.Solid };
            switch (def.Tag)
            {
                case "quick_summary": icon.Type = HeroIconsAvalonia.Enums.IconType.ArrowPath; break;
                case "suggest_question": icon.Type = HeroIconsAvalonia.Enums.IconType.Cog6Tooth; break;
                case "response_coach": icon.Type = HeroIconsAvalonia.Enums.IconType.ChatBubbleLeftRight; break;
                case "action_items": icon.Type = HeroIconsAvalonia.Enums.IconType.PlusCircle; break;
                default: icon.Type = HeroIconsAvalonia.Enums.IconType.QuestionMarkCircle; break;
            }

            try { icon.Foreground = contentForeground; } catch { }

            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = def.Text, Foreground = contentForeground, FontSize = 11 });

            // Ensure content can expand; set minimum width so chips are usable but can wrap/stack
            btn.MinWidth = 80;
            // Add spacing between buttons and internal padding for nicer chip appearance
            btn.Margin = new Thickness(4, 2); // left/right = 4, top/bottom = 2
            btn.Padding = new Thickness(8, 4); // internal padding

            btn.Content = sp;
            panel.Children.Add(btn);
        }
    }

    // Shows an indeterminate progress indicator on the source button while asking AI.
    private async Task HandleButtonClickAsync(string kind, Button btn)
    {
        if (btn == null) return;

        var originalContent = btn.Content;
        var originalIsEnabled = btn.IsEnabled;

        // Create a small loading content (spinner replacement using an indeterminate ProgressBar)
        var fg = TryGetBrush("SystemBaseHighColor");
        var loadingSp = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var pb = new ProgressBar { Width = 18, Height = 12, IsIndeterminate = true, Margin = new Thickness(0, 8, 0, 8) };
        var txt = new TextBlock { Text = "Loading...", Foreground = fg, FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        loadingSp.Children.Add(pb);
        loadingSp.Children.Add(txt);

        // Set UI state to loading
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                btn.IsEnabled = false;
                btn.Content = loadingSp;
            }
            catch { }
        });

        try
        {
            await AskAiWithKindAsync(kind);
        }
        finally
        {
            // Restore UI state on UI thread
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    btn.Content = originalContent;
                    btn.IsEnabled = originalIsEnabled;
                }
                catch { }
            });
        }
    }

    public async Task AskAiWithKindAsync(string kind)
    {
        try
        {
            _chatViewModel.IsAsking = true;
            Dispatcher.UIThread.Post(() => { /* optionally set a UI placeholder */ });

            var messagesSnapshot = _chatViewModel.Messages.ToList();

            if(messagesSnapshot.Count == 0)
            {
                _logger.Warning("[AI Action] No messages in chat to provide context for AI. Please add some messages first.");
                App.Notifications.Show("No messages in chat to provide context for AI. Please add some messages first.");
                return;
            }

            var service = _chatCompletionServiceAccessor();
            if (service == null)
            {
                _logger.Warning("[Config] Chat completion not configured (missing base/model). Skipping AI response.");
                App.Notifications.Show(
                    "Chat completion not configured! Please edit settings to set a valid base URL and model.");
                return;
            }

            var selectedLanguage = "en";
            try
            {
                selectedLanguage = (_owner.FindControl<ComboBox>("LanguageComboBox")?.SelectedItem as string) ?? _settings.Languages?.FirstOrDefault() ?? "en";
            }
            catch { }
            service.Language = selectedLanguage;

            var promptSetting = _settings.Prompts?.FirstOrDefault(x => x.Title == kind);
            var prompt = promptSetting?.PromptText ?? kind;
            var promptId = promptSetting?.GetHash() ?? kind;

            var messages = messagesSnapshot
                .Where(m => m.Author != MessageAuthor.AiAssistant || (m.Author== MessageAuthor.AiAssistant && m.PromptId == promptId))
                .Select(m => $"[{(m.Author == MessageAuthor.Me ? "m" : m.Author == MessageAuthor.AiAssistant ? "ai" : "o")}] {m.Text}")
                .ToList();

            var chatResult = await service.GetCompletionAsync(prompt, messages);

            if (!string.IsNullOrWhiteSpace(chatResult))
            {
                _chatViewModel.AddAiMessage(chatResult, prompt);
                App.Notifications.Show(chatResult);
            }
        }
        catch (Exception ex)
        {
            _chatViewModel.AddLogMessage($"[Log][Exception] {ex.Message} {ex.StackTrace}");
        }
        finally
        {
            _chatViewModel.IsAsking = false;
        }
    }

    public void Dispose()
    {
        // noop for now
    }
}
