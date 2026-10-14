using System.Diagnostics;
using System.Text.Json;
using System.IO;
namespace TaskbarCats;

/// Which coding tool a session belongs to. Claude reads its own registry (SessionReader); the others report through hooks.
public enum AgentTool { Claude, Codex, Copilot, Cursor, Gemini }

/// One hook-reported session, as stored in %LOCALAPPDATA%\TaskbarCats\events\<tool>-<id>.json (overwritten per event).
/// Privacy: only these fields are ever written. Never prompts, messages, tool inputs/outputs, commands or file paths.
public sealed record HookRecord(string Tool, string Id, string Cwd, string Status, string Event, DateTimeOffset At,
    string? ToolName, DateTimeOffset? ToolAt, int Tools, string? Model, int? Pid, string? PidStart, string Host);

public static class Hook
{
    /// The CLI verb each tool's hook config runs; also the marker HookInstaller uses to find its own entries.
    public const string Verb = "taskbar-cats-hook";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarCats", "events");

    /// Event names of every supported tool (case-insensitive) → status. Null = not one of ours: write nothing.
    static readonly Dictionary<string, string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UserPromptSubmit"] = "working", ["userPromptSubmitted"] = "working", ["beforeSubmitPrompt"] = "working",
        ["BeforeAgent"] = "working", ["PostToolUse"] = "working", ["AfterTool"] = "working",
        ["PermissionRequest"] = "waiting", ["Notification"] = "waiting",
        ["SessionStart"] = "idle", ["Stop"] = "idle", ["agentStop"] = "idle", ["AfterAgent"] = "idle",
        ["SessionEnd"] = "end",
    };
    static readonly HashSet<string> ToolEvents = new(StringComparer.OrdinalIgnoreCase) { "PostToolUse", "AfterTool" };

    public static string? StatusOf(string ev) => Statuses.GetValueOrDefault(ev);

    public static HookRecord? Parse(AgentTool tool, string ev, JsonElement? payload, Func<string, string?> env, DateTimeOffset now,
        int? pid, string? pidStart, string host, HookRecord? previous)
    {
        if (StatusOf(ev) is not { } status) return null;
        var p = payload is { ValueKind: JsonValueKind.Object } o ? o : default;
        string? id = Str(p, "session_id") ?? Str(p, "sessionId") ?? Str(p, "conversation_id") ?? env("GEMINI_SESSION_ID");
        if (id is null) return null;
        string? cwd = Str(p, "cwd") ?? FirstRoot(p) ?? env("GEMINI_CWD") ?? env("GEMINI_PROJECT_DIR");
        if (cwd is null) return null;   // ponytail: no folder, no cat (a Cursor user hook's own cwd is ~/.cursor, never the project)
        string? toolName = ToolEvents.Contains(ev) ? Clip(Str(p, "tool_name") ?? Str(p, "toolName")) : null;
        return new HookRecord(tool.ToString().ToLowerInvariant(), id, Normalize(cwd), status, ev, now,
            toolName ?? previous?.ToolName, toolName is null ? previous?.ToolAt : now,
            (previous?.Tools ?? 0) + (toolName is null ? 0 : 1),
            Clip(Str(p, "model")) ?? previous?.Model, pid, pidStart, host);
    }

    /// Only [A-Za-z0-9_-] of the id reach the file name, at most 64 chars; null when nothing is left.
    public static string? FileFor(string dir, string tool, string id)
    {
        var safe = new string(id.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
        return safe.Length == 0 ? null : Path.Combine(dir, $"{tool}-{safe}.json");
    }

    public static HookRecord? Load(string path)
    {
        try { return JsonSerializer.Deserialize<HookRecord>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    /// Atomic overwrite (temp file + move) so the app never reads half a file; "end" deletes it.
    public static void Save(string dir, HookRecord r)
    {
        if (FileFor(dir, r.Tool, r.Id) is not { } path) return;
        if (r.Status == "end") { File.Delete(path); return; }
        Directory.CreateDirectory(dir);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
        File.Move(tmp, path, overwrite: true);   // ponytail: two hooks of one session racing = last writer wins, a tool count may be lost
    }

    static string? FirstRoot(JsonElement p) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty("workspace_roots", out var w) && w.ValueKind == JsonValueKind.Array
        && w.GetArrayLength() > 0 && w[0].ValueKind == JsonValueKind.String ? w[0].GetString() : null;

    /// "/c:/Users/x" (Cursor, unverified format) → "c:\Users\x"; Windows paths unchanged.
    static string Normalize(string path)
    {
        if (path.Length > 2 && path[0] == '/' && path[2] == ':') path = path[1..];
        return path.Replace('/', '\\');
    }

    static string? Clip(string? s) => s is null ? null : new string(s.Where(c => !char.IsControl(c)).Take(64).ToArray());

    static string? Str(JsonElement r, string n) =>
        r.ValueKind == JsonValueKind.Object && r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "sh.exe", "conhost.exe", "OpenConsole.exe", "wsl.exe" };

    /// The tool's own process: the nearest ancestor that is not a shell the hook was started through.
    public static (int Pid, string Exe)? AgentOf(IReadOnlyList<(int Pid, string Exe)> chain) =>
        chain.Where(a => !Shells.Contains(a.Exe)).Select(a => ((int, string)?)a).FirstOrDefault();

    public static string HostOf(IReadOnlyList<(int Pid, string Exe)> chain) =>
        chain.Any(a => a.Exe.Equals("Cursor.exe", StringComparison.OrdinalIgnoreCase)) ? "cursor"
        : chain.Any(a => a.Exe.StartsWith("Code", StringComparison.OrdinalIgnoreCase)) ? "vscode"   // Code.exe, "Code - Insiders.exe"
        : "terminal";

    /// TaskbarCats.exe taskbar-cats-hook <tool> <event>, payload on stdin. Always returns 0 and never throws:
    /// a cat is never worth failing or slowing the user's coding tool.
    public static int Run(string[] args, TextReader? stdin = null, string? dir = null)
    {
        try
        {
            if (args.Length < 3 || !Enum.TryParse<AgentTool>(args[1], true, out var tool) || tool == AgentTool.Claude) return 0;
            string ev = args[2];
            if (tool == AgentTool.Cursor && ev.Equals("beforeSubmitPrompt", StringComparison.OrdinalIgnoreCase))
                Console.Out.Write("{\"continue\":true}");   // explicit allow: Cursor reads this event's stdout
            string text = (stdin ?? Console.In).ReadToEnd();
            JsonElement? payload = null;
            try { payload = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
            var chain = Native.Ancestors(Environment.ProcessId);
            var agent = AgentOf(chain);
            string? start = null;
            if (agent is { } a)
                try { using var p = Process.GetProcessById(a.Pid); start = p.StartTime.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception) { }
            dir ??= DefaultDir;
            string toolKey = tool.ToString().ToLowerInvariant();
            HookRecord? previous = null;
            // previous needs the id, which needs the payload: parse once without, then reload with
            var first = Parse(tool, ev, payload, Environment.GetEnvironmentVariable, DateTimeOffset.Now, agent?.Pid, start, HostOf(chain), null);
            if (first is null) return 0;
            if (FileFor(dir, toolKey, first.Id) is { } path && File.Exists(path)) previous = Load(path);
            var rec = previous is null ? first : Parse(tool, ev, payload, Environment.GetEnvironmentVariable, DateTimeOffset.Now, agent?.Pid, start, HostOf(chain), previous)!;
            Save(dir, rec);
        }
        catch (Exception) { }   // ponytail: swallow everything; the tool must never see a failure from us
        return 0;
    }
}

/// Reads the hook-reported sessions (Hook.Save) with the same "active" rule as Claude: busy, waiting, or touched within the hour.
public static class EventReader
{
    public static List<SessionInfo> ReadActive(string dir, DateTimeOffset now, Func<int, string?, bool> isAlive)
    {
        var list = new List<SessionInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (Hook.Load(path) is not { } r || !Enum.TryParse<AgentTool>(r.Tool, true, out var tool) || tool == AgentTool.Claude) continue;
            bool dead = r.Pid is int pid && !isAlive(pid, r.PidStart);
            if (dead || now - r.At > Tune.EventExpiry) { TryDelete(path); continue; }
            var status = r.Status switch { "working" => SessionStatus.Working, "waiting" => SessionStatus.Waiting, _ => SessionStatus.Idle };
            if (status != SessionStatus.Idle && r.Pid is null && now - r.At > Tune.StaleWorking) status = SessionStatus.Idle;
            if (status == SessionStatus.Idle && now - r.At > SessionReader.ActiveWindow) continue;
            var live = new Signals(Tool: r.ToolName, ToolAt: r.ToolAt, Results: r.Tools, Model: r.Model, LastAt: r.At);
            list.Add(new SessionInfo($"{r.Tool}:{r.Id}", r.Cwd, Path.GetFileName(r.Cwd.TrimEnd('\\', '/')), status, r.At,
                Tool: tool, Live: live, Pid: r.Pid, Host: r.Host));
        }
        return list;
    }

    static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
}
