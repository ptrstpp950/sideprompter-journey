using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaApp.Services.Chat;

namespace AvaloniaApp.ViewModel;

public class SessionHistoryViewModel : INotifyPropertyChanged
{
    private readonly IChatStorage _storage;
    private ObservableCollection<SessionListItem> _sessions = new();
    private ObservableCollection<SessionListItem> _allSessions = new();

    private string _searchText = string.Empty;
    private SessionListItem? _selectedSession;

    public SessionHistoryViewModel(IChatStorage storage)
    {
        _storage = storage;
    }

    public ObservableCollection<SessionListItem> Sessions
    {
        get => _sessions;
        set { _sessions = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public SessionListItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (_selectedSession == value) return;
            _selectedSession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Raised when a session is selected for opening. 
    /// The handler receives the session ID to load.
    /// </summary>
    public event EventHandler<Guid>? SessionOpenRequested;

    public async Task LoadSessionsAsync()
    {
        var list = await _storage.ListSessionsAsync();
        _allSessions = new ObservableCollection<SessionListItem>(
            list.Select(s => new SessionListItem
            {
                SessionId = s.SessionId,
                Title = string.IsNullOrWhiteSpace(s.Title) || s.Title == "TODO" 
                    ? $"Session {s.StartedUtc:g}" 
                    : s.Title,
                Date = s.StartedUtc.ToLocalTime(),
                Summary = s.Summary,
                MessageCount = s.Messages.Count,
                IsInProgress = s.Summary == "(In progress)"
            }));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            Sessions = new ObservableCollection<SessionListItem>(_allSessions);
        }
        else
        {
            var q = _searchText.Trim();
            Sessions = new ObservableCollection<SessionListItem>(
                _allSessions.Where(s =>
                    (s.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (s.Summary?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    s.Date.ToString("g").Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task RenameSessionAsync(Guid sessionId, string newTitle)
    {
        await _storage.UpdateSessionTitleAsync(sessionId, newTitle);
        // Update in the list
        var item = _allSessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (item != null) item.Title = newTitle;
        var visibleItem = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (visibleItem != null) visibleItem.Title = newTitle;
    }

    public void RequestOpenSession(Guid sessionId)
    {
        SessionOpenRequested?.Invoke(this, sessionId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class SessionListItem : INotifyPropertyChanged
{
    public Guid SessionId { get; set; }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public DateTime Date { get; set; }
    public string? Summary { get; set; }
    public int MessageCount { get; set; }
    public bool IsInProgress { get; set; }

    public string DateFormatted => Date.ToString("yyyy-MM-dd HH:mm");
    public string Subtitle => IsInProgress 
        ? "In progress" 
        : $"{MessageCount} messages";

    public event PropertyChangedEventHandler? PropertyChanged;
}
