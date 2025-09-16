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
using AvaloniaApp.Services.Chat;

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
    private readonly ChatSessionService _chatSessionService;

    public MainWindow() : this(null, null) {}

    public MainWindow(AppSettings? settings, ILogger? logger)
    {
        InitializeComponent();
        _settings = settings ?? SettingsService.Load();
        this.logger = logger ?? Serilog.Log.Logger;
        DataContext = _chatViewModel;
    // Initialize chat persistence services
    var pathProvider = new AppPathProvider();
    var storage = new FileChatStorage(pathProvider);
    _chatSessionService = new ChatSessionService(storage, _settings);
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
        _chatViewModel.MessageAdded += ChatViewModelOnMessageAdded;

        EnableWindowPrivacyService.SetProtected(this, _isWindowProtected);
    }

    private async void ChatViewModelOnMessageAdded(object? sender, ChatMessage e)
    {
        try
        {
            var lang = LanguageComboBox?.SelectedItem as string ?? _settings.Languages?.FirstOrDefault() ?? "en";
            await _chatSessionService.EnsureHeaderAsync(_chatViewModel, lang);
            await _chatSessionService.AppendMessageAsync(e);
        }
        catch (Exception ex)
        {
            _chatViewModel.AddLogMessage($"[Persist] Failed to save message: {ex.Message}");
        }
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
            // Start a new session when transcription starts
            _chatSessionService.NewSession();
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

            // Ensure header exists and append a placeholder summary/title
            var lang = LanguageComboBox?.SelectedItem as string ?? _settings.Languages?.FirstOrDefault() ?? "en";
            await _chatSessionService.EnsureHeaderAsync(_chatViewModel, lang);
            await _chatSessionService.FinalizeAsync("TODO", "TODO");
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
                //Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = chatResult; });
                App.Notifications.Show(chatResult);
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
                //Dispatcher.UIThread.Post(() => { if (AiAssistantResponseTextBox != null) AiAssistantResponseTextBox.Text = result ?? "[AI Context] No response from AI."; });
                App.Notifications.Show(result ?? "[AI Context] No response from AI.");
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
        _chatViewModel.MessageAdded -= ChatViewModelOnMessageAdded;
        base.OnClosed(e);
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
