using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TaskbarCats;

/// Finds when the user last typed into a conversation, from Claude Code's transcript
/// %USERPROFILE%\.claude\projects\<project dir>\<sessionId>.jsonl (read only, never written).
/// A human message is a record with "type":"user" and "origin":{"kind":"human"}. Tool results, sub-agent
/// hand-backs, task notifications, /model echoes and compaction summaries are also "type":"user" but have
/// no origin or a different origin.kind (verified on transcripts from 2.1.223 to 2.1.294).
public sealed class Transcripts(string projectsDir)
{
    public static string DefaultDir =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "projects");

    readonly Dictionary<string, string> _paths = new();
    readonly Dictionary<string, (long Length, DateTimeOffset NotBefore, DateTimeOffset? Result)> _cache = new();

    /// Newest human message at or after notBefore, or null (none, no transcript, or unreadable).
    public DateTimeOffset? LastHumanAt(string sessionId, DateTimeOffset notBefore)
    {
        if (!Guid.TryParse(sessionId, out var g)) return null;   // only a GUID ever becomes part of a path
        try
        {
            if (Find(g.ToString()) is not { } path) return null;
            long length = new FileInfo(path).Length;
            // Same length = same content. notBefore only moves forward, so an older answer can only expire.
            if (_cache.TryGetValue(path, out var c) && c.Length == length && c.NotBefore <= notBefore)
                return c.Result >= notBefore ? c.Result : null;
            var result = Scan(path, notBefore);
            _cache[path] = (length, notBefore, result);
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// The conversation's tab title: the newest {"type":"ai-title","aiTitle":"..."} record, or null.
    /// VS Code shows it (cut to about 24 characters with "…") as the first part of the window title while that
    /// conversation's tab is active (seen on 2.1.295: "Taskbar cat walker proje… - MyApp - Visual Studio Code").
    // ponytail: reads the whole file forwards; only runs when a cat is fed, a few MB at most.
    public string? Title(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var g)) return null;
        try
        {
            if (Find(g.ToString()) is not { } path) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            string? title = null;
            for (string? line; (line = reader.ReadLine()) is not null;)
            {
                if (!line.Contains("\"ai-title\"")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (r.ValueKind == JsonValueKind.Object
                        && r.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "ai-title"
                        && r.TryGetProperty("aiTitle", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                        title = t.GetString();
                }
                catch (JsonException) { }   // half-written last line, or the text only mentions ai-title
            }
            return title;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// Bytes read at most per call; first sight of a file reads its last TailCap bytes.
    internal int TailCap { get; init; } = 256 * 1024;
    public static readonly TimeSpan SubagentWindow = TimeSpan.FromSeconds(90);

    sealed class Tail { public long Pos; public bool CutLine; public byte[] Carry = []; public SignalState State = new(); }
    readonly Dictionary<string, Tail> _tails = new();

    /// Live signals for a conversation. Each call stats the file; it reads only the bytes appended since the last
    /// call (at most TailCap), feeds whole lines to the session's SignalState and keeps an unfinished last line
    /// for next time. Null when there is no transcript, the id is not a GUID, or the file cannot be read.
    public Signals? Signals(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var g)) return null;
        try
        {
            if (Find(g.ToString()) is not { } path) return null;
            long length = new FileInfo(path).Length;
            // First sight, a rewritten (shorter) file, or a jump too big to read: start over from the tail.
            // Counters are then "at least" (Edits since the user typed may have started before the tail).
            if (!_tails.TryGetValue(path, out var t) || length < t.Pos || length - t.Pos > TailCap)
                _tails[path] = t = new Tail { Pos = Math.Max(0, length - TailCap), CutLine = length > TailCap };
            if (length > t.Pos) ReadNew(path, t, length);
            return t.State.Snapshot;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void ReadNew(string path, Tail t, long length)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = t.Pos;
        var buf = new byte[length - t.Pos];
        fs.ReadExactly(buf);          // only up to the length we stat'ed; later bytes wait for the next poll
        t.Pos = length;
        byte[] data = t.Carry.Length == 0 ? buf : [.. t.Carry, .. buf];
        int start = 0;
        for (int nl; (nl = data.AsSpan(start).IndexOf((byte)'\n')) >= 0; start += nl + 1)
        {
            if (t.CutLine) { t.CutLine = false; continue; }   // started mid-file: the first line is a fragment
            t.State.Feed(data.AsSpan(start, nl));
        }
        t.Carry = data.Length - start > 1024 * 1024 ? [] : data[start..];   // ponytail: a >1 MB unfinished line is dropped
    }

    /// Sub-agents that wrote to their own transcript (<project dir>\<sessionId>\subagents\agent-*.jsonl) within
    /// SubagentWindow. 0 on any problem.
    // ponytail: a recent-write heuristic, not a running flag (Claude Code writes none). Bash p95 is 57 s, so a 90 s
    // window rarely flickers; an agent that ends shows for up to 90 s more.
    public int RunningSubagents(string sessionId, DateTimeOffset now)
    {
        if (!Guid.TryParse(sessionId, out var g)) return 0;
        try
        {
            if (Find(g.ToString()) is not { } path) return 0;
            var dir = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(path)!, g.ToString(), "subagents"));
            if (!dir.Exists) return 0;
            return dir.EnumerateFiles("agent-*.jsonl").Count(f => now - new DateTimeOffset(f.LastWriteTimeUtc) <= SubagentWindow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// The project folder name is the cwd with every non-alphanumeric char replaced by '-'. Rather than
    /// re-implement that rule, look for the file in every project folder and remember where it was.
    string? Find(string id)
    {
        if (_paths.TryGetValue(id, out var known) && File.Exists(known)) return known;
        if (!Directory.Exists(projectsDir)) return null;
        foreach (var dir in Directory.EnumerateDirectories(projectsDir))
        {
            var p = Path.Combine(dir, id + ".jsonl");
            if (File.Exists(p)) return _paths[id] = p;
        }
        return null;
    }

    /// Reads the file backwards in chunks: newest records first, stops at the first human record or at the
    /// first record older than notBefore. A half-written last line fails to parse and is skipped.
    // ponytail: parses every timestamped line it passes; a long agent run inside the last hour costs one
    // full scan of that hour, then the length cache makes it free until the file grows.
    internal static DateTimeOffset? Scan(string path, DateTimeOffset notBefore, int chunk = 64 * 1024)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long pos = fs.Length;
        var buf = new byte[chunk];
        byte[] pending = [];   // start of a line that began in an earlier (not yet read) chunk
        while (pos > 0)
        {
            int n = (int)Math.Min(chunk, pos);
            pos -= n;
            fs.Position = pos;
            fs.ReadExactly(buf, 0, n);
            byte[] data = [.. buf.AsSpan(0, n), .. pending];
            int end = data.Length;
            for (int i = data.Length - 1; i >= -1; i--)
            {
                if (i >= 0 && data[i] != (byte)'\n') continue;
                if (i < 0 && pos > 0) break;                    // line continues in the previous chunk
                var line = data.AsSpan(i + 1, end - i - 1);
                end = i;
                if (Read(line) is not { } rec) continue;
                if (rec.At < notBefore) return null;
                if (rec.Human) return rec.At;
            }
            pending = data[..Math.Max(end, 0)];
        }
        return null;
    }

    static (bool Human, DateTimeOffset At)? Read(ReadOnlySpan<byte> line)
    {
        if (line.IndexOf("\"timestamp\""u8) < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String) return null;
            if (!DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return null;
            bool human = r.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "user"
                && r.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object
                && o.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "human";
            return (human, at);
        }
        catch (JsonException) { return null; }
    }
}
