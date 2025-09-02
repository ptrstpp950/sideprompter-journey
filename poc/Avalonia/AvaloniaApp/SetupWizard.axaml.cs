using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Whisper.net.Ggml;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public partial class SetupWizard : Window
{
    private int _stepIndex;
    private readonly List<Func<Control>> _steps;
    private readonly AppSettings _settings;

    // Step controls state
    private ListBox? _languagesList;
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
        LoadStep();
    }

    private Control BuildLanguagesStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Choose languages to enable (at least one):", FontWeight = Avalonia.Media.FontWeight.Bold });
        _languagesList = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = new[] { "en", "pl", "es", "fr", "de", "it" }
        };
        var list = _languagesList; // may be null per analyzer
        if (list != null)
        {
            var itemsSource = (list.ItemsSource as IEnumerable<string>) ?? Array.Empty<string>();
            var selectedItems = list.SelectedItems; // capture reference
            foreach (var item in itemsSource)
            {
                if (_settings.Languages.Contains(item))
                {
                    selectedItems?.Add(item);
                }
            }
            panel.Children.Add(list);
        }
        panel.Children.Add(new TextBlock{Text="English (en) recommended for best smaller model performance."});
        return panel;
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
                if (_languagesList == null) return false;
                var selected = (_languagesList?.SelectedItems?.Cast<string>() ?? Array.Empty<string>()).ToList();
                if (selected.Count == 0) return false; // require at least one
                _settings.Languages = selected;
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