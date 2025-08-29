using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    private readonly AudioTeeTranscriptionService _transcriptionService;
    private readonly IWindowTextExtractionService _windowTextExtractionService;
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private IHotKeyService? _hotKeyService;
    private bool _windowCaptureHotkeyRegistered = false;

    public MainWindow()
    {
        InitializeComponent();
        _transcriptionService = new AudioTeeTranscriptionService(new AudioTeeOptions(){SampleRate = 16000});
        _transcriptionService.MessageGenerated += OnMessageGenerated;
        _transcriptionService.StatusChanged += OnMessageGenerated;
        
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
        try
        {
#if MACOS || OSX || MACCATALYST
            _hotKeyService = new HotKeyServiceMacOptionTwo(this);
            _hotKeyService.RegisterStartRecordingHotKey(Key.OemQuestion, KeyModifiers.Meta, OnHotKeyPressed);
#elif WINDOWS
            _hotKeyService = new HotKeyServiceWindows(this);
            _hotKeyService.RegisterStartRecordingHotKey(Key.OemQuestion, KeyModifiers.Alt, OnHotKeyPressed);
#endif
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
    
    private async void GetWindowTextCliButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExtractAndDisplayWindowTextViaCli();
    }
    
    private async Task ExtractAndDisplayWindowTextViaCli()
    {
        try
        {
            AddMessage("--- Running CLI tool to extract window text ---");
            
            var binaryPath = GetActiveWindowTextGetterBinaryPath();
            
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = binaryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = processStartInfo };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                AddMessage($"CLI Output:");
                AddMessage(output);
            }
            else
            {
                AddMessage($"CLI Error (Exit Code: {process.ExitCode}):");
                if (!string.IsNullOrEmpty(error))
                    AddMessage(error);
                if (!string.IsNullOrEmpty(output))
                    AddMessage(output);
            }
            
            AddMessage("--- End of CLI output ---");
        }
        catch (Exception ex)
        {
            AddMessage($"Error running CLI tool: {ex.Message}");
        }
    }
    
    private string GetActiveWindowTextGetterBinaryPath()
    {
        // For macOS app bundles, check Resources first
        if (OperatingSystem.IsMacOS())
        {
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                "..", "Resources", "libs", "activeWindowTextGetter", "bin", "activeWindowTextGetter");
            if (File.Exists(resourcesPath))
                return resourcesPath;
        }

        // For regular builds, try the libs directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var libsPath = Path.Combine(baseDir, "libs", "activeWindowTextGetter", "bin", "activeWindowTextGetter");
        
        if (File.Exists(libsPath))
            return libsPath;

        // Fallback to looking in the current directory structure
        var currentDir = Directory.GetCurrentDirectory();
        var projectPath = Path.Combine(currentDir, "libs", "activeWindowTextGetter", "bin", "activeWindowTextGetter");
        
        if (File.Exists(projectPath))
            return projectPath;

        // Last resort - assume it's in PATH
        return "activeWindowTextGetter";
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
            if (_hotKeyService != null && !_windowCaptureHotkeyRegistered)
            {
                _hotKeyService.RegisterWindowCaptureHotKey(Key.OemPeriod, KeyModifiers.Meta, OnWindowTextHotkeyPressed);
                _windowCaptureHotkeyRegistered = true;
                AddMessage("Registered CMD+. hotkey for window text extraction");
                
                RegisterHotkeyButton.Content = "Unregister Hotkey";
            }
            else if (_hotKeyService != null && _windowCaptureHotkeyRegistered)
            {
                _hotKeyService.UnregisterWindowCaptureHotKey();
                _windowCaptureHotkeyRegistered = false;
                AddMessage("Unregistered CMD+. hotkey");
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
        await ExtractAndDisplayWindowTextViaCli();
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



    private async void ToggleButton_OnUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_isTranscribing)
        {
            await _transcriptionService.StopProcessing();
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