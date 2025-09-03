using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
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
using AvaloniaApp.Services;
using AvaloniaApp.ViewModel;
using AvaloniaApp.Settings;
using HeroIconsAvalonia.Enums;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    private IAudioTranscriptionService? _audioTranscriptionService; // model-dependent, can be recreated
    private readonly IWindowTextExtractionService _windowTextExtractionService;
    private ChatCompletionService? _chatCompletionService; // now nullable until configured
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private DateTime? _startedAt; 
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1)}; 
    private readonly IHotKeyService? _hotKeyService;
    // ReSharper disable once RedundantDefaultMemberInitializer
    private bool _windowCaptureHotkeyRegistered = false;

    // Settings window (singleton per main window lifetime)
    private SetupWizard? _settingsWindow;

    private readonly ChatViewModel _chatViewModel = new();

    private readonly AppSettings _settings;
    private Whisper.net.Ggml.GgmlType _currentWhisperModelType;

    public MainWindow() : this(null) {}

    public MainWindow(AppSettings? settings)
    {
        InitializeComponent();
        _settings = settings ?? SettingsService.Load();
        DataContext = _chatViewModel;
        // Position window top-center with margin from top (e.g., 20px)
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            Position = new PixelPoint(
                x: bounds.X + (bounds.Width - (int)Width) / 2,
                y: bounds.Y + 20);
        }
        // Platform specific static services
#if MACOS || OSX || MACCATALYST
        _windowTextExtractionService = new WindowTextExtractionServiceMac();
        _hotKeyService = new HotKeyServiceMacOptionTwo(this);
#elif WINDOWS
        _windowTextExtractionService = new WindowTextExtractionServiceWin();
        _hotKeyService = new HotKeyServiceWindows(this);
#endif

        _hotKeyService!.RegisterStartRecordingHotKey(Key.OemQuestion, KeyModifiers.Meta, OnHotKeyPressed);
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();

        ApplySettings();

        // No scrolling area in compact mode; keep handler for potential future UI.
        _chatViewModel.Messages.CollectionChanged += (_, _) => { };
    }

    private void ApplySettings()
    {
        // (Re)initialize transcription service if model changed
        var desiredModel = _settings.WhisperModelType;
        if (_audioTranscriptionService == null || desiredModel != _currentWhisperModelType)
        {
            if (_isTranscribing)
            {
                try { _audioTranscriptionService?.StopProcessing().GetAwaiter().GetResult(); } catch { /* ignore */ }
                _isTranscribing = false;
            }
            if (_audioTranscriptionService != null)
            {
                _audioTranscriptionService.TranscriptionReceived -= OnMessageGenerated;
                _audioTranscriptionService.LogReceived -= TranscriptionServiceOnLogReceived;
                _audioTranscriptionService.StatusChanged -= TranscriptionServiceOnStatusChanged;
                _audioTranscriptionService.Dispose();
            }

            var transcriptionCore = new WhisperTranscriptionService(desiredModel);
#if MACOS || OSX || MACCATALYST
            _audioTranscriptionService = new AudioTranscriptionServiceMac(transcriptionCore);
#elif WINDOWS
            _audioTranscriptionService = new AudioTranscriptionServiceWin(transcriptionCore);
#endif
            _audioTranscriptionService!.TranscriptionReceived += OnMessageGenerated;
            _audioTranscriptionService.LogReceived += TranscriptionServiceOnLogReceived;
            _audioTranscriptionService.StatusChanged += TranscriptionServiceOnStatusChanged;
            _currentWhisperModelType = desiredModel;
            _chatViewModel.AddLogMessage($"[Config] Whisper model set to: {_currentWhisperModelType}");
        }

        // Chat completion service
        var chatApiBase = _settings.ChatApiBase;
        var chatApiKey = _settings.ChatApiKey ?? string.Empty;
        var chatModel = _settings.ChatModel;
        if (string.IsNullOrWhiteSpace(chatApiBase) || string.IsNullOrWhiteSpace(chatModel))
        {
            _chatCompletionService = null;
            _chatViewModel.AddLogMessage("[Config] Chat API base or model missing. Configure ChatApiBase and ChatModel in settings.");
        }
        else
        {
            _chatCompletionService = new ChatCompletionService(chatApiBase, chatApiKey, chatModel);
            _chatViewModel.AddLogMessage($"[Config] Chat model set to: {chatModel}");
        }

        // Languages combo
        if (LanguageComboBox != null)
        {
            var langs = (_settings.Languages?.Count > 0 ? _settings.Languages : _supportedLanguages.ToList());
            LanguageComboBox.ItemsSource = langs.ToArray();
            var defaultLang = langs.Contains("en") ? "en" : langs.FirstOrDefault() ?? "en";
            LanguageComboBox.SelectedItem = defaultLang;
        }
    }

    
    private void TranscriptionServiceOnStatusChanged(string message)
    {
        _chatViewModel.AddLogMessage($"[Log][Status] {message}");
    }

    private void TranscriptionServiceOnLogReceived(LogMessage message)
    {
        _chatViewModel.AddLogMessage($"[Log][{message.MessageType}] {message.Message}");
    }


    private async void OnMessageGenerated(TranscriptionMessage message)
    {
        var author = message.MessageType switch
        {
            TranscriptionMessageType.Mic => MessageAuthor.Me,
            TranscriptionMessageType.Speaker => MessageAuthor.Other,
            _ => MessageAuthor.Other
        };
        
        _chatViewModel.AddMessage(message.Message, author);

        if (_chatViewModel.Messages.Count(m => m.Author != MessageAuthor.Me) <= 10)
            return;

        var messages = _chatViewModel.Messages
            .Select(m => $"[{m.Author}] {m.Text}")
            .ToList();
        
        if (_chatCompletionService != null)
        {
            var chatResult = await _chatCompletionService.GetCompletionAsync(messages);
            _chatViewModel.ClearMessages();
            _chatViewModel.AddMessage(chatResult, MessageAuthor.Other);
        }
        else
        {
            _chatViewModel.AddLogMessage("[Config] Chat completion not configured (missing base/model). Skipping AI response.");
        }
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
    
    // Removed UI for active window title in compact redesign; keep extraction helpers for future use.
    
    // Hotkey registration button removed in compact UI.
    
    private async void OnWindowTextHotkeyPressed()
    {
        await ExtractAndDisplayWindowTextViaCli();
    }
    
    // Privacy mode checkbox removed in compact UI.

    private void AddMessage(string message)
    {
        _chatViewModel.AddLogMessage(message);
    }

    private async void ToggleButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_isTranscribing) return;
            _isTranscribing = true;
            _chatViewModel.ClearMessages();
            var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "en";
            if (this.FindControl<HeroIconsAvalonia.Controls.HeroIcon>("StartStopIcon") is { } startIcon)
            {
                startIcon.Type = IconType.StopCircle;
                startIcon.Foreground = Avalonia.Media.Brushes.IndianRed;
            }

            await _audioTranscriptionService!.StartProcessing((selectedLanguage));
            _startedAt = DateTime.UtcNow;
            _elapsedTimer.Start();
            UpdateElapsedTime();
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
            _elapsedTimer.Stop();
            _startedAt = null;
            UpdateElapsedTime();
            if (this.FindControl<HeroIconsAvalonia.Controls.HeroIcon>("StartStopIcon") is { } startIcon)
            {
                startIcon.Type = IconType.PlayCircle;
                startIcon.Foreground = Avalonia.Media.Brushes.LimeGreen;
            }
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

    private void UpdateElapsedTime()
    {
        if (ElapsedTimeText == null)
            return;
        if (_startedAt == null)
        {
            ElapsedTimeText.Text = "00:00";
            return;
        }
        var elapsed = DateTime.UtcNow - _startedAt.Value;
        if (elapsed.TotalHours >= 1)
            ElapsedTimeText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        else
            ElapsedTimeText.Text = $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Open (or focus) the settings / setup wizard in settings mode
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SetupWizard(_settings, true)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; ApplySettings(); }; // re-apply after close
            _settingsWindow.Show(this);
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBarPanel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Allow dragging the window when pressing on the custom top panel with left mouse button
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotKeyService?.Dispose();
        _audioTranscriptionService?.Dispose();
        base.OnClosed(e);
    }

    private async void TestChatButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var list = new List<string>
        {
            "[o] As Michael you said, the PE objective was migrate Hattori V2 on Unicorn to prepare for prod release.",
            "[o] And just first, you know disclaimer something worth to know because we are using different names.",
            "[o] So here, let's say internally we use the name hatori for the service that is officially and when it comes to.",
            "[o] The the official documents for documentation.",
            "[o] It's called Esoftware update service.",
            "[o] So just to ensure that we are on the actually same page.",
            "[o] Yeah. Please keep it in mind. But of course I will use for for this demo the name of the reads shorter.",
            "[o] And but what was the the objective about? It was somehow, you know, it was all around the feature number 2265.You can click on the link if you want to see and the the feature was about.Yeah.",
            "[o] My greeting Qatari service clinical platform to remove technical debt and simplify future development. That was the title of the feature and basically the the objective was about moving the service.",
            "[o] V2 because the.",
            "[o] Consists of V1 or V2 or you can treat as a. You know Part 1 or Part 2.",
            "[o] Something like that. But to get the the.",
            "[o] It to part move it to the Unicorn platform, somehow modernize and by modernization. It means not, you know, fixing all technical depth, rather adapting to.",
            "[o] The to the Unicorn platform, adapting to the ways how we operate with the services here in the WS.",
            "[o] And the whole process we can call it is the, let's say.",
            "[o] And taking a look on what's was.",
            "[o] What we wanted to to achieve or what was the desired state.",
            "[o] Here the service is is.",
            "[o] The service is deployed.",
            "[o] It's too long environments and documents"
        };

        if (_chatCompletionService == null)
        {
            _chatViewModel.AddLogMessage("[AI TEST] Chat completion not configured.");
            return;
        }

        var result = await _chatCompletionService.GetCompletionAsync(list);
        _chatViewModel.AddLogMessage("[AI TEST] " + result);
    }
}
