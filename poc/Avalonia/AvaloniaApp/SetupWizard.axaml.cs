using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Whisper.net.Ggml;
using AvaloniaApp.Settings;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;

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

    public SetupWizard() : this(new AppSettings()) {}

    public SetupWizard(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        _steps = new List<Func<Control>>
        {
            BuildLanguagesStep,
            BuildModelStep,
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
        panel.Children.Add(new TextBlock { Text = "Choose Whisper model to download:", FontWeight = Avalonia.Media.FontWeight.Bold });
    // Provide a curated list (avoid very large unless user later changes settings manually)
    var allowed = Enum.GetValues<GgmlType>().Where(t => t is GgmlType.Tiny or GgmlType.TinyEn or GgmlType.Base or GgmlType.BaseEn or GgmlType.Small or GgmlType.SmallEn || t.ToString().StartsWith("LargeV"));
    _modelCombo = new ComboBox { ItemsSource = allowed.ToList() };
        _modelCombo.SelectedItem = Enum.TryParse<GgmlType>(_settings.WhisperModel, out var model) ? model : GgmlType.Base;
        _modelCombo.SelectionChanged += (_, _) => UpdateModelDescription();
        _modelDescription = new TextBlock{TextWrapping = Avalonia.Media.TextWrapping.Wrap};
        panel.Children.Add(_modelCombo);
        panel.Children.Add(_modelDescription);
        UpdateModelDescription();
        return panel;
    }

    private void UpdateModelDescription()
    {
        if (_modelCombo?.SelectedItem is GgmlType t)
        {
            var desc = t switch
            {
                GgmlType.Tiny or GgmlType.TinyEn => "Fastest, lowest accuracy.",
                GgmlType.Base or GgmlType.BaseEn => "Balanced speed and accuracy (default).",
                GgmlType.Small or GgmlType.SmallEn => "Better accuracy, slower.",
                GgmlType.Medium or GgmlType.MediumEn => "High accuracy, slower.",
                GgmlType.LargeV3 or GgmlType.LargeV2 => "Best accuracy, slowest & large download.",
                _ => "General purpose model."
            };
            _modelDescription!.Text = desc;
        }
    }

    private Control BuildMacExtraStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "macOS extra step (reserved).", FontStyle = Avalonia.Media.FontStyle.Italic });
        panel.Children.Add(new TextBlock { Text = "We'll add content here later." });
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

    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (!ValidateAndPersistCurrentStep()) return;
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
                if (_modelCombo?.SelectedItem is GgmlType t)
                {
                    // if only english selected and not using *En variant for tiny/base/small/medium, map automatically
                    if (_settings.Languages.All(l => l == "en"))
                    {
                        // prefer the *En variant when available
                        if (t == GgmlType.Base) t = GgmlType.BaseEn;
                        if (t == GgmlType.Tiny) t = GgmlType.TinyEn;
                    }
                    _settings.WhisperModel = t.ToString();
                    SettingsService.Save(_settings);
                }
                break;
            case 2:
                // mac extra step - nothing yet
                break;
            case 3:
                break;
        }
        return true;
    }
}