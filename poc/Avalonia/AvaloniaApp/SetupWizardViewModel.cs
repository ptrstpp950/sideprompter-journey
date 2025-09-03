using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using AvaloniaApp.Settings;

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

    private readonly AppSettings _settings;

    public ICommand PrimaryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetCommand { get; }

    public bool IsLastPage => SelectedPage != null && Pages.Count > 0 && Pages[^1] == SelectedPage;

    public SetupWizardViewModel() : this(SettingsService.Load(), isSettingsMode: false) { }

    public SetupWizardViewModel(AppSettings settings, bool isSettingsMode)
    {
        _settings = settings;
        IsSettingsMode = isSettingsMode;
        BuildPages();
        SelectedPage = Pages.Count > 0 ? Pages[0] : null;

        PrimaryCommand = new DelegateCommand(_ => PrimaryAction());
        CancelCommand = new DelegateCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));
        ResetCommand = new DelegateCommand(_ => { foreach (var p in Pages) p.Reset(); });
    }

    private void BuildPages()
    {
        Pages.Add(new LanguagesSettingsPage(_settings));
        Pages.Add(new ModelSettingsPage(_settings));
        Pages.Add(new ChatSettingsPage(_settings));
    }

    private void PrimaryAction()
    {
        if (SelectedPage == null) return;
        if (!SelectedPage.ValidateAndSave(out var error))
        {
            StatusMessage = error;
            return;
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

    public event EventHandler? RequestClose;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
