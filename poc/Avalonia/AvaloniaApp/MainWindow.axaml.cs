using Avalonia.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
        _transcriptionService = new AudioTranscriptionService();
        _transcriptionService.MessageGenerated += OnMessageGenerated;
        LanguageComboBox.ItemsSource = _supportedLanguages;
        LanguageComboBox.SelectedIndex = 0;
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
}