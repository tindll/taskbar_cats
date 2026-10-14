using System.Text;
using TaskbarCats;
using Xunit;

/// Shapes trimmed from real 2.1.294 records (structure only, no real text).
public class SignalsTests
{
    static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    static string Ts(double s) => T0.AddSeconds(s).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    static string Human(double s) =>
        $$$"""{"type":"user","isSidechain":false,"message":{"role":"user","content":[{"type":"text","text":"secret prompt"}]},"origin":{"kind":"human"},"timestamp":"{{{Ts(s)}}}"}""";
    static string Use(double s, string name, string id, string model = "claude-opus-5-5", long cacheRead = 1000, string side = "false") =>
        $$$"""{"type":"assistant","isSidechain":{{{side}}},"message":{"id":"m{{{id}}}","model":"{{{model}}}","role":"assistant","stop_reason":"tool_use","usage":{"input_tokens":2,"cache_read_input_tokens":{{{cacheRead}}},"cache_creation_input_tokens":100,"output_tokens":5},"content":[{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{"command":"rm -rf secret"}}]},"timestamp":"{{{Ts(s)}}}"}""";
    static string Result(double s, string id, bool error = false) =>
        $$$"""{"type":"user","message":{"role":"user","content":[{"tool_use_id":"{{{id}}}","type":"tool_result","content":"secret output","is_error":{{{(error ? "true" : "false")}}}}]},"toolUseResult":{"stdout":"secret"},"timestamp":"{{{Ts(s)}}}"}""";
    static string Said(double s, string stop = "end_turn") =>
        $$$"""{"type":"assistant","message":{"id":"mx","model":"claude-sonnet-5-5","role":"assistant","stop_reason":"{{{stop}}}","usage":{"input_tokens":1,"cache_read_input_tokens":5000,"cache_creation_input_tokens":0},"content":[{"type":"text","text":"secret answer"}]},"timestamp":"{{{Ts(s)}}}"}""";
    static string ApiError(double s, string error) =>
        $$$"""{"type":"assistant","message":{"id":"e","model":"<synthetic>","role":"assistant","stop_reason":"stop_sequence","usage":{"input_tokens":0},"content":[{"type":"text","text":"API Error"}]},"isApiErrorMessage":true,"error":"{{{error}}}","apiErrorStatus":429,"timestamp":"{{{Ts(s)}}}"}""";
    static string Compact(double s, long post) =>
        $$$"""{"type":"system","subtype":"compact_boundary","level":"info","parentUuid":null,"compactMetadata":{"trigger":"auto","preTokens":267000,"postTokens":{{{post}}}},"timestamp":"{{{Ts(s)}}}"}""";

    static Signals Feed(params string[] lines)
    {
        var st = new SignalState();
        foreach (var l in lines) st.Feed(Encoding.UTF8.GetBytes(l));
        return st.Snapshot;
    }

    [Fact] public void A_tool_call_is_open_until_its_result_arrives()
    {
        var s = Feed(Human(0), Use(1, "Bash", "t1"));
        Assert.Equal("Bash", s.Tool);
        Assert.Equal(T0.AddSeconds(1), s.ToolAt);
        Assert.Equal(1, s.Open);
        s = Feed(Human(0), Use(1, "Bash", "t1"), Result(3, "t1"));
        Assert.Equal(0, s.Open);
        Assert.Equal(1, s.Results);
        Assert.Equal(0, s.Errors);
        Assert.Equal(T0.AddSeconds(3), s.LastAt);
    }

    [Fact] public void Parallel_calls_close_one_by_one_and_errors_are_counted()
    {
        var s = Feed(Use(1, "Read", "a"), Use(1, "Grep", "b"), Result(2, "a", error: true));
        Assert.Equal(1, s.Open);
        Assert.Equal("Grep", s.Tool);
        Assert.Equal(1, s.Results);
        Assert.Equal(1, s.Errors);
    }

    [Fact] public void Edits_count_edit_and_write_since_the_user_last_typed()
    {
        var s = Feed(Use(1, "Edit", "a"), Use(2, "Write", "b"), Use(3, "Read", "c"));
        Assert.Equal(2, s.Edits);
        s = Feed(Use(1, "Edit", "a"), Human(5), Use(6, "Edit", "b"));
        Assert.Equal(1, s.Edits);
        Assert.Equal(T0.AddSeconds(5), s.LastHuman);
    }

    [Fact] public void Context_is_input_plus_cache_and_model_is_the_newest_reply()
    {
        var s = Feed(Use(1, "Read", "a", cacheRead: 200_000), Said(2));
        Assert.Equal(5001, s.Context);
        Assert.Equal(200_102, s.MaxContext);
        Assert.Equal("claude-sonnet-5-5", s.Model);
        Assert.Equal("end_turn", s.Stop);
    }

    [Fact] public void A_human_message_starts_a_new_turn()
    {
        var s = Feed(Use(1, "Bash", "t1"), Human(2));   // the Bash call was interrupted: its result never comes
        Assert.Null(s.Stop);
        Assert.Equal(0, s.Open);
        Assert.Equal(T0.AddSeconds(2), s.LastAt);        // the thinking clock starts at the prompt
    }

    [Fact] public void Api_errors_are_counted_and_cleared_by_the_next_real_reply()
    {
        var s = Feed(Said(1, "tool_use"), ApiError(2, "rate_limit"));
        Assert.Equal("rate_limit", s.ApiError);
        Assert.Equal(1, s.ApiErrors);
        Assert.Equal("claude-sonnet-5-5", s.Model);      // the synthetic record is not a model
        Assert.Equal("tool_use", s.Stop);
        s = Feed(ApiError(2, "server_error"), Said(9));
        Assert.Null(s.ApiError);
        Assert.Equal(1, s.ApiErrors);
    }

    [Fact] public void Compaction_is_counted_and_shrinks_the_context_at_once()
    {
        var s = Feed(Use(1, "Read", "a", cacheRead: 260_000), Compact(5, 30_000));
        Assert.Equal(1, s.Compactions);
        Assert.Equal(30_000, s.Context);
        Assert.Equal(260_102, s.MaxContext);
    }

    [Fact] public void Sidechain_records_and_garbage_are_ignored()
    {
        var s = Feed(Use(1, "Bash", "a", side: "true"), "not json", """{"type":"assistant","message":""", "[1,2]", "");
        Assert.Null(s.Tool);
        Assert.Equal(0, s.Open);
    }

    [Fact] public void Only_names_counts_times_and_flags_are_kept()
    {
        var strings = typeof(Signals).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Order();
        Assert.Equal(["ApiError", "Model", "Stop", "Tool"], strings);
        var s = Feed(Human(0), Use(1, "Bash", "t1"), Result(2, "t1"), Said(3));
        Assert.DoesNotContain("secret", s.ToString());
    }
}
