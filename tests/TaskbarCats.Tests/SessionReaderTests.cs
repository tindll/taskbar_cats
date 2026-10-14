using System.Diagnostics;
using System.Globalization;
using TaskbarCats;
using Xunit;

public class SessionReaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tc-" + Guid.NewGuid());
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1791472090917);
    static readonly Func<int, string?, bool> Alive = (_, _) => true;

    public SessionReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    // Shape copied from a real C:\Users\me\.claude\sessions\56116.json
    void Write(int pid, string status, long updatedAt, string entry = "claude-vscode", string? id = null) =>
        File.WriteAllText(Path.Combine(_dir, $"{pid}.json"),
            $$"""{"pid":{{pid}},"sessionId":"{{id ?? Guid.NewGuid().ToString()}}","cwd":"c:\\Users\\x\\DemoGame","startedAt":1791471684075,"procStart":"134359452825552089","version":"2.1.294","kind":"interactive","entrypoint":"{{entry}}","name":"demogame-08","status":"{{status}}","updatedAt":{{updatedAt}},"statusUpdatedAt":{{updatedAt}}}""");

    long HoursAgo(double h) => Now.AddHours(-h).ToUnixTimeMilliseconds();

    [Fact] public void Busy_is_working()
    {
        Write(1, "busy", HoursAgo(0));
        var s = Assert.Single(SessionReader.ReadActive(_dir, Now, Alive));
        Assert.Equal(SessionStatus.Working, s.Status);
        Assert.Equal(@"c:\Users\x\DemoGame", s.Cwd);
    }

    [Theory]
    [InlineData("shell", SessionStatus.Working)]
    [InlineData("waiting", SessionStatus.Waiting)]
    [InlineData("idle", SessionStatus.Idle)]
    [InlineData("something-new", SessionStatus.Idle)]
    public void Status_mapping(string raw, SessionStatus expected)
    {
        Write(1, raw, HoursAgo(0));
        Assert.Equal(expected, Assert.Single(SessionReader.ReadActive(_dir, Now, Alive)).Status);
    }

    [Fact] public void Idle_inside_1h_active_outside_not()
    {
        Write(1, "idle", HoursAgo(0.99));
        Write(2, "idle", HoursAgo(1.01));
        Assert.Single(SessionReader.ReadActive(_dir, Now, Alive));
    }

    [Fact] public void Idle_session_needs_a_human_message_within_the_hour()
    {
        var typed = Guid.NewGuid().ToString();
        Write(1, "idle", HoursAgo(0.1), id: typed);
        Write(2, "idle", HoursAgo(0.1));                  // a long agent run that just ended
        var seen = new List<DateTimeOffset>();
        var list = SessionReader.ReadActive(_dir, Now, Alive, (id, notBefore) =>
        {
            seen.Add(notBefore);
            return id == typed ? Now.AddMinutes(-20) : null;
        });
        var s = Assert.Single(list);
        Assert.Equal(typed, s.SessionId);
        Assert.Equal(Now.AddMinutes(-20), s.LastHumanAt);
        Assert.All(seen, nb => Assert.Equal(Now.AddHours(-1), nb));
    }

    [Fact] public void Busy_or_stale_sessions_never_read_the_transcript()
    {
        Write(1, "busy", HoursAgo(5));
        Write(2, "waiting", HoursAgo(0));
        Write(3, "idle", HoursAgo(2));
        var list = SessionReader.ReadActive(_dir, Now, Alive, (_, _) => throw new Exception("should not be called"));
        Assert.Equal(2, list.Count);
    }

    [Fact] public void Busy_older_than_8h_still_active()
    {
        Write(1, "busy", HoursAgo(30));
        Assert.Single(SessionReader.ReadActive(_dir, Now, Alive));
    }

    [Fact] public void Dead_pid_skipped()
    {
        Write(1, "busy", HoursAgo(0));
        Assert.Empty(SessionReader.ReadActive(_dir, Now, (_, _) => false));
    }

    [Fact] public void Cli_session_is_a_terminal_cat()
    {
        Write(1, "busy", HoursAgo(0), entry: "cli");
        var s = Assert.Single(SessionReader.ReadActive(_dir, Now, Alive));
        Assert.Equal(("terminal", 1), (s.Host, s.Pid));
    }

    [Fact] public void Sdk_and_other_entrypoints_skipped()
    {
        Write(1, "busy", HoursAgo(0), entry: "sdk-ts");
        Assert.Empty(SessionReader.ReadActive(_dir, Now, Alive));
    }

    [Fact] public void Vscode_session_keeps_default_host()
    {
        Write(1, "busy", HoursAgo(0));
        Assert.Null(Assert.Single(SessionReader.ReadActive(_dir, Now, Alive)).Host);
    }

    [Fact] public void Malformed_file_skipped_others_kept()
    {
        Write(1, "busy", HoursAgo(0));
        File.WriteAllText(Path.Combine(_dir, "2.json"), "{\"pid\":2,\"sessionId\":\"ab");
        File.WriteAllText(Path.Combine(_dir, "3.json"), "{\"pid\":3,\"updatedAt\":99999999999999999}");
        Assert.Single(SessionReader.ReadActive(_dir, Now, Alive));
    }

    [Fact] public void Missing_dir_is_empty() =>
        Assert.Empty(SessionReader.ReadActive(Path.Combine(_dir, "nope"), Now, Alive));

    [Fact] public void IsAlive_matches_FILETIME_procStart()
    {
        using var me = Process.GetCurrentProcess();
        var ft = me.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
        Assert.True(SessionReader.IsAlive(me.Id, ft));
    }

    [Fact] public void IsAlive_rejects_wrong_procStart() =>
        Assert.False(SessionReader.IsAlive(Environment.ProcessId, "1"));

    [Fact] public void IsAlive_rejects_missing_pid() =>
        Assert.False(SessionReader.IsAlive(int.MaxValue - 7, null));
}

public class RosterTests
{
    static SessionInfo S(string id) => new(id, @"c:\x", id, SessionStatus.Idle, DateTimeOffset.Now);

    [Fact] public void Added_once()
    {
        var r = new Roster();
        Assert.Single(r.Update([S("a")]).Added);
        Assert.Empty(r.Update([S("a")]).Added);
    }

    [Fact] public void Removed_only_after_two_misses()
    {
        var r = new Roster();
        r.Update([S("a")]);
        Assert.Empty(r.Update([]).Removed);
        Assert.Equal(["a"], r.Update([]).Removed);
    }

    [Fact] public void Reappearing_resets_misses()
    {
        var r = new Roster();
        r.Update([S("a")]);
        r.Update([]);
        r.Update([S("a")]);
        Assert.Empty(r.Update([]).Removed);
    }
    [Fact] public void A_dropped_session_is_added_again_when_seen()
    {
        var r = new Roster();
        r.Update([S("a")]);
        r.Drop("a");
        Assert.Single(r.Update([S("a")]).Added);
    }
}
