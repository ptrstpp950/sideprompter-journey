using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApp.Settings;

namespace AvaloniaApp;

public class LanguagesSettingsPage : SettingsPageViewModel
{
    private readonly AppSettings _settings;
    private readonly TextBox _searchBox;
    private readonly ListBox _allList;
    private readonly WrapPanel _selectedPanel;
    private readonly TextBlock _countsText;
    private readonly Action _updateCounts;

    public LanguagesSettingsPage(AppSettings settings) : base("Languages", "🌐", new Grid())
    {
        _settings = settings;
        EnsureSystemLanguagesIfEmpty();
        var root = (Grid)View;
        // Layout: header, selected tags, search, list, buttons, tip
        root.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto");
        root.RowSpacing = 8;

        // Header
        var header = new TextBlock { Text = "Languages", FontSize = 18, FontWeight = FontWeight.Bold };
        root.Children.Add(header); Grid.SetRow(header, 0);

        // Selected tags section
        _selectedPanel = new WrapPanel();
        _countsText = new TextBlock { FontSize = 11, Opacity = 0.75 };
        var selectedSection = new StackPanel { Spacing = 4 };
        selectedSection.Children.Add(new TextBlock { Text = "Selected languages (click to remove):", FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Avalonia.Thickness(0,4,0,0) });
        var selectedScroll = new ScrollViewer { Content = _selectedPanel, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        selectedSection.Children.Add(selectedScroll);
        selectedSection.Children.Add(_countsText);
        root.Children.Add(selectedSection); Grid.SetRow(selectedSection, 1);

        // Search row
        _searchBox = new TextBox { Watermark = "type to filter", Width = 240 };
        _searchBox.TextChanged += (_, _) => RefreshAllList();
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        searchRow.Children.Add(new TextBlock { Text = "Search:" });
        searchRow.Children.Add(_searchBox);
        root.Children.Add(searchRow); Grid.SetRow(searchRow, 2);

        // List
        _allList = new ListBox { Margin = new Avalonia.Thickness(0,4,0,0) };
        _allList.DoubleTapped += (_, _) => AddSelected();
        root.Children.Add(_allList); Grid.SetRow(_allList, 3);

        // Buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var addBtn = new Button { Content = "Add" }; addBtn.Click += (_, _) => AddSelected();
        var undoBtn = new Button { Content = "Undo Last" }; undoBtn.Click += (_, _) => RemoveSelectedTag();
        var clearBtn = new Button { Content = "Clear All" }; clearBtn.Click += (_, _) => { _settings.Languages.Clear(); RefreshSelected(); RefreshAllList(); };
        btnRow.Children.Add(addBtn); btnRow.Children.Add(undoBtn); btnRow.Children.Add(clearBtn);
        root.Children.Add(btnRow); Grid.SetRow(btnRow, 4);

        // Tip
        var tip = new TextBlock { Text = "Tip: English (en) recommended for interface.", FontSize = 11, FontStyle = FontStyle.Italic, Opacity = 0.85 };
        root.Children.Add(tip); Grid.SetRow(tip, 5);

        // Counts updater
        _updateCounts = () => _countsText.Text = $"Selected: {_settings.Languages.Count} • Showing: {_allList.ItemCount}";
        _allList.PropertyChanged += (_, e) => { if (e.Property == SelectingItemsControl.ItemCountProperty) _updateCounts(); };

        RefreshSelected();
        RefreshAllList();
    }

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
        _updateCounts();
    }

    private void RefreshAllList()
    {
        var filter = _searchBox.Text?.Trim().ToLowerInvariant();
        IEnumerable<(string Code, string Name)> data = WhisperLanguages.All;
        if (!string.IsNullOrWhiteSpace(filter))
            data = data.Where(l => l.Code.Contains(filter!) || l.Name.ToLowerInvariant().Contains(filter!));
        data = data.Where(d => !_settings.Languages.Contains(d.Code)).Take(200);
        _allList.ItemsSource = data.Select(d => $"{d.Code} – {d.Name}").ToList();
        _updateCounts();
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

    private void RemoveSelectedTag()
    {
        if (_settings.Languages.Count > 0)
        {
            _settings.Languages.RemoveAt(_settings.Languages.Count - 1);
            RefreshSelected();
            RefreshAllList();
        }
    }

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
        try { TryAdd(CultureInfo.InstalledUICulture); } catch { /* ignore */ }
        if (!picked.Contains("en") && available.Contains("en")) picked.Add("en"); // fallback English
        if (picked.Count == 0) picked.Add("en");
        _settings.Languages.AddRange(picked);
    }

    public override bool ValidateAndSave(out string errorMessage)
    {
        if (_settings.Languages.Count == 0)
        {
            errorMessage = "Select at least one language.";
            return false;
        }
        SettingsService.Save(_settings);
        errorMessage = string.Empty;
        return true;
    }
}
