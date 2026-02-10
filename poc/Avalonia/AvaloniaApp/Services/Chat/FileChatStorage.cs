using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvaloniaApp.Services.Chat;

public class FileChatStorage : IChatStorage
{
    private readonly IAppPathProvider _paths;

    public FileChatStorage(IAppPathProvider paths)
    {
        _paths = paths;
    }

    private string GetSessionFile(Guid sessionId, DateTime startedUtc)
    {
        var dir = _paths.GetChatsDirectory();
        var ts = startedUtc.ToString("yyyy-MM-ddTHH-mm-ssZ");
        return Path.Combine(dir, $"{ts}__{sessionId}.jsonl");
    }

    public async Task AppendSessionHeaderAsync(ChatSessionHeader header)
    {
        var file = GetSessionFile(header.SessionId, header.StartedUtc);
        if (!File.Exists(file))
        {
            await using var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var sw = new StreamWriter(fs);
            var json = JsonSerializer.Serialize(new { type = "header", header });
            await sw.WriteLineAsync(json);
        }
        else
        {
            // Optionally verify header line exists; keep idempotent
        }
    }

    public async Task AppendMessageAsync(Guid sessionId, ChatMessageDto message)
    {
        var dir = _paths.GetChatsDirectory();
        var pattern = $"*__{sessionId}.jsonl";
        var matches = Directory.GetFiles(dir, pattern);
        string file = matches.Length > 0 ? matches[0] : Path.Combine(dir, $"{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ssZ}__{sessionId}.jsonl");

        await using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var sw = new StreamWriter(fs);
        var json = JsonSerializer.Serialize(new { type = "message", message });
        await sw.WriteLineAsync(json);
    }

    public async Task FinalizeSessionAsync(Guid sessionId, string title, string summary)
    {
        var dir = _paths.GetChatsDirectory();
        var pattern = $"*__{sessionId}.jsonl";
        var matches = Directory.GetFiles(dir, pattern);
        string jsonl = matches.Length > 0 ? matches[0] : Path.Combine(dir, $"{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ssZ}__{sessionId}.jsonl");

        // Read all lines from JSONL and build export model
        var export = new ChatSessionExport();
        var latestByTs = new Dictionary<DateTime, ChatMessageDto>();

        if (File.Exists(jsonl))
        {
            var lines = await File.ReadAllLinesAsync(jsonl);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (type == "header")
                {
                    if (doc.RootElement.TryGetProperty("header", out var headerEl))
                    {
                        export.SessionId = headerEl.GetProperty("SessionId").GetGuid();
                        export.StartedUtc = headerEl.GetProperty("StartedUtc").GetDateTime();
                        export.Language = headerEl.GetProperty("Language").GetString() ?? string.Empty;
                        export.PromptId = headerEl.TryGetProperty("PromptId", out var pid) ? pid.GetString() : null;
                        export.PromptText = headerEl.TryGetProperty("PromptText", out var ptxt) ? ptxt.GetString() : null;
                        export.WhisperModel = headerEl.TryGetProperty("WhisperModel", out var w) ? w.GetString() : null;
                        export.ChatModel = headerEl.TryGetProperty("ChatModel", out var c) ? c.GetString() : null;
                        export.Title = headerEl.TryGetProperty("Title", out var t) ? t.GetString() : null;
                        export.Summary = headerEl.TryGetProperty("Summary", out var s) ? s.GetString() : null;
                    }
                }
                else if (type == "message")
                {
                    if (doc.RootElement.TryGetProperty("message", out var msgEl))
                    {
                        var ts = msgEl.GetProperty("TimestampUtc").GetDateTime();
                        var author = (ChatAuthorDto)msgEl.GetProperty("Author").GetInt32();
                        var text = msgEl.GetProperty("Text").GetString() ?? string.Empty;
                        latestByTs[ts] = new ChatMessageDto
                        {
                            TimestampUtc = ts,
                            Author = author,
                            Text = text
                        };
                    }
                }
                else if (type == "summary")
                {
                    if (doc.RootElement.TryGetProperty("title", out var te)) export.Title = te.GetString();
                    if (doc.RootElement.TryGetProperty("summary", out var se)) export.Summary = se.GetString();
                }
            }
        }

        // Apply requested title/summary now (overrides any previous)
        export.Title = title;
        export.Summary = summary;

        // Deduplication: keep only last text per TimestampUtc
        export.Messages.AddRange(latestByTs
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value));

        // Write final JSON next to JSONL
        var jsonPath = Path.ChangeExtension(jsonl, ".json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var payload = JsonSerializer.Serialize(export, options);
        await File.WriteAllTextAsync(jsonPath, payload);
        File.Delete(jsonl);
    }

    public async Task<List<ChatSessionExport>> ListSessionsAsync(Guid? currentSessionId = null)
    {
        var dir = _paths.GetChatsDirectory();
        var sessions = new List<ChatSessionExport>();

        // Auto-finalize orphaned .jsonl sessions (any that aren't the current active session)
        var jsonlFiles = Directory.GetFiles(dir, "*.jsonl");
        foreach (var file in jsonlFiles)
        {
            try
            {
                var firstLine = (await File.ReadAllLinesAsync(file)).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(firstLine)) continue;
                using var doc = JsonDocument.Parse(firstLine);
                if (doc.RootElement.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "header"
                    && doc.RootElement.TryGetProperty("header", out var headerEl))
                {
                    var sessionId = headerEl.GetProperty("SessionId").GetGuid();
                    if (currentSessionId.HasValue && sessionId == currentSessionId.Value)
                        continue; // Skip the current active session

                    // Auto-finalize this orphaned session
                    var title = headerEl.TryGetProperty("Title", out var t) && !string.IsNullOrWhiteSpace(t.GetString())
                        ? t.GetString()!
                        : "Untitled Session";
                    await FinalizeSessionAsync(sessionId, title, string.Empty);
                }
            }
            catch
            {
                // Skip malformed files
            }
        }

        // Load all finalized .json sessions
        var jsonFiles = Directory.GetFiles(dir, "*.json");
        foreach (var file in jsonFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var export = JsonSerializer.Deserialize<ChatSessionExport>(content);
                if (export != null)
                    sessions.Add(export);
            }
            catch
            {
                // Skip malformed files
            }
        }

        return sessions.OrderByDescending(s => s.StartedUtc).ToList();
    }

    public async Task<ChatSessionExport?> LoadSessionAsync(Guid sessionId)
    {
        var dir = _paths.GetChatsDirectory();

        // Try finalized .json first
        var jsonPattern = $"*__{sessionId}.json";
        var jsonMatches = Directory.GetFiles(dir, jsonPattern);
        if (jsonMatches.Length > 0)
        {
            var content = await File.ReadAllTextAsync(jsonMatches[0]);
            return JsonSerializer.Deserialize<ChatSessionExport>(content);
        }

        // Try active .jsonl
        var jsonlPattern = $"*__{sessionId}.jsonl";
        var jsonlMatches = Directory.GetFiles(dir, jsonlPattern);
        if (jsonlMatches.Length > 0)
        {
            // Parse JSONL into export model (similar to finalize but without writing)
            var export = new ChatSessionExport();
            var messages = new List<ChatMessageDto>();
            var lines = await File.ReadAllLinesAsync(jsonlMatches[0]);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (type == "header" && doc.RootElement.TryGetProperty("header", out var headerEl))
                {
                    export.SessionId = headerEl.GetProperty("SessionId").GetGuid();
                    export.StartedUtc = headerEl.GetProperty("StartedUtc").GetDateTime();
                    export.Language = headerEl.GetProperty("Language").GetString() ?? string.Empty;
                    export.PromptId = headerEl.TryGetProperty("PromptId", out var pid) ? pid.GetString() : null;
                    export.PromptText = headerEl.TryGetProperty("PromptText", out var ptxt) ? ptxt.GetString() : null;
                    export.WhisperModel = headerEl.TryGetProperty("WhisperModel", out var w) ? w.GetString() : null;
                    export.ChatModel = headerEl.TryGetProperty("ChatModel", out var c) ? c.GetString() : null;
                    export.Title = headerEl.TryGetProperty("Title", out var t) ? t.GetString() : null;
                    export.Summary = headerEl.TryGetProperty("Summary", out var s) ? s.GetString() : null;
                }
                else if (type == "message" && doc.RootElement.TryGetProperty("message", out var msgEl))
                {
                    messages.Add(new ChatMessageDto
                    {
                        TimestampUtc = msgEl.GetProperty("TimestampUtc").GetDateTime(),
                        Author = (ChatAuthorDto)msgEl.GetProperty("Author").GetInt32(),
                        Text = msgEl.GetProperty("Text").GetString() ?? string.Empty
                    });
                }
            }
            export.Messages = messages;
            return export;
        }

        return null;
    }

    public async Task UpdateSessionTitleAsync(Guid sessionId, string newTitle)
    {
        var dir = _paths.GetChatsDirectory();
        var jsonPattern = $"*__{sessionId}.json";
        var jsonMatches = Directory.GetFiles(dir, jsonPattern);
        if (jsonMatches.Length == 0) return;

        var file = jsonMatches[0];
        var content = await File.ReadAllTextAsync(file);
        var export = JsonSerializer.Deserialize<ChatSessionExport>(content);
        if (export == null) return;

        export.Title = newTitle;
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(export, options));
    }

    public async Task<ChatSessionHeader?> ReopenSessionAsync(Guid sessionId)
    {
        var dir = _paths.GetChatsDirectory();
        var jsonPattern = $"*__{sessionId}.json";
        var jsonMatches = Directory.GetFiles(dir, jsonPattern);
        if (jsonMatches.Length == 0) return null;

        var jsonFile = jsonMatches[0];
        var content = await File.ReadAllTextAsync(jsonFile);
        var export = JsonSerializer.Deserialize<ChatSessionExport>(content);
        if (export == null) return null;

        // Convert back to JSONL for continued recording
        var jsonlFile = Path.ChangeExtension(jsonFile, ".jsonl");
        var header = new ChatSessionHeader
        {
            SessionId = export.SessionId,
            StartedUtc = export.StartedUtc,
            Language = export.Language,
            PromptId = export.PromptId,
            PromptText = export.PromptText,
            WhisperModel = export.WhisperModel,
            ChatModel = export.ChatModel,
            Title = export.Title,
            Summary = export.Summary
        };

        await using (var fs = new FileStream(jsonlFile, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var sw = new StreamWriter(fs))
        {
            // Write header
            await sw.WriteLineAsync(JsonSerializer.Serialize(new { type = "header", header }));

            // Write all existing messages
            foreach (var msg in export.Messages)
            {
                await sw.WriteLineAsync(JsonSerializer.Serialize(new { type = "message", message = msg }));
            }
        }

        // Delete the finalized JSON (we're back to JSONL now)
        File.Delete(jsonFile);

        return header;
    }

    public Task DeleteSessionAsync(Guid sessionId)
    {
        var dir = _paths.GetChatsDirectory();

        // Delete finalized .json
        var jsonMatches = Directory.GetFiles(dir, $"*__{sessionId}.json");
        foreach (var f in jsonMatches)
            File.Delete(f);

        // Delete in-progress .jsonl
        var jsonlMatches = Directory.GetFiles(dir, $"*__{sessionId}.jsonl");
        foreach (var f in jsonlMatches)
            File.Delete(f);

        return Task.CompletedTask;
    }
}
