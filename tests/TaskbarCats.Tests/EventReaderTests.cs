using System.Text.Json;
using TaskbarCats;
using Xunit;

public class EventReaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tc-ev-" + Guid.NewGuid());
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1791472090917);
    static readonly Func<int, string?, bool> Alive = (_, _) => true, Dead = (_, _) => false;
    public EventReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    string Put(string status, TimeSpan ago, int? pid = 7, string tool = "codex", string id = "s1")
    {
        var r = new HookRecord(tool, id, @"C:\src\Proj", status, "PostToolUse", Now - ago, "shell", Now - ago, 3, "gpt-5", pid, "1", "terminal");
        var path = Path.Combine(_dir, $"{tool}-{id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(r, Hook.Json));
        return path;
    }

    [Fact] public void Working_session_becomes_a_cat()
    {
        Put("working", TimeSpan.FromSeconds(5));
        var s = Assert.Single(EventReader.ReadActive(_dir, Now, Alive));
        Assert.Equal(("codex:s1", "Proj", SessionStatus.Working, AgentTool.Codex, 7, "terminal"), (s.SessionId, s.Name, s.Status, s.Tool, s.Pid, s.Host));
        Assert.Equal(("shell", 3, "gpt-5"), (s.Live!.Tool, s.Live.Results, s.Live.Model));
    }

    [Fact] public void Waiting_maps() { Put("waiting", TimeSpan.Zero); Assert.Equal(SessionStatus.Waiting, Assert.Single(EventReader.ReadActive(_dir, Now, Alive)).Status); }

    [Fact] public void Idle_older_than_an_hour_hidden_but_kept()
    {
        var f = Put("idle", TimeSpan.FromHours(2));
        Assert.Empty(EventReader.ReadActive(_dir, Now, Alive));
        Assert.True(File.Exists(f));
    }

    [Fact] public void Expired_file_deleted()
    {
        var f = Put("working", TimeSpan.FromHours(25));
        Assert.Empty(EventReader.ReadActive(_dir, Now, Alive));
        Assert.False(File.Exists(f));
    }

    [Fact] public void Dead_pid_file_deleted()
    {
        var f = Put("working", TimeSpan.Zero);
        Assert.Empty(EventReader.ReadActive(_dir, Now, Dead));
        Assert.False(File.Exists(f));
    }

    [Fact] public void Stale_working_without_pid_is_idle()
    {
        Put("working", TimeSpan.FromMinutes(11), pid: null);
        Assert.Equal(SessionStatus.Idle, Assert.Single(EventReader.ReadActive(_dir, Now, Dead)).Status);
    }

    [Fact] public void Garbage_and_missing_dir_are_harmless()
    {
        File.WriteAllText(Path.Combine(_dir, "x.json"), "{\"tool\":");
        Put("working", TimeSpan.Zero, id: "ok");
        Assert.Single(EventReader.ReadActive(_dir, Now, Alive));
        Assert.Empty(EventReader.ReadActive(Path.Combine(_dir, "nope"), Now, Alive));
    }
}
