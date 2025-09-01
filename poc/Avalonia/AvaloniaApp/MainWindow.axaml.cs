using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaApp.Services.AudioTranscription;
using AvaloniaApp.Services.EnableWindowPrivacy;
using AvaloniaApp.Services.HotKey;
using AvaloniaApp.Services.TranscriptionService;
using AvaloniaApp.Services.WindowTextExtraction;
using Microsoft.Extensions.AI;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    private readonly IAudioTranscriptionService? _audioTranscriptionService;
    private readonly IWindowTextExtractionService _windowTextExtractionService;
    private readonly ChatCompletionService _chatCompletionService;
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private readonly IHotKeyService? _hotKeyService;
    // ReSharper disable once RedundantDefaultMemberInitializer
    private bool _windowCaptureHotkeyRegistered = false;

    private List<string> _messages = new();

    public MainWindow()
    {
        InitializeComponent();
        var transcriptionService = new WhisperTranscriptionService();
#if MACOS || OSX || MACCATALYST
        _audioTranscriptionService = new AudioTranscriptionServiceMac(transcriptionService);
        _windowTextExtractionService = new WindowTextExtractionServiceMac();
        _hotKeyService = new HotKeyServiceMacOptionTwo(this);
#elif WINDOWS
        _audioTranscriptionService = new AudioTranscriptionServiceWin(transcriptionService);
        _windowTextExtractionService = new WindowTextExtractionServiceWin();
        _hotKeyService = new HotKeyServiceWindows(this);
#endif
        _audioTranscriptionService!.TranscriptionReceived += OnMessageGenerated;
        _audioTranscriptionService.LogReceived += TranscriptionServiceOnLogReceived;
        _audioTranscriptionService.StatusChanged += TranscriptionServiceOnStatusChanged;
        _hotKeyService!.RegisterStartRecordingHotKey(Key.OemQuestion, KeyModifiers.Meta, OnHotKeyPressed);

        _chatCompletionService =
            //new ChatCompletionService("http://localhost:11434/","None", "phi:latest");
            new ChatCompletionService("https://openrouter.ai/api/v1",
                "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",
                "deepseek/deepseek-chat-v3.1:free");

        LanguageComboBox.ItemsSource = _supportedLanguages;
        LanguageComboBox.SelectedIndex = 0;

        // Update the active window title
        UpdateActiveWindowTitle();
    }

    
    private void TranscriptionServiceOnStatusChanged(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += $"[Log][Status] {message}" + Environment.NewLine;
        });    }

    private void TranscriptionServiceOnLogReceived(LogMessage message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += $"[Log][{message.MessageType}] {message.Message}" + Environment.NewLine;
        });
    }


    private async void OnMessageGenerated(TranscriptionMessage message)
    {
        var type = message.MessageType switch
        {
            TranscriptionMessageType.Mic => "[m]",
            TranscriptionMessageType.Speaker => "[o]",
            _ => "[unknown] "
        };
        var msg = $"{type} {message.Message}" + Environment.NewLine;
        _messages.Add(msg);
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += msg;
        });
        if (_messages.Count <= 30)
            return;
        var chatMsg =_messages.Select(x => new ChatMessage(ChatRole.User, x)).ToList();
        _messages.Clear();

        var chatResult = await _chatCompletionService.GetCompletionAsync(chatMsg);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageTextBlock.Text += $"[AI] {chatResult}" + Environment.NewLine;
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

            using var process = new System.Diagnostics.Process();
            process.StartInfo = processStartInfo;
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
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
        EnableWindowPrivacyService.SetProtected(this, isChecked);
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
        try
        {
            if (_isTranscribing) return;
            _isTranscribing = true;
            ToggleButton.Content = "Stop Transcription";
            MessageTextBlock.Text = "";
            var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "en";

            await _audioTranscriptionService!.StartProcessing((selectedLanguage));
        }
        catch (Exception)
        {
            // Log the error but keep the UI responsive
        }
    }



    private async void ToggleButton_OnUnchecked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isTranscribing) return;
            await _audioTranscriptionService!.StopProcessing();

            _isTranscribing = false;
            ToggleButton.Content = "Start Transcription";
        }
        catch (Exception)
        {
            // Log the error but keep the UI responsive
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
        _audioTranscriptionService?.Dispose();
        base.OnClosed(e);
    }
}