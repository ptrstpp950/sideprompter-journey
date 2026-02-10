using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaApp.Services.Chat;
using AvaloniaApp.ViewModel;

namespace AvaloniaApp;

public partial class SessionHistoryWindow : Window
{
    private readonly SessionHistoryViewModel _viewModel;

    public SessionHistoryWindow() : this(new FileChatStorage(new AppPathProvider()))
    {
    }

    public SessionHistoryWindow(IChatStorage storage)
    {
        InitializeComponent();
        _viewModel = new SessionHistoryViewModel(storage);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Raised when user requests to open a session. The MainWindow listens to this.
    /// </summary>
    public event EventHandler<Guid>? SessionOpenRequested;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await _viewModel.LoadSessionsAsync();
    }

    private void SessionListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedSession == null) return;
        SessionOpenRequested?.Invoke(this, _viewModel.SelectedSession.SessionId);
        Close();
    }
}
