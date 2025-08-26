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
    private readonly IWindowTextExtractionService _windowTextExtractionService;
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private HotKeyService? _hotKeyService;
    private int? _windowTextHotkeyId;

    public MainWindow()
    {
        InitializeComponent();
        _transcriptionService = new AudioTranscriptionService();
        _transcriptionService.MessageGenerated += OnMessageGenerated;
#if MACOS || OSX || MACCATALYST
        _windowTextExtractionService = new WindowTextExtractionServiceMac();
#elif WINDOWS
        _windowTextExtractionService = new WindowTextExtractionServiceWin();
#else
        _windowTextExtractionService = new NoopWindowTextExtractionService();
#endif
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
        
        // Update the active window title
        UpdateActiveWindowTitle();
    }

    private void OnMessageGenerated(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += message + Environment.NewLine;
        });
    }
    
    private async void GetWindowTextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExtractAndDisplayWindowText();
    }
    
    private async Task ExtractAndDisplayWindowText()
    {
        try
        {
            string windowText = await _windowTextExtractionService.GetActiveWindowTextAsync();
            AddMessage($"--- Text from window: {_windowTextExtractionService.GetActiveWindowTitle()} ---");
            AddMessage(windowText);
            AddMessage("--- End of window text ---");
        }
        catch (Exception ex)
        {
            AddMessage($"Error extracting window text: {ex.Message}");
        }
    }
    
    private void RefreshWindowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        UpdateActiveWindowTitle();
    }
    
    private void UpdateActiveWindowTitle()
    {
        try
        {
            ActiveWindowTitleBlock.Text = _windowTextExtractionService.GetActiveWindowTitle();
        }
        catch (Exception ex)
        {
            ActiveWindowTitleBlock.Text = $"Error: {ex.Message}";
        }
    }
    
    private void RegisterHotkeyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_hotKeyService != null && _windowTextHotkeyId == null)
            {
                _windowTextHotkeyId = _hotKeyService.RegisterGlobalHotKey(Key.W, KeyModifiers.Alt, OnWindowTextHotkeyPressed);
                AddMessage("Registered ALT+W hotkey for window text extraction");
                RegisterHotkeyButton.Content = "Unregister Hotkey";
            }
            else if (_hotKeyService != null && _windowTextHotkeyId != null)
            {
                _hotKeyService.UnregisterGlobalHotKey(_windowTextHotkeyId.Value);
                _windowTextHotkeyId = null;
                AddMessage("Unregistered ALT+W hotkey");
                RegisterHotkeyButton.Content = "Register Hotkey";
            }
        }
        catch (Exception ex)
        {
            AddMessage($"Failed to register/unregister hotkey: {ex.Message}");
        }
    }
    
    private async void OnWindowTextHotkeyPressed()
    {
        await ExtractAndDisplayWindowText();
    }
    
    private void PrivacyMode_OnToggled(object? sender, RoutedEventArgs e)
    {
        bool isChecked = PrivacyModeCheckBox.IsChecked ?? false;
        WindowPrivacy.SetProtected(this, isChecked);
        AddMessage($"Privacy mode {(isChecked ? "enabled" : "disabled")}");
    }

    private void AddMessage(string message)
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