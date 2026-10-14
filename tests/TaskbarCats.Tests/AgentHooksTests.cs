using System.Text.Json;
using TaskbarCats;
using Xunit;

public class AgentHooksTests
{
    [Fact] public void Claude_is_the_default_tool()
    {
        var s = new SessionInfo("id", @"c:\x\Proj", "n", SessionStatus.Idle, DateTimeOffset.Now);
        Assert.Equal(AgentTool.Claude, s.Tool);
        Assert.Null(s.Live); Assert.Null(s.Pid); Assert.Null(s.Host);
    }

    [Fact] public void Every_other_tool_has_its_own_badge_colour_and_claude_none()
    {
        Assert.Null(Tune.ToolColor(AgentTool.Claude));
        var others = Enum.GetValues<AgentTool>().Where(t => t != AgentTool.Claude).Select(t => Tune.ToolColor(t)).ToList();
        Assert.All(others, c => Assert.NotNull(c));
        Assert.Equal(others.Count, others.Distinct().Count());
    }

    readonly string _dir = Path.Combine(Path.GetTempPath(), "tc-hk-" + Guid.NewGuid());
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1791472090917);
    static readonly Func<string, string?> NoEnv = _ => null;
    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
    static HookRecord? P(AgentTool t, string ev, string json, Func<string, string?>? env = null, HookRecord? prev = null) =>
        Hook.Parse(t, ev, J(json), env ?? NoEnv, Now, 42, "123", "terminal", prev);

    [Theory]
    [InlineData("UserPromptSubmit", "working")] [InlineData("userPromptSubmitted", "working")]
    [InlineData("PostToolUse", "working")] [InlineData("postToolUse", "working")] [InlineData("AfterTool", "working")]
    [InlineData("beforeSubmitPrompt", "working")] [InlineData("BeforeAgent", "working")]
    [InlineData("PermissionRequest", "waiting")] [InlineData("notification", "waiting")]
    [InlineData("SessionStart", "idle")] [InlineData("Stop", "idle")] [InlineData("agentStop", "idle")] [InlineData("AfterAgent", "idle")]
    [InlineData("SessionEnd", "end")] [InlineData("sessionEnd", "end")]
    [InlineData("PreToolUse", null)]
    public void Event_status_table(string ev, string? status) => Assert.Equal(status, Hook.StatusOf(ev));

    [Fact] public void Codex_post_tool_use()
    {
        var r = P(AgentTool.Codex, "PostToolUse",
            """{"session_id":"019a-abc","cwd":"C:\\src\\Proj","model":"gpt-5-codex","hook_event_name":"PostToolUse","tool_name":"shell","tool_input":{"command":"rm -rf secret"}}""")!;
        Assert.Equal(("codex", "019a-abc", @"C:\src\Proj", "working", "shell", "gpt-5-codex", 1), (r.Tool, r.Id, r.Cwd, r.Status, r.ToolName, r.Model, r.Tools));
        Assert.Equal(Now, r.ToolAt);
    }

    [Fact] public void Copilot_camel_and_snake_fields()
    {
        var a = P(AgentTool.Copilot, "postToolUse", """{"sessionId":"s1","timestamp":1704614400000,"cwd":"C:\\p\\A","toolName":"bash","toolArgs":"{\"command\":\"ls\"}"}""")!;
        var b = P(AgentTool.Copilot, "postToolUse", """{"session_id":"s1","cwd":"C:\\p\\A","tool_name":"bash"}""")!;
        Assert.Equal(("s1", "bash"), (a.Id, a.ToolName));
        Assert.Equal(("s1", "bash"), (b.Id, b.ToolName));
    }

    [Fact] public void Cursor_uses_conversation_and_workspace_root()
    {
        var r = P(AgentTool.Cursor, "stop", """{"conversation_id":"c-9","workspace_roots":["/c:/Users/x/Proj"],"model":"claude-4.5","user_email":"a@b.c"}""")!;
        Assert.Equal(("c-9", @"c:\Users\x\Proj", "idle"), (r.Id, r.Cwd, r.Status));
    }

    [Fact] public void Gemini_falls_back_to_env()
    {
        var env = (string k) => k switch { "GEMINI_SESSION_ID" => "g1", "GEMINI_CWD" => @"C:\g\Proj", _ => null };
        var r = P(AgentTool.Gemini, "AfterTool", """{"tool_name":"read_file"}""", env)!;
        Assert.Equal(("g1", @"C:\g\Proj"), (r.Id, r.Cwd));
    }

    [Fact] public void No_id_writes_nothing() => Assert.Null(P(AgentTool.Codex, "Stop", """{"cwd":"C:\\x"}"""));

    [Fact] public void Unknown_event_writes_nothing() => Assert.Null(P(AgentTool.Codex, "PreToolUse", """{"session_id":"a","cwd":"C:\\x"}"""));

    [Fact] public void Count_and_model_carry_over_from_previous()
    {
        var first = P(AgentTool.Copilot, "postToolUse", """{"sessionId":"s","cwd":"C:\\x","toolName":"bash"}""")! with { Model = "gpt-5" };
        var next = P(AgentTool.Copilot, "agentStop", """{"sessionId":"s","cwd":"C:\\x"}""", prev: first)!;
        Assert.Equal((1, "gpt-5", "bash", "idle"), (next.Tools, next.Model, next.ToolName, next.Status));
    }

    [Fact] public void Never_writes_payload_text()
    {
        Directory.CreateDirectory(_dir);
        var r = P(AgentTool.Codex, "PostToolUse",
            """{"session_id":"abc","cwd":"C:\\x\\P","tool_name":"shell","prompt":"SECRET-PROMPT","tool_input":{"command":"SECRET-CMD"},"tool_response":"SECRET-OUT","last_assistant_message":"SECRET-MSG","transcript_path":"C:\\SECRET-PATH"}""")!;
        Hook.Save(_dir, r);
        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_dir)));
        Assert.DoesNotContain("SECRET", text);
        Directory.Delete(_dir, true);
    }

    [Fact] public void Save_then_end_deletes_and_ids_are_sanitized()
    {
        Directory.CreateDirectory(_dir);
        var r = P(AgentTool.Codex, "Stop", """{"session_id":"..\\..\\evil","cwd":"C:\\x"}""")!;
        Hook.Save(_dir, r);
        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal("codex-evil.json", Path.GetFileName(file));
        Assert.Equal("idle", Hook.Load(file)!.Status);
        Hook.Save(_dir, r with { Status = "end" });
        Assert.Empty(Directory.GetFiles(_dir));
        Directory.Delete(_dir, true);
    }

    static readonly (int, string)[] VsCodeChain = [(10, "pwsh.exe"), (11, "Code.exe"), (12, "Code.exe"), (13, "explorer.exe")];
    static readonly (int, string)[] TerminalChain = [(20, "cmd.exe"), (21, "node.exe"), (22, "pwsh.exe"), (23, "WindowsTerminal.exe")];

    [Fact] public void Agent_is_the_first_non_shell_ancestor()
    {
        Assert.Equal(11, Hook.AgentOf(VsCodeChain)?.Pid);
        Assert.Equal(21, Hook.AgentOf(TerminalChain)?.Pid);
        Assert.Null(Hook.AgentOf([(1, "cmd.exe")]));
    }

    [Fact] public void Host_from_chain()
    {
        Assert.Equal("vscode", Hook.HostOf(VsCodeChain));
        Assert.Equal("cursor", Hook.HostOf([(1, "node.exe"), (2, "Cursor.exe")]));
        Assert.Equal("terminal", Hook.HostOf(TerminalChain));
    }

    [Fact] public void Our_own_parent_is_in_the_chain() =>
        Assert.Contains(Native.Ancestors(Environment.ProcessId), a => a.Pid > 0 && a.Exe.Length > 0);

    [Fact] public void Run_never_throws_on_garbage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tc-run-" + Guid.NewGuid());
        Assert.Equal(0, Hook.Run([Hook.Verb, "codex", "Stop"], new StringReader("not json {{{"), dir));
        Assert.Equal(0, Hook.Run([Hook.Verb], new StringReader(""), dir));
        Assert.Equal(0, Hook.Run([Hook.Verb, "nope", "Stop"], new StringReader("{}"), dir));
        Assert.Equal(0, Hook.Run([Hook.Verb, "codex", "Stop"], new StringReader("""{"session_id":"r1","cwd":"C:\\x"}"""), dir));
        Assert.Single(Directory.GetFiles(dir));
        Directory.Delete(dir, true);
    }
}
