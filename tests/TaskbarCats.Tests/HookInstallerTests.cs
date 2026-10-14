using System.Text.Json;
using System.Text.Json.Nodes;
using TaskbarCats;
using Xunit;

public class HookInstallerTests
{
    const string Exe = @"C:\Apps\TaskbarCats\TaskbarCats.exe";

    [Fact] public void Exe_arg_unquoted_short_or_quoted()
    {
        Assert.Equal(Exe, HookInstaller.ExeArg(Exe, _ => null));
        Assert.Equal(@"C:\PROGRA~1\TC\TC.exe", HookInstaller.ExeArg(@"C:\Program Files\TC\TC.exe", _ => @"C:\PROGRA~1\TC\TC.exe"));
        Assert.Equal("\"C:\\Program Files\\TC\\TC.exe\"", HookInstaller.ExeArg(@"C:\Program Files\TC\TC.exe", _ => null));
    }

    [Fact] public void Command_carries_the_marker() =>
        Assert.Equal($"{Exe} taskbar-cats-hook codex Stop", HookInstaller.Command(Exe, AgentTool.Codex, "Stop"));

    [Fact] public void Codex_new_file_has_grouped_entries_for_every_event()
    {
        var root = JsonNode.Parse(HookInstaller.Merge(null, AgentTool.Codex, Exe))!;
        foreach (var ev in HookInstaller.Events(AgentTool.Codex))
        {
            var h = root["hooks"]![ev]![0]!["hooks"]![0]!;
            Assert.Equal("command", (string)h["type"]!);
            Assert.Equal(HookInstaller.Command(Exe, AgentTool.Codex, ev), (string)h["command"]!);
            Assert.Equal(5, (int)h["timeout"]!);
        }
    }

    [Fact] public void Cursor_is_flat_with_version()
    {
        var root = JsonNode.Parse(HookInstaller.Merge(null, AgentTool.Cursor, Exe))!;
        Assert.Equal(1, (int)root["version"]!);
        Assert.Equal(HookInstaller.Command(Exe, AgentTool.Cursor, "stop"), (string)root["hooks"]!["stop"]![0]!["command"]!);
    }

    [Fact] public void Gemini_timeout_in_ms_and_other_settings_kept()
    {
        var root = JsonNode.Parse(HookInstaller.Merge("""{"theme":"Dracula","general":{"vimMode":true}}""", AgentTool.Gemini, Exe))!;
        Assert.Equal("Dracula", (string)root["theme"]!);
        Assert.True((bool)root["general"]!["vimMode"]!);
        Assert.Equal(5000, (int)root["hooks"]!["AfterTool"]![0]!["hooks"]![0]!["timeout"]!);
    }

    [Fact] public void Install_twice_is_idempotent()
    {
        var once = HookInstaller.Merge(null, AgentTool.Codex, Exe);
        Assert.Equal(once, HookInstaller.Merge(once, AgentTool.Codex, Exe));
    }

    const string UserCodex = """{"hooks":{"Stop":[{"matcher":"","hooks":[{"type":"command","command":"python3 notify.py"}]}],"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"guard.exe"}]}]}}""";

    [Fact] public void User_hooks_survive_round_trip()
    {
        var merged = HookInstaller.Merge(UserCodex, AgentTool.Codex, Exe);
        Assert.Equal(2, JsonNode.Parse(merged)!["hooks"]!["Stop"]!.AsArray().Count);
        var back = HookInstaller.Unmerge(merged);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(UserCodex), JsonNode.Parse(back)));
        Assert.False(HookInstaller.Has(back));
    }

    [Fact] public void Unmerge_of_our_only_content_is_empty() =>
        Assert.True(HookInstaller.IsEmpty(HookInstaller.Unmerge(HookInstaller.Merge(null, AgentTool.Cursor, Exe))));

    [Fact] public void Comments_refused() =>
        Assert.ThrowsAny<JsonException>(() => HookInstaller.Merge("{ // mine\n \"hooks\": {} }", AgentTool.Codex, Exe));

    [Fact] public void Hooks_not_object_refused() =>
        Assert.Throws<InvalidOperationException>(() => HookInstaller.Merge("""{"hooks":[1,2]}""", AgentTool.Codex, Exe));

    [Fact] public void Crlf_preserved()
    {
        var merged = HookInstaller.Merge("{\r\n  \"a\": 1\r\n}", AgentTool.Codex, Exe);
        Assert.Contains("\r\n", merged);
        Assert.DoesNotContain("\n", merged.Replace("\r\n", ""));
    }

    [Fact] public void Copilot_file_is_version_1_camel_case()
    {
        var root = JsonNode.Parse(HookInstaller.CopilotFile(Exe))!;
        Assert.Equal(1, (int)root["version"]!);
        Assert.Equal(HookInstaller.Command(Exe, AgentTool.Copilot, "agentStop"), (string)root["hooks"]!["agentStop"]![0]!["command"]!);
        Assert.Equal(5, (int)root["hooks"]!["agentStop"]![0]!["timeoutSec"]!);
    }

    sealed class Home : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "tc-home-" + Guid.NewGuid());
        public Home(params string[] subdirs) { foreach (var s in subdirs) Directory.CreateDirectory(Path.Combine(Dir, s)); }
        public string P(params string[] parts) => Path.Combine([Dir, .. parts]);
        public void Dispose() => Directory.Delete(Dir, true);
    }
    static readonly Func<string, string?> NoEnv = _ => null;

    [Fact] public void Install_only_touches_detected_tools_and_backs_up()
    {
        using var h = new Home(".codex", ".cursor");
        File.WriteAllText(h.P(".codex", "hooks.json"), UserCodex);
        HookInstaller.Install(h.Dir, NoEnv, Exe);
        Assert.Equal(UserCodex, File.ReadAllText(h.P(".codex", "hooks.json.taskbar-cats.bak")));
        Assert.True(HookInstaller.Has(File.ReadAllText(h.P(".codex", "hooks.json"))));
        Assert.True(File.Exists(h.P(".cursor", "hooks.json")));
        Assert.False(Directory.Exists(h.P(".gemini")));
        Assert.False(Directory.Exists(h.P(".copilot")));
    }

    [Fact] public void Uninstall_restores_user_file_and_removes_created_ones()
    {
        using var h = new Home(".codex", ".cursor", ".copilot");
        File.WriteAllText(h.P(".codex", "hooks.json"), UserCodex);
        HookInstaller.Install(h.Dir, NoEnv, Exe);
        Assert.True(File.Exists(h.P(".copilot", "hooks", "taskbar-cats.json")));
        HookInstaller.Uninstall(h.Dir, NoEnv);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(UserCodex), JsonNode.Parse(File.ReadAllText(h.P(".codex", "hooks.json")))));
        Assert.False(File.Exists(h.P(".cursor", "hooks.json")));
        Assert.False(File.Exists(h.P(".copilot", "hooks", "taskbar-cats.json")));
    }

    [Fact] public void Unmergeable_file_left_alone_with_manual_steps()
    {
        using var h = new Home(".codex");
        const string commented = "{ // mine\n \"hooks\": {} }";
        File.WriteAllText(h.P(".codex", "hooks.json"), commented);
        var report = HookInstaller.Install(h.Dir, NoEnv, Exe);
        Assert.Equal(commented, File.ReadAllText(h.P(".codex", "hooks.json")));
        Assert.Contains(report, l => l.Contains("by hand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void Env_homes_respected()
    {
        using var h = new Home("ch");
        var env = (string k) => k == "CODEX_HOME" ? Path.Combine(h.Dir, "ch") : null;
        Assert.Equal(Path.Combine(h.Dir, "ch", "hooks.json"), HookInstaller.Targets(h.Dir, env).Single(t => t.Tool == AgentTool.Codex).Path);
    }

    [Fact] public void Status_line_names_each_tool()
    {
        using var h = new Home(".cursor");
        HookInstaller.Install(h.Dir, NoEnv, Exe);
        var line = HookInstaller.Status(h.Dir, NoEnv);
        Assert.Contains("Cursor ✓", line);
        Assert.Contains("Codex not found", line);
    }

    [Fact] public void Refresh_rewrites_paths_only_for_tools_already_hooked()
    {
        using var h = new Home(".codex", ".cursor");
        HookInstaller.Install(h.Dir, NoEnv, Exe, only: t => t.Tool == AgentTool.Codex);   // cursor folder exists but is not hooked
        Assert.False(File.Exists(h.P(".cursor", "hooks.json")));
        const string NewExe = @"C:\Apps\TaskbarCats.App\current\TaskbarCats.exe";
        var report = HookInstaller.Refresh(h.Dir, NoEnv, NewExe);
        var codex = File.ReadAllText(h.P(".codex", "hooks.json")).Replace(@"\\", @"\");   // JSON-escaped backslashes
        Assert.Contains(NewExe, codex);
        Assert.DoesNotContain(Exe, codex);
        Assert.False(File.Exists(h.P(".cursor", "hooks.json")));   // never a fresh install
        Assert.Single(report);
    }

    [Fact] public void Refresh_with_nothing_installed_writes_nothing()
    {
        using var h = new Home(".codex", ".copilot");
        Assert.Empty(HookInstaller.Refresh(h.Dir, NoEnv, Exe));
        Assert.False(File.Exists(h.P(".codex", "hooks.json")));
        Assert.False(Directory.Exists(h.P(".copilot", "hooks")));
    }
}
