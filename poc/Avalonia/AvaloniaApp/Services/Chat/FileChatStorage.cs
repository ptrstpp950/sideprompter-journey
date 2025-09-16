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
        // We need the filename. We assume first header already created in this run; to avoid caching state here,
        // we encode sessionId into filename via a convention: the newest file matching sessionId is used.
        // For simplicity, we also allow writing using a "current" header on first call.
        // In practice, ChatSessionService will call AppendSessionHeaderAsync before the first message.
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
                    // If there was an earlier summary record, keep latest
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
    }
}
