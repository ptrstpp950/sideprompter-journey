using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    private readonly AudioTranscriptionService _transcriptionService;
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private HotKeyService? _hotKeyService;

    public MainWindow()
    {
        InitializeComponent();
        _transcriptionService = new AudioTranscriptionService();
        _transcriptionService.MessageGenerated += OnMessageGenerated;
        LanguageComboBox.ItemsSource = _supportedLanguages;
        LanguageComboBox.SelectedIndex = 0;
        
        // Register global ALT+? hotkey
        _hotKeyService = new HotKeyService(this);
        
        try
        {
            _hotKeyService.RegisterGlobalHotKey(Key.OemQuestion, KeyModifiers.Alt, OnHotKeyPressed);
        }
        catch (Exception ex)
        {
            // Log the error but continue execution
            Console.WriteLine($"Failed to register global hotkey: {ex.Message}");
        }
    }

    private void OnMessageGenerated(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += message + Environment.NewLine;
        });
    }

    private async void ToggleButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!_isTranscribing)
        {
            _isTranscribing = true;
            ToggleButton.Content = "Stop Transcription";
            MessageTextBlock.Text = "";
            var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "en";
            await Task.Run(() => _transcriptionService.StartProcessing(selectedLanguage));
        }
    }



    private void ToggleButton_OnUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_isTranscribing)
        {
            _transcriptionService.StopProcessing();
            _isTranscribing = false;
            ToggleButton.Content = "Start Transcription";
        }
    }

    private void OnHotKeyPressed()
    {
        // Toggle the transcription state when ALT+? is pressed
        if (_isTranscribing)
        {
            ToggleButton.IsChecked = false;
        }
        else
        {
            ToggleButton.IsChecked = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotKeyService?.Dispose();
        base.OnClosed(e);
    }
}