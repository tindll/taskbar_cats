using System.Globalization;
using System.Text.Json;

namespace TaskbarCats;

/// What a conversation's transcript says right now. Only tool NAMES, counts, timestamps, the model id and status
/// flags are kept: never message text, tool inputs, tool outputs, file paths or commands.
/// Results, Errors, Compactions and ApiErrors only grow, so a change between two polls is an event.
public sealed record Signals(
    string? Tool = null, DateTimeOffset? ToolAt = null, int Open = 0,
    int Results = 0, int Errors = 0, int Edits = 0,
    long Context = 0, long MaxContext = 0, string? Model = null, string? Stop = null,
    int Compactions = 0, string? ApiError = null, int ApiErrors = 0,
    DateTimeOffset? LastHuman = null, DateTimeOffset? LastAt = null);

/// Turns main-chain transcript records into Signals, one JSONL line at a time (Transcripts feeds only new bytes).
/// Record shapes verified on 2.1.285 to 2.1.294.
public sealed class SignalState
{
    static readonly string[] EditTools = ["Edit", "Write", "MultiEdit", "NotebookEdit"];
    const int MaxOpen = 64;   // ponytail: tool ids whose result never came (interrupts) are dropped past this
    readonly HashSet<string> _open = [];
    Signals _s = new();

    public Signals Snapshot => _s with { Open = _open.Count };

    public void Feed(ReadOnlySpan<byte> line)
    {
        // Cheap needles first: most lines are none of these. Then a real parse, so text that merely mentions them is harmless.
        if (line.IndexOf("\"assistant\""u8) < 0 && line.IndexOf("\"tool_result\""u8) < 0
            && line.IndexOf("\"human\""u8) < 0 && line.IndexOf("compact_boundary"u8) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return;
            DateTimeOffset? at = DateTimeOffset.TryParse(Str(r, "timestamp"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var t) ? t : null;
            switch (Str(r, "type"))
            {
                case "assistant": Assistant(r, at); break;
                case "user": User(r, at); break;
                case "system" when Str(r, "subtype") == "compact_boundary":
                    long post = r.TryGetProperty("compactMetadata", out var cm) && cm.ValueKind == JsonValueKind.Object ? Num(cm, "postTokens") : 0;
                    _s = _s with { Compactions = _s.Compactions + 1, Context = post, LastAt = at ?? _s.LastAt };
                    break;
            }
        }
        catch (JsonException) { }   // half-written last line, or not JSON
    }

    void Assistant(JsonElement r, DateTimeOffset? at)
    {
        if (!r.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
        if (r.TryGetProperty("isApiErrorMessage", out var e) && e.ValueKind == JsonValueKind.True)
        {
            _s = _s with { ApiError = Str(r, "error") ?? "unknown", ApiErrors = _s.ApiErrors + 1, LastAt = at ?? _s.LastAt };
            return;
        }
        if (Str(m, "model") is not { } model || model == "<synthetic>") return;
        long ctx = 0;
        if (m.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            ctx = Num(u, "input_tokens") + Num(u, "cache_read_input_tokens") + Num(u, "cache_creation_input_tokens");
        _s = _s with
        {
            Model = model, Stop = Str(m, "stop_reason"), ApiError = null, LastAt = at ?? _s.LastAt,
            Context = ctx > 0 ? ctx : _s.Context, MaxContext = Math.Max(_s.MaxContext, ctx),
        };
        if (!m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        foreach (var b in content.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object || Str(b, "type") != "tool_use" || Str(b, "name") is not { } name) continue;
            if (_open.Count >= MaxOpen) _open.Clear();
            if (Str(b, "id") is { } id) _open.Add(id);
            _s = _s with { Tool = name, ToolAt = at, Edits = _s.Edits + (EditTools.Contains(name) ? 1 : 0) };
        }
    }

    void User(JsonElement r, DateTimeOffset? at)
    {
        if (r.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object && Str(o, "kind") == "human")
        {
            _open.Clear();   // a new turn: calls still open were interrupted, and the old end_turn no longer applies
            _s = _s with { LastHuman = at, LastAt = at ?? _s.LastAt, Edits = 0, Stop = null };
        }
        if (!r.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object
            || !m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        foreach (var b in content.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object || Str(b, "type") != "tool_result") continue;
            if (Str(b, "tool_use_id") is { } id) _open.Remove(id);
            bool error = b.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
            _s = _s with { Results = _s.Results + 1, Errors = _s.Errors + (error ? 1 : 0), LastAt = at ?? _s.LastAt };
        }
    }

    static string? Str(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long Num(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
}
