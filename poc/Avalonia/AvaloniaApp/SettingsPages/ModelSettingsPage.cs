using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApp.Settings;
using Whisper.net.Ggml;

namespace AvaloniaApp;

public class ModelSettingsPage : SettingsPageViewModel
{
    private readonly AppSettings _settings;
    private readonly ComboBox _modelCombo;
    private readonly TextBlock _desc;
    private readonly TextBlock _warning;

    private record ModelOpt(string Key, string Label, string Params, string Vram, string Speed, string Ggml);
    private List<ModelOpt> _opts = new();

    public ModelSettingsPage(AppSettings settings) : base("Model", "🧠", new StackPanel { Spacing = 8 })
    {
        _settings = settings;
        var root = (StackPanel)View;
        root.Children.Add(new TextBlock { Text = "Choose Whisper model", FontWeight = FontWeight.Bold });
        // English-only variants removed; always show multilingual models.
        _opts = BuildOptions();
        _modelCombo = new ComboBox { ItemsSource = _opts, SelectedIndex = 0, Width = 340, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, ItemTemplate = BuildTemplate() };
        var pre = _opts.FirstOrDefault(o => string.Equals(o.Ggml, settings.WhisperModel, StringComparison.OrdinalIgnoreCase));
        if (pre == null && !string.IsNullOrWhiteSpace(settings.WhisperModel) && settings.WhisperModel.EndsWith("En", StringComparison.OrdinalIgnoreCase))
        {
            // Map legacy English-only model to its multilingual counterpart by trimming the 'En' suffix.
            var trimmed = settings.WhisperModel[..^2];
            pre = _opts.FirstOrDefault(o => string.Equals(o.Ggml, trimmed, StringComparison.OrdinalIgnoreCase));
            if (pre != null)
            {
                // Persist migration immediately so future loads match directly.
                settings.WhisperModel = pre.Ggml;
                SettingsService.Save(settings);
            }
        }
        
        if (pre != null) _modelCombo.SelectedItem = pre;
        _modelCombo.SelectionChanged += (_, _) => UpdateDesc();
        _warning = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Text = "⚠ Warning: Do not change this setting unless you understand the implications. Changing the model can affect transcription accuracy and resource usage." };
        root.Children.Add(_warning);
    
        root.Children.Add(_modelCombo);
        _desc = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        root.Children.Add(_desc);
        
        UpdateDesc();
        // Removed explanatory text about English-only variants since they are no longer exposed.
    }

    private IDataTemplate BuildTemplate() => new FuncDataTemplate<ModelOpt>((o, _) => new StackPanel { Orientation = Orientation.Vertical, Children =
    {
        new TextBlock { Text = o.Label },
        new TextBlock { Text = $"Params: {o.Params}  VRAM: {o.Vram}  Speed: {o.Speed}", FontSize = 10, Foreground = Brushes.Gray }
    }} , true);

    private List<ModelOpt> BuildOptions()
    {
        var list = new List<ModelOpt>();
        void Add(string key,string p,string v,string s,string ggml){ if(Enum.GetNames(typeof(GgmlType)).Any(n=>n.Equals(ggml,StringComparison.OrdinalIgnoreCase))) list.Add(new ModelOpt(key,key,p,v,s,ggml)); }
        Add("tiny","39M","~1 GB","~10x","Tiny");
        Add("base","74M","~1 GB","~7x","Base");
        Add("small","244M","~2 GB","~4x","Small");
        Add("medium","769M","~5 GB","~2x","Medium");
        if (Enum.IsDefined(typeof(GgmlType),"LargeV3")) Add("large","1550M","~10 GB","1x","LargeV3");
        return list;
    }

    private void UpdateDesc()
    {
        if (_modelCombo.SelectedItem is ModelOpt m)
        {
            _desc.Text = $"{m.Key}: {m.Params} params, {m.Vram} VRAM, {m.Speed} relative speed.";
        }
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        if (_modelCombo.SelectedItem is not ModelOpt m)
        {
            errorMessage = "Select a model."; return false;
        }
        _settings.WhisperModel = m.Ggml;
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
