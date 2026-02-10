using System;
using System.Collections.Generic;
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

    /// <summary>
    /// List all finalized sessions, ordered by date descending.
    /// </summary>
    Task<List<ChatSessionExport>> ListSessionsAsync();

    /// <summary>
    /// Load a full session (header + messages) by session ID.
    /// </summary>
    Task<ChatSessionExport?> LoadSessionAsync(Guid sessionId);

    /// <summary>
    /// Update the title of a finalized session.
    /// </summary>
    Task UpdateSessionTitleAsync(Guid sessionId, string newTitle);

    /// <summary>
    /// Re-open a finalized session for continued recording.
    /// Converts the JSON back to JSONL so new messages can be appended.
    /// Returns the session header info.
    /// </summary>
    Task<ChatSessionHeader?> ReopenSessionAsync(Guid sessionId);
}
