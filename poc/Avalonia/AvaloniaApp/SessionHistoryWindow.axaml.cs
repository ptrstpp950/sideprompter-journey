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
    private readonly Guid? _currentSessionId;

    public SessionHistoryWindow() : this(new FileChatStorage(new AppPathProvider()), null)
    {
    }

    public SessionHistoryWindow(IChatStorage storage, Guid? currentSessionId)
    {
        InitializeComponent();
        _currentSessionId = currentSessionId;
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
        await _viewModel.LoadSessionsAsync(_currentSessionId);
    }

    private void SessionListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedSession == null) return;
        SessionOpenRequested?.Invoke(this, _viewModel.SelectedSession.SessionId);
        Close();
    }

    private void OpenSession_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSessionFromSender(sender);
        if (item == null) return;
        SessionOpenRequested?.Invoke(this, item.SessionId);
        Close();
    }

    private async void RenameSession_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSessionFromSender(sender);
        if (item == null) return;

        // Show a simple rename dialog using a TextBox in a child window
        var dialog = new Window
        {
            Title = "Rename Session",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Topmost = true
        };

        var textBox = new TextBox
        {
            Text = item.Title,
            Margin = new Avalonia.Thickness(12, 12, 12, 8),
            Watermark = "Session title..."
        };

        var okButton = new Button
        {
            Content = "Save",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 12, 12)
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 8, 12)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancelButton, okButton }
        };

        var panel = new StackPanel
        {
            Children = { textBox, buttonPanel }
        };

        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = panel;
        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(result) && result != item.Title)
        {
            await _viewModel.RenameSessionAsync(item.SessionId, result.Trim());
        }
    }

    private async void DeleteSession_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSessionFromSender(sender);
        if (item == null) return;

        // Show confirmation dialog
        var dialog = new Window
        {
            Title = "Delete Session",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Topmost = true
        };

        var message = new TextBlock
        {
            Text = $"Delete \"{item.Title}\"?\nThis cannot be undone.",
            Margin = new Avalonia.Thickness(12, 12, 12, 8),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var deleteButton = new Button
        {
            Content = "Delete",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 12, 12),
            Foreground = Avalonia.Media.Brushes.White,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#ef4444"))
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 8, 12)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancelButton, deleteButton }
        };

        var panel = new StackPanel
        {
            Children = { message, buttonPanel }
        };

        var confirmed = false;
        deleteButton.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = panel;
        await dialog.ShowDialog(this);

        if (confirmed)
        {
            await _viewModel.DeleteSessionAsync(item.SessionId);
        }
    }

    private static SessionListItem? GetSessionFromSender(object? sender)
    {
        // From inline Button (Tag binding)
        if (sender is Button btn && btn.Tag is SessionListItem tagItem)
            return tagItem;
        // From context MenuItem (DataContext)
        if (sender is MenuItem menuItem && menuItem.DataContext is SessionListItem ctxItem)
            return ctxItem;
        return null;
    }
}
