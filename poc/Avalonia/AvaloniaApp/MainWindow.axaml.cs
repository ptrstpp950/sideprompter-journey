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
using AvaloniaApp.Services.HotKey;
using AvaloniaApp.Services.TranscriptionService;
using AvaloniaApp.Services.WindowTextExtraction;
using AvaloniaApp.Services;
using AvaloniaApp.Services.EnableWindowPrivacy;
using AvaloniaApp.ViewModel;
using AvaloniaApp.Settings;
using Serilog;

namespace AvaloniaApp;

public partial class MainWindow : Window
{
    private IAudioTranscriptionService? _audioTranscriptionService; // model-dependent, can be recreated
    private readonly IWindowTextExtractionService? _windowTextExtractionService;
    private ChatCompletionService? _chatCompletionService; // now nullable until configured
    private bool _isTranscribing;
    private readonly string[] _supportedLanguages = { "en", "pl" };
    private DateTime? _startedAt; 
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1)}; 
    private readonly IHotKeyService? _hotKeyService;
#if MACOS || OSX || MACCATALYST
    private readonly IMacOsPermissionsService? _macOsPermissionsService;
#endif
    private bool _isWindowProtected = true;

    // Settings window (singleton per main window lifetime)
    private SetupWizard? _settingsWindow;

    private readonly ChatViewModel _chatViewModel = new();
    private ChatHistoryWindow? _chatHistoryWindow;

    private readonly AppSettings _settings;
    private readonly ILogger logger;
    private Whisper.net.Ggml.GgmlType _currentWhisperModelType;

    public MainWindow() : this(null, null) {}

    public MainWindow(AppSettings? settings, ILogger? logger)
    {
        InitializeComponent();
        _settings = settings ?? SettingsService.Load();
        this.logger = logger ?? Serilog.Log.Logger;
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
        try
        {
            _windowTextExtractionService = new WindowTextExtractionServiceMac();
            Serilog.Log.Information("WindowTextExtractionServiceMac initialized.");

            _hotKeyService = new HotKeyServiceMac();
            Serilog.Log.Information("HotKeyServiceMac initialized.");

            _macOsPermissionsService = new MacOsPermissionsService();
            Serilog.Log.Information("MacOsPermissionsService initialized.");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error initializing macOS-specific services.");
            // Optionally, show an error message to the user
        }
#elif WINDOWS
        _windowTextExtractionService = new WindowTextExtractionServiceWin();
        _hotKeyService = new HotKeyServiceWindows(this);
#endif

    // Defer registering macOS hotkeys until the window is opened. In debug builds
    // the Objective-C runtime and native services may not be fully initialized
    // at constructor time which can cause NullReferenceExceptions. Register
    // on the Opened event instead.
    this.Opened += MainWindow_Opened;
    this.Closed += MainWindow_Closed;
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();

        ApplySettings();

        // No scrolling area in compact mode; keep handler for potential future UI.
        _chatViewModel.Messages.CollectionChanged += (_, _) => { };

        EnableWindowPrivacyService.SetProtected(this, _isWindowProtected);
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            if (_hotKeyService != null)
            {
                _hotKeyService.RegisterAiHelpNeededHotKey(Key.OemQuestion, KeyModifiers.Meta, OnAiHelpNeededPressed);
                _hotKeyService.RegisterWindowCaptureHotKey(Key.OemQuestion, KeyModifiers.Alt, OnAiContextHelpPressed);
            }
        }
        catch (Exception ex)
        {
            // Log but don't crash the app if hotkey registration fails in debug.
            _chatViewModel.AddLogMessage($"[HotKey] Failed to register hotkeys: {ex.Message}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            if (_hotKeyService is IDisposable d) d.Dispose();
        }
        catch { }

        this.Opened -= MainWindow_Opened;
        this.Closed -= MainWindow_Closed;
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
            transcriptionCore.StatusChanged += (status) => Dispatcher.UIThread.Post(() => TranscriptionServiceOnLogReceived(new LogMessage(){MessageType = MessageType.Info, Message = status, Timestamp = DateTime.UtcNow}));
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
            var defaultLang = langs.Contains("pl") ? "pl" : langs.FirstOrDefault() ?? "en";
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


    private void OnMessageGenerated(TranscriptionMessage message)
    {
        var author = message.MessageType switch
        {
            TranscriptionMessageType.Mic => MessageAuthor.Me,
            TranscriptionMessageType.Speaker => MessageAuthor.Other,
            _ => MessageAuthor.Other
        };
        
        _chatViewModel.AddMessage(message.Message, author);

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
                "..", "Resources", "libs", "activeWindowTextGetter", "activeWindowTextGetter");
            if (File.Exists(resourcesPath))
                return resourcesPath;
        }

        // For regular builds, try the libs directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var libsPath = Path.Combine(baseDir, "libs", "activeWindowTextGetter", "activeWindowTextGetter");
        
        if (File.Exists(libsPath))
            return libsPath;

        // Fallback to looking in the current directory structure
        var currentDir = Directory.GetCurrentDirectory();
        var projectPath = Path.Combine(currentDir, "libs", "activeWindowTextGetter", "activeWindowTextGetter");
        
        if (File.Exists(projectPath))
            return projectPath;

        // Last resort - assume it's in PATH
        return "activeWindowTextGetter";
    }
    
    private async Task ExtractAndDisplayWindowText()
    {
        try
        {
            if(_windowTextExtractionService == null)
            {
                AddMessage("Window text extraction service not available on this platform.");
                return;
            }
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

    private void SwitchStartStopIcon(bool startIconVisible = false)
    {
        StartIcon.IsVisible = startIconVisible;
        BeforeStartRow.IsVisible = startIconVisible;

        StopIcon.IsVisible = !startIconVisible;
        AiAssistantResponseTextBox.IsVisible = !startIconVisible;

        SettingsButton.IsVisible = startIconVisible;
        
    }

    private async void ToggleButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        try
        {
            SwitchStartStopIcon(false);
            if (_isTranscribing) return;
            _isTranscribing = true;
            //_chatViewModel.ClearMessages();
            var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "pl";

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
            SwitchStartStopIcon(true);
            if (!_isTranscribing) return;
            await _audioTranscriptionService!.StopProcessing();

            _isTranscribing = false;
            _elapsedTimer.Stop();
            _startedAt = null;
            UpdateElapsedTime();
        }
        catch (Exception)
        {
            // Log the error but keep the UI responsive
        }
    }


    private void PrivacyToggleButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        SetWindowsProtection(true);
    }
    private void PrivacyToggleButton_OnUnchecked(object? sender, RoutedEventArgs e)
    {
        SetWindowsProtection(false);
    }

    private void SetWindowsProtection(bool status)
    {
        _isWindowProtected = status;
        EnableWindowPrivacyService.SetProtected(this, _isWindowProtected);
        if (_chatHistoryWindow != null)
            EnableWindowPrivacyService.SetProtected(_chatHistoryWindow, _isWindowProtected);
        if (_settingsWindow != null)
            EnableWindowPrivacyService.SetProtected(_settingsWindow, _isWindowProtected);
        SwitchPrivacyIcon(status);
    }

    private void SwitchPrivacyIcon(bool protectedIconVisible)
    {
        if (this.FindControl<HeroIconsAvalonia.Controls.HeroIcon>("ProtectedIcon") is { } protectedIcon)
        {
            protectedIcon.IsVisible = protectedIconVisible;
        }
        if (this.FindControl<HeroIconsAvalonia.Controls.HeroIcon>("UnprotectedIcon") is { } unprotectedIcon)
        {
            unprotectedIcon.IsVisible = !protectedIconVisible;
        }
    }

    private string AuthorToLetter(MessageAuthor author)
    {
        return author switch
        {
            MessageAuthor.Me => "m",
            MessageAuthor.Other => "o",
            _ => "ai"
        };
    }

    private async void OnAiHelpNeededPressed()
    {
        try
        {
            Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = "Loading conversation help..."; });
            // Create a snapshot of the Messages collection to avoid modification during enumeration
            var messagesSnapshot = _chatViewModel.Messages.ToList();

            var messages = messagesSnapshot
                .Select(m => $"[{AuthorToLetter(m.Author)}] {m.Text}")
                .ToList();

            if (_chatCompletionService != null)
            {
                var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "pl";

                var selectedPrompt = (PromptComboBox.SelectedItem as Prompt)?.PromptText ??
                                     "Answer always with 'wrong prompt - fix it'";

                _chatCompletionService.Language = selectedLanguage;
                _chatCompletionService.Prompt = selectedPrompt;

                var chatResult = await _chatCompletionService.GetCompletionAsync(messages);
                //_chatViewModel.ClearMessages();
                _chatViewModel.AddMessage(chatResult, MessageAuthor.AiAssistant);
                Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = chatResult; });
            }
            else
            {
                _chatViewModel.AddLogMessage("[Config] Chat completion not configured (missing base/model). Skipping AI response.");
            }
        }
        catch (Exception e)
        {
            _chatViewModel.AddLogMessage($"[Log][Exception] {e.Message} {e.StackTrace}");
            Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = $"[AI Context] Error: {e.Message}"; });
        }
    }

    private async void OnAiContextHelpPressed()
    {
        try
        {
            if(_windowTextExtractionService == null)
            {
                AddMessage("Window text extraction service not available on this platform.");
                return;
            }
            Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = "Loading window context help..."; });
            var ctx = await _windowTextExtractionService.GetActiveWindowTextAsync();

            var selectedLanguage = LanguageComboBox.SelectedItem as string ?? "pl";
            if (_chatCompletionService != null)
            {
                _chatCompletionService.Language = selectedLanguage;

                var result = await _chatCompletionService?.GetWindowHelpCompletionAsync(ctx)!;
                Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = result ?? "[AI Context] No response from AI."; });
            }

            //AiAssistantResponseTextBox.Text = $"[AI Context] Title: {title}\nContent: {ctx.Result}";
        }
        catch (Exception e)
        {
            _chatViewModel.AddLogMessage($"[Log][Exception] {e.Message} {e.StackTrace}");
            Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = $"[AI Context] Error: {e.Message}"; });
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
        SetWindowsProtection(_isWindowProtected);
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void HistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_chatHistoryWindow == null || !_chatHistoryWindow.IsVisible)
        {
            _chatHistoryWindow = new ChatHistoryWindow
            {
                DataContext = _chatViewModel,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _chatHistoryWindow.Closed += (_, _) => _chatHistoryWindow = null;
            _chatHistoryWindow.Show(this);
        }
        else
        {
            _chatHistoryWindow.Activate();
        }
        SetWindowsProtection(_isWindowProtected);
    }

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _chatViewModel.ClearMessages();
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

    private async void AccessibilityButton_OnClick(object? sender, RoutedEventArgs e)
    {
#if MACOS || OSX || MACCATALYST
        if (_macOsPermissionsService == null)
        {
            _chatViewModel.AddLogMessage("[Permissions] Service not available.");
            return;
        }

        _chatViewModel.AddLogMessage("[Permissions] Requesting Accessibility access...");
        await _macOsPermissionsService.RequestAccessibilityPermission();

        // There's a delay between the call and the system showing the prompt.
        // We check the status after a short delay.
        await Task.Delay(200);

        if (_macOsPermissionsService.HasAccessibilityPermission())
        {
            _chatViewModel.AddLogMessage("[Permissions] Accessibility permission granted.");
        }
        else
        {
            _chatViewModel.AddLogMessage("[Permissions] Accessibility permission NOT granted. Please grant it in System Settings > Privacy & Security > Accessibility.");
        }
#else
        _chatViewModel.AddLogMessage("[Permissions] This feature is only available on macOS.");
        await Task.CompletedTask;
#endif
    }

    private void AskAiButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OnAiHelpNeededPressed();
    }

    private void AddContextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OnAiContextHelpPressed();
    }
}
