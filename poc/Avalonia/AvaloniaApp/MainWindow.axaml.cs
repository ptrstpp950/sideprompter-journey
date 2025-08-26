using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using System;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    UpdateStateUI();
    }

    private bool _toggled;

    private void OnChangeTextClicked(object? sender, RoutedEventArgs e)
    {
        _toggled = !_toggled;
        UpdateStateUI();
    // Apply platform-specific privacy.
    WindowPrivacy.SetProtected(this, _toggled);
    }

    private void UpdateStateUI()
    {
        if (MessageTextBlock != null)
            MessageTextBlock.Text = $"State: {(_toggled ? "On" : "Off")}";
        if (ToggleButton != null)
            ToggleButton.Content = _toggled ? "Turn Off" : "Turn On";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
    // Initial privacy state (off by default; change to true if you want it enabled from start)
    WindowPrivacy.SetProtected(this, _toggled);
    }
}