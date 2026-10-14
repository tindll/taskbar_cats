using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskbarCats;

/// Adds/removes our hook entries in each tool's config. Our entries are found by the verb in their command
/// (Hook.Verb), never by position, so the user's own hooks are never touched. Refuses (throws) on files that are not
/// strict JSON objects: the caller then prints manual instructions instead of rewriting them.
public static partial class HookInstaller
{
    public static string[] Events(AgentTool t) => t switch
    {
        AgentTool.Codex => ["SessionStart", "UserPromptSubmit", "PostToolUse", "PermissionRequest", "Stop", "SessionEnd"],
        AgentTool.Cursor => ["sessionStart", "beforeSubmitPrompt", "postToolUse", "stop", "sessionEnd"],
        AgentTool.Gemini => ["SessionStart", "BeforeAgent", "AfterTool", "Notification", "AfterAgent", "SessionEnd"],
        AgentTool.Copilot => ["sessionStart", "userPromptSubmitted", "postToolUse", "notification", "agentStop", "sessionEnd"],
        _ => [],
    };

    /// Unquoted when possible: the tool may run the command through cmd, PowerShell or sh, and only a bare path works in all three.
    public static string ExeArg(string exe, Func<string, string?> shortPath) =>
        !exe.Contains(' ') ? exe : shortPath(exe) is { } s && !s.Contains(' ') ? s : $"\"{exe}\"";

    public static string Command(string exeArg, AgentTool tool, string ev) => $"{exeArg} {Hook.Verb} {tool.ToString().ToLowerInvariant()} {ev}";

    public static bool Has(string json) => json.Contains(Hook.Verb, StringComparison.Ordinal);

    static readonly JsonDocumentOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    public static string Merge(string? json, AgentTool tool, string exeArg)
    {
        string nl = json?.Contains("\r\n") == true ? "\r\n" : "\n";
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : Parse(Unmerge(json));
        bool flat = tool == AgentTool.Cursor;
        if (flat && root["version"] is null) root["version"] = 1;
        var hooks = root["hooks"] as JsonObject ?? (root["hooks"] is null ? new JsonObject() : throw new InvalidOperationException("hooks is not an object"));
        root["hooks"] = hooks;
        int timeout = tool == AgentTool.Gemini ? Tune.HookTimeoutSec * 1000 : Tune.HookTimeoutSec;
        foreach (var ev in Events(tool))
        {
            var list = hooks[ev] as JsonArray ?? (hooks[ev] is null ? new JsonArray() : throw new InvalidOperationException($"hooks.{ev} is not a list"));
            hooks[ev] = list;
            var cmd = Command(exeArg, tool, ev);
            list.Add(flat
                ? new JsonObject { ["command"] = cmd, ["timeout"] = timeout }
                : new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = cmd, ["timeout"] = timeout }) });
        }
        return Write(root, nl);
    }

    /// Removes every handler whose command carries our verb, then the groups, event lists and "hooks" object that became empty
    /// because of that (a list the user left empty stays).
    public static string Unmerge(string json)
    {
        string nl = json.Contains("\r\n") ? "\r\n" : "\n";
        var root = Parse(json);
        if (root["hooks"] is not JsonObject hooks) return json;
        bool any = false;
        foreach (var (ev, node) in hooks.ToList())
        {
            if (node is not JsonArray list) continue;
            bool touched = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is not JsonObject e) continue;
                if (Ours(e)) { list.RemoveAt(i); touched = true; continue; }
                if (e["hooks"] is JsonArray inner)
                {
                    int before = inner.Count;
                    for (int j = inner.Count - 1; j >= 0; j--) if (inner[j] is JsonObject h && Ours(h)) inner.RemoveAt(j);
                    if (inner.Count < before) { touched = true; if (inner.Count == 0) list.RemoveAt(i); }
                }
            }
            if (touched && list.Count == 0) hooks.Remove(ev);
            any |= touched;
        }
        if (!any) return json;
        if (hooks.Count == 0) root.Remove("hooks");
        return Write(root, nl);
    }

    /// Nothing left but our own scaffolding: {} or {"version":1}.
    public static bool IsEmpty(string json) => Parse(json) is var r && r.All(p => p.Key == "version");

    public static string CopilotFile(string exeArg)
    {
        var hooks = new JsonObject();
        foreach (var ev in Events(AgentTool.Copilot))
            hooks[ev] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(exeArg, AgentTool.Copilot, ev), ["timeoutSec"] = Tune.HookTimeoutSec });
        return Write(new JsonObject { ["version"] = 1, ["hooks"] = hooks }, "\n");
    }

    static bool Ours(JsonObject h) => h["command"] is JsonValue v && v.TryGetValue<string>(out var c) && c.Contains(Hook.Verb, StringComparison.Ordinal);

    static JsonObject Parse(string json) =>
        JsonNode.Parse(json, documentOptions: Strict) as JsonObject ?? throw new InvalidOperationException("root is not an object");

    static string Write(JsonObject root, string nl) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = nl, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

public sealed record HookTarget(AgentTool Tool, string Path, bool Detected);

public static partial class HookInstaller
{
    const string Backup = ".taskbar-cats.bak";

    /// Where each tool's hook config lives; Detected = the tool's own folder exists (we never create a tool's folder).
    /// Copilot covers both the CLI and VS Code agent mode: both load ~/.copilot/hooks/*.json.
    public static List<HookTarget> Targets(string home, Func<string, string?> env)
    {
        string codex = env("CODEX_HOME") ?? System.IO.Path.Combine(home, ".codex");
        string copilot = env("COPILOT_HOME") ?? System.IO.Path.Combine(home, ".copilot");
        string vscode = System.IO.Path.Combine(env("APPDATA") ?? "", "Code", "User");
        return
        [
            new(AgentTool.Codex, System.IO.Path.Combine(codex, "hooks.json"), Directory.Exists(codex)),
            new(AgentTool.Copilot, System.IO.Path.Combine(copilot, "hooks", "taskbar-cats.json"), Directory.Exists(copilot) || (env("APPDATA") is not null && Directory.Exists(vscode))),
            new(AgentTool.Cursor, System.IO.Path.Combine(home, ".cursor", "hooks.json"), Directory.Exists(System.IO.Path.Combine(home, ".cursor"))),
            new(AgentTool.Gemini, System.IO.Path.Combine(home, ".gemini", "settings.json"), Directory.Exists(System.IO.Path.Combine(home, ".gemini"))),
        ];
    }

    /// After an app update the exe path may have changed: rewrite the hooks of tools that already have ours, install nothing new.
    public static List<string> Refresh(string home, Func<string, string?> env, string exeArg) =>
        Install(home, env, exeArg, only: t => File.Exists(t.Path) && Has(SafeRead(t.Path)), quiet: true);

    public static List<string> Install(string home, Func<string, string?> env, string exeArg, Func<HookTarget, bool>? only = null, bool quiet = false)
    {
        var report = new List<string>();
        if (!quiet) report.Add("Claude Code: built in, nothing to install.");
        foreach (var t in Targets(home, env))
        {
            if (only is not null && !only(t)) continue;
            if (!t.Detected) { report.Add($"{Tune.ToolLabel(t.Tool)}: not found, skipped."); continue; }
            try
            {
                if (t.Tool == AgentTool.Copilot)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(t.Path)!);
                    WriteAtomic(t.Path, CopilotFile(exeArg));
                }
                else
                {
                    string? old = File.Exists(t.Path) ? File.ReadAllText(t.Path) : null;
                    string merged = Merge(old, t.Tool, exeArg);   // throws before anything is written
                    if (old is not null && !File.Exists(t.Path + Backup)) File.Copy(t.Path, t.Path + Backup);
                    WriteAtomic(t.Path, merged);
                }
                report.Add($"{Tune.ToolLabel(t.Tool)}: hooks added to {t.Path}" + (t.Tool == AgentTool.Codex ? " (run /hooks in Codex once to trust them)" : ""));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                report.Add($"{Tune.ToolLabel(t.Tool)}: {t.Path} could not be merged safely ({e.Message}); left unchanged. Add these by hand:");
                report.Add(t.Tool == AgentTool.Copilot ? CopilotFile(exeArg) : Merge(null, t.Tool, exeArg));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { report.Add($"{Tune.ToolLabel(t.Tool)}: {e.Message}"); }
        }
        return report;
    }

    public static List<string> Uninstall(string home, Func<string, string?> env)
    {
        var report = new List<string>();
        foreach (var t in Targets(home, env))
        {
            if (!File.Exists(t.Path)) continue;
            try
            {
                string text = File.ReadAllText(t.Path);
                if (!Has(text)) continue;
                if (t.Tool == AgentTool.Copilot) File.Delete(t.Path);   // our own file
                else
                {
                    string rest = Unmerge(text);
                    if (IsEmpty(rest) && !File.Exists(t.Path + Backup)) File.Delete(t.Path);   // we created it
                    else WriteAtomic(t.Path, rest);
                }
                report.Add($"{Tune.ToolLabel(t.Tool)}: hooks removed from {t.Path}");
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                report.Add($"{Tune.ToolLabel(t.Tool)}: could not edit {t.Path} ({e.Message}); remove the lines containing '{Hook.Verb}' by hand.");
            }
        }
        if (report.Count == 0) report.Add("No Taskbar Cats hooks found.");
        return report;
    }

    /// Upgrade: one line for the menu, e.g. "Claude ✓ · Codex ✓ · Copilot – · Cursor ✓ · Gemini not found".
    public static string Status(string home, Func<string, string?> env) =>
        string.Join(" · ", Targets(home, env).Select(t =>
            $"{Tune.ToolLabel(t.Tool)} " + (!t.Detected ? "not found" : File.Exists(t.Path) && Has(SafeRead(t.Path)) ? "✓" : "–"))
            .Prepend("Claude ✓"));

    /// TaskbarCats.exe hooks install|uninstall|status — prints to the calling console. Exit 0 always (status text says what happened).
    public static int Cli(string[] args)
    {
        Native.AttachParentConsole();
        var outw = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Func<string, string?> env = Environment.GetEnvironmentVariable;
        var lines = (args.Length > 1 ? args[1] : "status") switch
        {
            "install" => Install(home, env, ExeArg(AppPaths.Exe, Native.ShortPath)),
            "uninstall" => Uninstall(home, env),
            _ => [Status(home, env)],
        };
        foreach (var l in lines) outw.WriteLine(l);
        return 0;
    }

    static string SafeRead(string path) { try { return File.ReadAllText(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }

    static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);   // UTF-8 without BOM
        File.Move(tmp, path, overwrite: true);
    }
}
