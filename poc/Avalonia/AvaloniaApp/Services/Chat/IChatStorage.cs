using System;
using System.Threading.Tasks;

namespace AvaloniaApp.Services.Chat;

/// <summary>
/// Abstraction for persisting chat sessions/messages.
/// Implementations should be safe for frequent, incremental writes.
/// </summary>
public interface IChatStorage
{
    /// <summary>
    /// Ensure a session file exists and append a session header as the first record.
    /// If the file already exists and header is present, implementations may no-op.
    /// </summary>
    Task AppendSessionHeaderAsync(ChatSessionHeader header);

    /// <summary>
    /// Append a single message record to the session.
    /// </summary>
    Task AppendMessageAsync(Guid sessionId, ChatMessageDto message);

    /// <summary>
    /// Finalize a session by consolidating JSONL into a single JSON file,
    /// applying the provided title and summary, and deduplicating messages.
    /// </summary>
    Task FinalizeSessionAsync(Guid sessionId, string title, string summary);
}
