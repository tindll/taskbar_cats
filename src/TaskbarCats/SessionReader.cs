using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TaskbarCats;

public enum SessionStatus { Idle, Working, Waiting }

/// LastHumanAt: newest message the user typed within ActiveWindow (idle sessions only; null otherwise).
/// Tool/Live/Pid/Host: hook-reported sessions (AgentHooks.cs). Live = signals from the hook file (Claude: null, read from
/// transcripts). Pid = the agent process (liveness, terminal focus). Host = "vscode" (null), "cursor" or "terminal".
public sealed record SessionInfo(string SessionId, string Cwd, string Name, SessionStatus Status, DateTimeOffset UpdatedAt,
    DateTimeOffset? LastHumanAt = null, AgentTool Tool = AgentTool.Claude, Signals? Live = null, int? Pid = null, string? Host = null);

/// Reads Claude Code's own registry: %USERPROFILE%\.claude\sessions\<pid>.json (one per running process).
public static class SessionReader
{
    /// A conversation is shown while something is happening in it, or for an hour after the user last typed in it.
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromHours(1);

    public static string DefaultDir =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "sessions");

    /// lastHumanAt(sessionId, notBefore) = newest human message at or after notBefore (Transcripts.LastHumanAt).
    /// Null = registry timestamps only (tests, or no transcripts).
    public static List<SessionInfo> ReadActive(string dir, DateTimeOffset now, Func<int, string?, bool> isAlive,
        Func<string, DateTimeOffset, DateTimeOffset?>? lastHumanAt = null)
    {
        var list = new List<SessionInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                var r = doc.RootElement;
                var entry = Str(r, "entrypoint");
                if (entry is not ("claude-vscode" or "cli")) continue;   // sdk/agents runs have no window to show
                if (Str(r, "kind") is { } kind && kind != "interactive") continue;
                if (Str(r, "sessionId") is not { } id || Str(r, "cwd") is not { } cwd) continue;
                if (!r.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out int pid)) continue;
                if (!isAlive(pid, Str(r, "procStart"))) continue;

                var status = Str(r, "status") switch
                {
                    "busy" or "shell" => SessionStatus.Working,   // shell = background shell task
                    "waiting" => SessionStatus.Waiting,
                    _ => SessionStatus.Idle,
                };
                var updated = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(Num(r, "updatedAt"), Num(r, "statusUpdatedAt")));
                // Every human message starts a turn, which bumps updatedAt: no update in the window means no message either,
                // so the transcript is only read for idle sessions touched in the last hour.
                if (status == SessionStatus.Idle && now - updated > ActiveWindow) continue;
                DateTimeOffset? human = null;
                if (status == SessionStatus.Idle && lastHumanAt is not null)
                {
                    human = lastHumanAt(id, now - ActiveWindow);
                    if (human is null) continue;   // the turn that ended recently was not started by the user within the hour
                }
                list.Add(new SessionInfo(id, cwd, Str(r, "name") ?? id, status, updated, human,
                    Pid: pid, Host: entry == "cli" ? "terminal" : null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
            {
                // Mid-write or deleted file: skip this poll. Roster's 2-poll grace keeps the cat on screen.
            }
        }
        return list;
    }

    /// procStart in the registry is the process start time as a Windows FILETIME (verified on this machine).
    public static bool IsAlive(int pid, string? procStart)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return procStart is null || p.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture) == procStart;
        }
        catch (ArgumentException) { return false; }          // no such pid
        catch (InvalidOperationException) { return false; }  // exited
        catch (Win32Exception) { return true; }              // ponytail: start time unreadable, trust the pid
    }

    static string? Str(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long Num(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
}

/// Turns successive poll results into spawn/despawn events. A session must be missing
/// on two consecutive polls before it is removed, so one unreadable file never makes a cat blink.
public sealed class Roster
{
    readonly Dictionary<string, int> _misses = new();

    public (List<SessionInfo> Added, List<string> Removed) Update(IReadOnlyList<SessionInfo> seen)
    {
        var added = new List<SessionInfo>();
        var removed = new List<string>();
        var ids = seen.Select(s => s.SessionId).ToHashSet();
        foreach (var s in seen)
        {
            if (!_misses.ContainsKey(s.SessionId)) added.Add(s);
            _misses[s.SessionId] = 0;
        }
        foreach (var id in _misses.Keys.Where(k => !ids.Contains(k)).ToList())
            if (++_misses[id] >= 2) { _misses.Remove(id); removed.Add(id); }
        return (added, removed);
    }

    /// Forget a session now (its project was hidden), so it counts as new when it is shown again.
    public void Drop(string id) => _misses.Remove(id);
}
