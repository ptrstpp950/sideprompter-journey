using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public partial class LanguagesSettingsPageView : UserControl
{
    private readonly AppSettings _settings;

    // Backing fields for named controls (avoid null if name fields not auto-generated)
    private TextBox _searchBox = default!;
    private ListBox _allList = default!;
    private WrapPanel _selectedPanel = default!;
    private TextBlock _countsText = default!;
    private Button _addButton = default!;
    private Button _undoButton = default!;
    private Button _clearButton = default!;

    public LanguagesSettingsPageView() : this(SettingsService.Load()) { }

    public LanguagesSettingsPageView(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        // Resolve controls explicitly (covers scenarios where generated fields are not present)
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _allList = this.FindControl<ListBox>("AllList")!;
        _selectedPanel = this.FindControl<WrapPanel>("SelectedPanel")!;
        _countsText = this.FindControl<TextBlock>("CountsText")!;
        _addButton = this.FindControl<Button>("AddButton")!;
        _undoButton = this.FindControl<Button>("UndoButton")!;
        _clearButton = this.FindControl<Button>("ClearButton")!;

        EnsureSystemLanguagesIfEmpty();

        _searchBox.TextChanged += (_, _) => RefreshAllList();
        _allList.DoubleTapped += (_, _) => AddSelected();
        _addButton.Click += (_, _) => AddSelected();
        _undoButton.Click += (_, _) => UndoLast();
        _clearButton.Click += (_, _) => { _settings.Languages.Clear(); RefreshSelected(); RefreshAllList(); };
        _allList.PropertyChanged += (_, e) => { if (e.Property == SelectingItemsControl.ItemCountProperty) UpdateCounts(); };
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

    private void UpdateCounts()
        => _countsText.Text = $"Selected: {_settings.Languages.Count} • Showing: {_allList.ItemCount}";

    private void RefreshSelected()
    {
        _selectedPanel.Children.Clear();
        foreach (var code in _settings.Languages.ToList())
        {
            var lang = WhisperLanguages.All.FirstOrDefault(l => l.Code == code).Name;
            var tag = new Border
            {
                Background = Brushes.DimGray,
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8,4),
                Margin = new Avalonia.Thickness(2),
                Child = new TextBlock { Text = code + " • " + lang, FontSize = 12 }
            };
            tag.PointerPressed += (_, _) => { _settings.Languages.Remove(code); RefreshSelected(); RefreshAllList(); };
            _selectedPanel.Children.Add(tag);
        }
        UpdateCounts();
    }

    private void RefreshAllList()
    {
    var filter = _searchBox.Text?.Trim().ToLowerInvariant();
        IEnumerable<(string Code, string Name)> data = WhisperLanguages.All;
        if (!string.IsNullOrWhiteSpace(filter))
            data = data.Where(l => l.Code.Contains(filter!) || l.Name.ToLowerInvariant().Contains(filter!));
        data = data.Where(d => !_settings.Languages.Contains(d.Code)).Take(200);
    _allList.ItemsSource = data.Select(d => $"{d.Code} – {d.Name}").ToList();
        UpdateCounts();
    }

    private void AddSelected()
    {
    if (_allList.SelectedItem is string s)
        {
            var code = s.Split('–')[0].Trim();
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
