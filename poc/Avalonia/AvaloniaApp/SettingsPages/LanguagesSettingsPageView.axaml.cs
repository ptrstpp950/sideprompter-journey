using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AvaloniaApp.Settings;
using HotAvalonia;

namespace AvaloniaApp;

public partial class LanguagesSettingsPageView : UserControl
{
    private readonly AppSettings _settings;

    // Backing fields for named controls (avoid null if name fields not auto-generated)
    
    public LanguagesSettingsPageView() : this(SettingsService.Load()) { }

    public LanguagesSettingsPageView(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        // Resolve controls explicitly (covers scenarios where generated fields are not present)
        
        EnsureSystemLanguagesIfEmpty();

        SearchBox.TextChanged += (_, _) => RefreshAllList();
        AllList.DoubleTapped += (_, _) => AddSelected();
        AddButton.Click += (_, _) => AddSelected();
        UndoButton.Click += (_, _) => UndoLast();
        ClearButton.Click += (_, _) => { _settings.Languages.Clear(); RefreshSelected(); RefreshAllList(); };
        RefreshSelected();
        RefreshAllList();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void EnsureSystemLanguagesIfEmpty()
    {
        if (_settings.Languages.Count > 0) return;
        var available = new HashSet<string>(WhisperLanguages.All.Select(l => l.Code));
        var picked = new List<string>();
        void TryAdd(CultureInfo ci)
        {
            if (ci == null) return;
            var code = ci.TwoLetterISOLanguageName.ToLowerInvariant();
            if (available.Contains(code) && !picked.Contains(code)) picked.Add(code);
        }
        TryAdd(CultureInfo.CurrentUICulture);
        TryAdd(CultureInfo.CurrentCulture);
        try { TryAdd(CultureInfo.InstalledUICulture); } catch { }
        if (!picked.Contains("en") && available.Contains("en")) picked.Add("en");
        if (picked.Count == 0) picked.Add("en");
        _settings.Languages.AddRange(picked);
    }

    [AvaloniaHotReload]
    private void RefreshSelected()
    {
        if (SelectedPanel == null) return;

        SelectedPanel.Children.Clear();
        foreach (var code in _settings.Languages.ToList())
        {
            var lang = WhisperLanguages.All.FirstOrDefault(l => l.Code == code).Name;
            var tag = new Border
            {
                Background = Brushes.DimGray,
                CornerRadius = new Avalonia.CornerRadius(10),
                Padding = new Avalonia.Thickness(10, 6),
                Margin = new Avalonia.Thickness(4),
                Child = new TextBlock { Text = code.ToUpper() + " - " + lang, FontSize = 12 }
            };
            tag.PointerPressed += (_, _) => { _settings.Languages.Remove(code); RefreshSelected(); RefreshAllList(); };
            SelectedPanel.Children.Add(tag);
        }
    }
    [AvaloniaHotReload]
    private void RefreshAllList()
    {
        var filter = SearchBox.Text?.Trim().ToLowerInvariant();
        IEnumerable<(string Code, string Name)> data = WhisperLanguages.All;
        if (!string.IsNullOrWhiteSpace(filter))
            data = data.Where(l => l.Code.Contains(filter!) || l.Name.ToLowerInvariant().Contains(filter!));
        data = data.Where(d => !_settings.Languages.Contains(d.Code)).Take(200);
        AllList.ItemsSource = data.Select(d => $"{d.Code.ToUpper()} – {d.Name}").ToList();
    }

    private void AddSelected()
    {
        if (AllList.SelectedItem is string s)
        {
            var code = s.Split('–')[0].Trim().ToLowerInvariant();
            if (!_settings.Languages.Contains(code))
            {
                _settings.Languages.Add(code);
                RefreshSelected();
                RefreshAllList();
            }
        }
    }

    private void UndoLast()
    {
        if (_settings.Languages.Count > 0)
        {
            _settings.Languages.RemoveAt(_settings.Languages.Count - 1);
            RefreshSelected();
            RefreshAllList();
        }
    }
}
