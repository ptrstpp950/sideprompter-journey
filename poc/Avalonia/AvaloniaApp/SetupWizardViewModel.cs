using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Controls;
using AvaloniaApp.Settings;
using AvaloniaApp.Services.TranscriptionService;
using Whisper.net.Ggml;

namespace AvaloniaApp;

public class SetupWizardViewModel : INotifyPropertyChanged
{
    public ObservableCollection<SettingsPageViewModel> Pages { get; } = new();

    private SettingsPageViewModel? _selectedPage;
    public SettingsPageViewModel? SelectedPage
    {
        get => _selectedPage;
        set { if (_selectedPage != value) { _selectedPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryButtonText)); OnPropertyChanged(nameof(IsLastPage)); } }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    public bool IsSettingsMode { get; }

    public string PrimaryButtonText => IsSettingsMode ? "Save" : (IsLastPage ? "Finish" : "Next");

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    private readonly AppSettings _settings;

    public ICommand PrimaryCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand ContinueFromSplashCommand { get; private set; }
    public ICommand SkipIntroCommand { get; private set; }

    public bool IsLastPage => SelectedPage != null && Pages.Count > 0 && Pages[^1] == SelectedPage;

    // Busy / progress state for operations like model download
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); } } }
    public bool IsNotBusy => !IsBusy;

    private string _busyMessage = string.Empty;
    public string BusyMessage { get => _busyMessage; private set { if (_busyMessage != value) { _busyMessage = value; OnPropertyChanged(); } } }

    private double _busyProgress; // 0-100
    public double BusyProgress { get => _busyProgress; private set { if (Math.Abs(_busyProgress - value) > 0.0001) { _busyProgress = value; OnPropertyChanged(); } } }

    private bool _isBusyIndeterminate = true;
    public bool IsBusyIndeterminate { get => _isBusyIndeterminate; private set { if (_isBusyIndeterminate != value) { _isBusyIndeterminate = value; OnPropertyChanged(); } } }

    public SetupWizardViewModel() : this(SettingsService.Load(), isSettingsMode: false) { }

    private CancellationTokenSource? _downloadCts;

    public SetupWizardViewModel(AppSettings settings, bool isSettingsMode)
    {
        _settings = settings;
        IsSettingsMode = isSettingsMode;
        BuildPages();
        SelectedPage = Pages.Count > 0 ? Pages[0] : null;

        PrimaryCommand = new DelegateCommand(async _ => await PrimaryActionAsync(), _ => !IsBusy);
        ContinueFromSplashCommand = new DelegateCommand(_ => AdvanceFromSplash(), _ => !IsBusy);
        SkipIntroCommand = new DelegateCommand(_ => AdvanceFromSplash(), _ => !IsBusy);
        var backCmd = new DelegateCommand(_ => BackAction(), _ => !IsBusy && CanGoBack());
        BackCommand = backCmd;
        ResetCommand = new DelegateCommand(_ => { foreach (var p in Pages) p.Reset(); });
        CancelDownloadCommand = new DelegateCommand(_ => _downloadCts?.Cancel(), _ => IsBusy);
    }

    private void BuildPages()
    {
        // If not in settings mode, insert an intro/splash page first.
        if (!IsSettingsMode)
        {
            var splash = new SplashScreen();
            // Let the splash control bind to this view model for its buttons.
            splash.DataContext = this;
            Pages.Add(new IntroSettingsPage(splash));
        }

        Pages.Add(new LanguagesSettingsPage(_settings));
        Pages.Add(new ModelSettingsPage(_settings));
        Pages.Add(new ChatSettingsPage(_settings));
        Pages.Add(new PromptsSettingsPage(_settings));
    }

    private void AdvanceFromSplash()
    {
        if (SelectedPage == null) return;
        var idx = Pages.IndexOf(SelectedPage);
        if (idx >= 0 && idx < Pages.Count - 1)
        {
            SelectedPage = Pages[idx + 1];
            StatusMessage = string.Empty;
        }
    }

    private async Task PrimaryActionAsync()
    {
    if (IsBusy) return; // guard
    if (SelectedPage == null) return;
        if (!SelectedPage.ValidateAndSave(out var error))
        {
            StatusMessage = error;
            return;
        }

        // If we are on the model page and advancing, ensure the model is downloaded first.
        if (SelectedPage is ModelSettingsPage)
        {
            if (Enum.TryParse<GgmlType>(_settings.WhisperModel, true, out var modelType))
            {
                _downloadCts = new CancellationTokenSource();
                try
                {
                    IsBusy = true;
                    RaiseCommandCanExecStates();
                    IsBusyIndeterminate = true;
                    BusyProgress = 0;
                    BusyMessage = "Preparing model download...";
                    StatusMessage = string.Empty;

                    void StatusCallback(string msg)
                    {
                        BusyMessage = msg;
                        // Try extract percentage
                        var match = Regex.Match(msg, "(\\d{1,3}(?:\\.\\d+)?)%" );
                        if (match.Success && double.TryParse(match.Groups[1].Value, out var pct))
                        {
                            BusyProgress = Math.Clamp(pct, 0, 100);
                            IsBusyIndeterminate = false;
                        }
                    }

                    await WhisperTranscriptionService.EnsureModelDownloadedAsync(modelType, StatusCallback, _downloadCts.Token);
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Model download cancelled.";
                    return;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Model download failed: {ex.Message}";
                    return; // Abort navigation if download failed.
                }
                finally
                {
                    IsBusy = false;
                    RaiseCommandCanExecStates();
                    _downloadCts.Dispose();
                    _downloadCts = null;
                }
            }
            else
            {
                StatusMessage = $"Unknown model type: {_settings.WhisperModel}";
                return;
            }
        }

        if (!IsSettingsMode && !IsLastPage)
        {
            var idx = Pages.IndexOf(SelectedPage);
            if (idx >= 0 && idx < Pages.Count - 1)
            {
                SelectedPage = Pages[idx + 1];
                StatusMessage = string.Empty;
            }
        }
        else
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanGoBack() => SelectedPage != null && Pages.IndexOf(SelectedPage) > 0;

    private void BackAction()
    {
        if (!CanGoBack()) return;
        var idx = Pages.IndexOf(SelectedPage!);
        SelectedPage = Pages[idx - 1];
        StatusMessage = string.Empty;
    }

    public event EventHandler? RequestClose;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(SelectedPage) && BackCommand is DelegateCommand dc)
        {
            dc.RaiseCanExecuteChanged();
        }
        if (name == nameof(IsBusy))
        {
            RaiseCommandCanExecStates();
        }
    }

    private void RaiseCommandCanExecStates()
    {
        if (PrimaryCommand is DelegateCommand pc) pc.RaiseCanExecuteChanged();
        if (BackCommand is DelegateCommand bc) bc.RaiseCanExecuteChanged();
        if (CancelDownloadCommand is DelegateCommand cc) cc.RaiseCanExecuteChanged();
    }
}

// Small concrete page wrapper for the intro splash control.
internal sealed class IntroSettingsPage : SettingsPageViewModel
{
    public IntroSettingsPage(Control view) : base("Welcome", "✨", view) { }
    public override bool ValidateAndSave(out string errorMessage) { errorMessage = string.Empty; return true; }
}

public abstract class SettingsPageViewModel
{
    public string Title { get; }
    public string Icon { get; }
    public Control View { get; }

    protected SettingsPageViewModel(string title, string icon, Control view)
    {
        Title = title;
        Icon = icon;
        View = view;
    }

    public abstract bool ValidateAndSave(out string errorMessage);
    public virtual void Reset() { }
}

public sealed class DelegateCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    public DelegateCommand(Action<object?> exec, Func<object?, bool>? canExec = null)
    { _execute = exec; _canExecute = canExec; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
