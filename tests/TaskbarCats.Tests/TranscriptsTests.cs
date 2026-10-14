using TaskbarCats;
using Xunit;

public class TranscriptsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "tc-proj-" + Guid.NewGuid());
    readonly string _id = Guid.NewGuid().ToString();
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T15:00:00Z");

    public TranscriptsTests() => Directory.CreateDirectory(Path.Combine(_root, "c--Users-x-feed-digest"));
    public void Dispose() => Directory.Delete(_root, true);

    string PathOf => Path.Combine(_root, "c--Users-x-feed-digest", _id + ".jsonl");
    static string Ts(double minutesAgo) => Now.AddMinutes(-minutesAgo).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    // Shapes trimmed from real 2.1.294 records.
    static string Human(double ago, string text = "hi") =>
        $$"""{"parentUuid":null,"isSidechain":false,"type":"user","message":{"role":"user","content":[{"type":"text","text":"{{text}}"}]},"timestamp":"{{Ts(ago)}}","origin":{"kind":"human"},"promptSource":"sdk","userType":"external","entrypoint":"claude-vscode"}""";
    static string ToolResult(double ago) =>
        $$"""{"type":"user","message":{"role":"user","content":[{"tool_use_id":"t1","type":"tool_result","content":"ok"}]},"toolUseResult":{"stdout":"ok"},"timestamp":"{{Ts(ago)}}"}""";
    static string Peer(double ago) =>
        $$"""{"type":"user","isMeta":true,"message":{"role":"user","content":"hand-back"},"origin":{"kind":"peer"},"timestamp":"{{Ts(ago)}}"}""";
    static string ModelEcho(double ago) =>
        $$"""{"type":"user","message":{"role":"user","content":"<command-name>/model</command-name><command-args>haiku</command-args>"},"timestamp":"{{Ts(ago)}}"}""";
    static string Assistant(double ago) =>
        $$"""{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"done"}]},"timestamp":"{{Ts(ago)}}"}""";
    const string LastPrompt = """{"type":"last-prompt","lastPrompt":"hi","sessionId":"x"}""";

    void Write(params string[] lines) => File.WriteAllText(PathOf, string.Join("\n", lines) + "\n");

    DateTimeOffset? Last(Transcripts t) => t.LastHumanAt(_id, Now.AddHours(-1));

    [Fact] public void Finds_the_newest_human_message_past_machine_records()
    {
        Write(Human(50, "old"), Human(30), Assistant(29), ToolResult(10), Peer(5), ModelEcho(4), Assistant(3), LastPrompt);
        Assert.Equal(Now.AddMinutes(-30), Last(new Transcripts(_root)));
    }

    [Fact] public void Human_message_older_than_an_hour_does_not_count()
    {
        Write(Human(61), Assistant(20), ToolResult(10));
        Assert.Null(Last(new Transcripts(_root)));
    }

    [Fact] public void Half_written_last_line_is_skipped()
    {
        File.WriteAllText(PathOf, Human(20) + "\n" + """{"type":"user","origin":{"kind":"hu""");
        Assert.Equal(Now.AddMinutes(-20), Last(new Transcripts(_root)));
    }

    [Fact] public void Missing_transcript_or_non_guid_id_is_null()
    {
        var t = new Transcripts(_root);
        Assert.Null(t.LastHumanAt(Guid.NewGuid().ToString(), Now.AddHours(-1)));
        Assert.Null(t.LastHumanAt(@"..\..\evil", Now.AddHours(-1)));
        Assert.Null(new Transcripts(Path.Combine(_root, "nope")).LastHumanAt(_id, Now.AddHours(-1)));
    }

    [Fact] public void Tiny_chunks_and_long_lines_parse_the_same()
    {
        Write(Human(40, new string('x', 5000)), Assistant(30), ToolResult(1));
        Assert.Equal(Now.AddMinutes(-40), Transcripts.Scan(PathOf, Now.AddHours(-1), chunk: 7));
        Assert.Equal(Now.AddMinutes(-40), Transcripts.Scan(PathOf, Now.AddHours(-1), chunk: 4096));
    }

    [Fact] public void Crlf_line_endings_work()
    {
        File.WriteAllText(PathOf, Human(15) + "\r\n" + Assistant(5) + "\r\n");
        Assert.Equal(Now.AddMinutes(-15), Last(new Transcripts(_root)));
    }

    [Fact] public void A_new_message_is_seen_on_the_next_call()
    {
        var t = new Transcripts(_root);
        Write(Human(61), Assistant(50));
        Assert.Null(Last(t));
        File.AppendAllText(PathOf, Human(1) + "\n");
        Assert.Equal(Now.AddMinutes(-1), Last(t));
    }

    [Fact] public void Cached_answer_expires_as_the_hour_moves_on()
    {
        var t = new Transcripts(_root);
        Write(Human(50));
        Assert.NotNull(t.LastHumanAt(_id, Now.AddHours(-1)));
        Assert.Null(t.LastHumanAt(_id, Now.AddMinutes(-40)));   // 15 minutes later, same file
    }
    static string AiTitle(string t) => $$"""{"type":"ai-title","aiTitle":"{{t}}","sessionId":"x"}""";

    [Fact] public void Title_is_the_newest_ai_title_record()
    {
        Write(AiTitle("Old name"), Human(30, "the ai-title record said aiTitle"), AiTitle("Taskbar cat walker project"), Assistant(3),
            """{"type":"ai-title","aiTitle":"half writ""");
        Assert.Equal("Taskbar cat walker project", new Transcripts(_root).Title(_id));
    }

    [Fact] public void Title_is_null_without_a_record_a_file_or_a_guid()
    {
        Write(Human(30), Assistant(3));
        var t = new Transcripts(_root);
        Assert.Null(t.Title(_id));
        Assert.Null(t.Title(Guid.NewGuid().ToString()));
        Assert.Null(t.Title(@"..\..\secret"));
    }

    static string Use(double ago, string name, string id) =>
        $$$"""{"type":"assistant","message":{"id":"m","model":"claude-opus-5-5","stop_reason":"tool_use","usage":{"input_tokens":1},"content":[{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{}}]},"timestamp":"{{{Ts(ago)}}}"}""";

    [Fact] public void Signals_follow_the_file_as_it_grows()
    {
        var t = new Transcripts(_root);
        Write(Human(5), Use(4, "Read", "a"));
        Assert.Equal("Read", t.Signals(_id)!.Tool);
        File.AppendAllText(PathOf, ToolResult(3).Replace("t1", "a") + "\n" + Use(2, "Bash", "b") + "\n");
        var s = t.Signals(_id)!;
        Assert.Equal("Bash", s.Tool);
        Assert.Equal(1, s.Open);
        Assert.Equal(1, s.Results);
    }

    [Fact] public void Unchanged_length_reads_nothing()
    {
        var t = new Transcripts(_root);
        var line = Use(4, "Read", "a");
        Write(line);
        Assert.Equal("Read", t.Signals(_id)!.Tool);
        Write(line.Replace("\"Read\"", "\"Grep\""));   // same length, different bytes: a re-read would see Grep
        Assert.Equal("Read", t.Signals(_id)!.Tool);
    }

    [Fact] public void A_half_written_line_is_finished_by_the_next_append()
    {
        var t = new Transcripts(_root);
        var line = Use(4, "Edit", "a");
        File.WriteAllText(PathOf, line[..40]);
        Assert.Null(t.Signals(_id)!.Tool);
        File.AppendAllText(PathOf, line[40..] + "\n");
        Assert.Equal("Edit", t.Signals(_id)!.Tool);
        Assert.Equal(1, t.Signals(_id)!.Edits);
    }

    [Fact] public void A_big_file_is_read_from_its_tail_only()
    {
        var lines = Enumerable.Range(0, 40).Select(i => Use(40 - i, "Read", "r" + i)).ToArray();
        Write(lines);
        var t = new Transcripts(_root) { TailCap = lines[0].Length * 5 };
        var s = t.Signals(_id)!;
        Assert.InRange(s.Open, 3, 5);          // the cut first line is dropped, older lines are never read
        Assert.Equal("Read", s.Tool);
    }

    [Fact] public void A_rewritten_shorter_file_starts_over()
    {
        var t = new Transcripts(_root);
        Write(Use(9, "Read", "a"), Use(8, "Read", "b"), Use(7, "Read", "c"));
        Assert.Equal(3, t.Signals(_id)!.Open);
        Write(Use(1, "Bash", "z"));
        var s = t.Signals(_id)!;
        Assert.Equal(1, s.Open);
        Assert.Equal("Bash", s.Tool);
    }

    [Fact] public void Signals_are_null_without_a_file_or_a_guid()
    {
        var t = new Transcripts(_root);
        Assert.Null(t.Signals(Guid.NewGuid().ToString()));
        Assert.Null(t.Signals(@"..\..\evil"));
    }

    [Fact] public void Running_subagents_are_the_recently_written_agent_files()
    {
        Write(Human(5));
        var dir = Path.Combine(_root, "c--Users-x-feed-digest", _id, "subagents");
        Directory.CreateDirectory(dir);
        foreach (var (name, ago) in new[] { ("agent-a.jsonl", 10), ("agent-b.jsonl", 60), ("agent-c.jsonl", 300), ("agent-a.meta.json", 1) })
        {
            var p = Path.Combine(dir, name);
            File.WriteAllText(p, "{}");
            File.SetLastWriteTimeUtc(p, Now.UtcDateTime.AddSeconds(-ago));
        }
        var t = new Transcripts(_root);
        Assert.Equal(2, t.RunningSubagents(_id, Now));
        Assert.Equal(0, t.RunningSubagents(Guid.NewGuid().ToString(), Now));
        Assert.Equal(0, t.RunningSubagents("x", Now));
    }
}
