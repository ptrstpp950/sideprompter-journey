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

    public MainWindow()
    {
        InitializeComponent();
        _transcriptionService = new AudioTranscriptionService();
        _transcriptionService.MessageGenerated += OnMessageGenerated;
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
            await Task.Run(_transcriptionService.StartProcessing);
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