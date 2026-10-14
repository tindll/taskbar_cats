using TaskbarCats;
using Xunit;

public class MoodTests
{
    static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    const string Id = "494d4841-0e3a-4fe4-92ab-24b6811922d0";
    const SessionStatus Busy = SessionStatus.Working, Idle = SessionStatus.Idle;

    static DateTimeOffset T(double s) => T0.AddSeconds(s);
    /// A tool call in flight, last record written at `last` seconds.
    static Signals Tool(string name = "Read", double last = 0, int open = 1) =>
        new(Tool: name, ToolAt: T(0), Open: open, Stop: "tool_use", LastAt: T(last));
    static IReadOnlyList<Cue> At(Mood m, Signals? s, double now, SessionStatus st = Busy, int subagents = 0, double since = 0) =>
        m.Update(s, st, T(since), subagents, T(now));
    static Mood New(int seed = 1) => new(Id, new Random(seed));

    [Fact] public void Unknown_signals_mean_todays_chase()
    {
        var m = New();
        Assert.Empty(At(m, null, 0));
        Assert.Equal(Activity.Toy, m.Activity);
        Assert.Equal(Look.Cat, m.Look);
        Assert.Equal(0, m.Belly);
        Assert.Empty(At(m, Tool(), 2));            // first real sight: still no cues
    }

    [Fact] public void Not_busy_means_the_toy()
    {
        var m = New();
        At(m, Tool("Bash"), 0, Idle);
        Assert.Equal(Activity.Toy, m.Activity);
    }

    [Fact] public void The_tool_pose_wins_most_first_picks_and_no_other_pose_shows()
    {
        var picks = Enumerable.Range(0, 200).Select(seed => { var m = New(seed); At(m, Tool("Edit"), 0); return m.Activity; }).ToList();
        Assert.InRange(picks.Count(a => a == Activity.Bat), 100, 200);
        Assert.DoesNotContain(picks, a => a is Activity.Sniff or Activity.Watch);
    }

    [Fact] public void A_new_tool_kind_switches_the_pose_at_once()
    {
        var m = Enumerable.Range(0, 100).Select(New).First(m => { At(m, Tool("Read"), 0); return m.Activity == Activity.Sniff; });
        At(m, Tool("Bash", last: 2), 2);
        Assert.NotEqual(Activity.Sniff, m.Activity);
    }

    [Fact] public void A_pick_is_held_then_never_repeated()
    {
        var m = New();
        At(m, Tool(), 0);
        for (int k = 1; k <= 40; k++)
        {
            var before = m.Activity;
            double t = k * 13;                       // past MaxDwell (12 s)
            At(m, Tool(last: t), t);
            Assert.NotEqual(before, m.Activity);
            var picked = m.Activity;
            At(m, Tool(last: t + 1), t + 1);         // inside MinDwell (6 s)
            Assert.Equal(picked, m.Activity);
        }
    }

    [Fact] public void Silence_is_thinking_the_box_after_20_s_and_nothing_new_before()
    {
        var m = New();
        At(m, Tool(open: 0), 1);
        var first = m.Activity;
        At(m, Tool(open: 0), 6);
        Assert.Equal(first, m.Activity);             // no staring stage: the pick goes on until the box
        At(m, Tool(open: 0), 25);
        Assert.Equal(Activity.Box, m.Activity);
        At(m, Tool(open: 1, last: 0), 300);          // a long tool call is not thinking
        Assert.NotEqual(Activity.Box, m.Activity);
    }

    [Fact] public void Ids_left_open_by_an_interrupt_do_not_block_thinking()
    {
        var m = New();
        At(m, new Signals(Tool: "Bash", Open: 1, Stop: null, LastAt: T(0)), 30);
        Assert.Equal(Activity.Box, m.Activity);
    }

    [Fact] public void Long_runs_pant_then_rest_then_nap()
    {
        List<Activity> Picks(double min) => Enumerable.Range(0, 200).Select(seed =>
        {
            var m = New(seed);
            At(m, Tool(last: min * 60), min * 60);
            return m.Activity;
        }).ToList();
        var tired = Picks(31);
        Assert.Contains(Activity.Rest, tired);
        Assert.DoesNotContain(Activity.Toy, tired);
        Assert.All(Picks(61), a => Assert.Equal(Activity.Nap, a));
    }

    [Fact] public void Rate_limit_naps_and_waits()
    {
        var m = New();
        At(m, new Signals(ApiError: "rate_limit", LastAt: T(0)), 1);
        Assert.Equal(Activity.Limit, m.Activity);
    }

    [Fact] public void Sub_agents_after_the_turn_mean_supervise_with_at_most_three_kittens()
    {
        var m = New();
        At(m, new Signals(Stop: "end_turn", LastAt: T(0)), 1, subagents: 2);
        Assert.Equal(Activity.Supervise, m.Activity);
        Assert.Equal(2, m.Kittens);
        At(m, new Signals(Stop: "end_turn", LastAt: T(0)), 3, subagents: 9);
        Assert.Equal(3, m.Kittens);
    }

    [Fact] public void Cues_come_from_counter_changes_and_are_rate_limited()
    {
        var m = New();
        var s = Tool(last: 0);
        At(m, s, 0);
        s = s with { ToolAt = T(1), LastAt = T(1) };
        Assert.Equal([Cue.Mrrp], At(m, s, 2));
        s = s with { ToolAt = T(3), LastAt = T(3) };
        Assert.Empty(At(m, s, 4));                   // second tool call within 30 s
        s = s with { Results = 1 };
        Assert.Equal([Cue.Catch], At(m, s, 6));
        s = s with { Results = 2, Errors = 1 };
        Assert.Equal([Cue.Slip], At(m, s, 8));       // an error is a slip, not a catch
        s = s with { Results = 3 };
        Assert.Empty(At(m, s, 10));                  // catch again within 20 s
        s = s with { Compactions = 1 };
        Assert.Equal([Cue.Sneeze], At(m, s, 12));
        s = s with { ApiErrors = 1, ApiError = "overloaded_error" };
        Assert.Equal([Cue.Startle], At(m, s, 14));
        s = s with { ApiErrors = 2, ApiError = "rate_limit" };
        Assert.Empty(At(m, s, 16));                  // rate limit: the Limit nap, no startle
    }

    [Fact] public void Finishing_a_long_job_brings_a_reward_a_short_one_does_not()
    {
        var done = new Signals(Stop: "end_turn", LastAt: T(0));
        var m = New();
        At(m, Tool(), 0);
        Assert.Contains(Cue.Reward, At(m, done, 11 * 60, Idle, since: 11 * 60));
        var quick = New();
        At(quick, Tool(), 0);
        Assert.DoesNotContain(Cue.Reward, At(quick, done, 5 * 60, Idle, since: 5 * 60));
    }

    [Fact] public void An_interrupted_turn_startles_a_rate_limited_one_does_not()
    {
        var m = New();
        At(m, Tool(), 0);
        Assert.Equal([Cue.Startle], At(m, Tool(), 30, Idle, since: 30));
        var limited = New();
        At(limited, new Signals(ApiError: "rate_limit", ApiErrors: 1, LastAt: T(0)), 0);
        Assert.Empty(At(limited, new Signals(ApiError: "rate_limit", ApiErrors: 1, LastAt: T(0)), 30, Idle, since: 30));
    }

    [Fact] public void Long_thinking_wonders_once()
    {
        var m = New();
        var s = Tool(open: 0);
        At(m, s, 1);
        Assert.Equal([Cue.Wonder], At(m, s, 46));
        Assert.Empty(At(m, s, 48));
    }

    [Fact] public void Look_belly_and_yarn_follow_the_signals()
    {
        var m = New();
        At(m, new Signals(Model: "claude-haiku-4-5", Context: 200_000, MaxContext: 200_000, Edits: 8, LastAt: T(0)), 1);
        Assert.Equal(Look.Kitten, m.Look);
        Assert.Equal(2, m.Belly);
        Assert.Equal(2, m.Yarn);
    }
}
