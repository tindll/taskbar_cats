using System.Windows;
using System.Windows.Controls;
using TaskbarCats;
using Xunit;

public class HoverCardTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
    static SessionInfo S(SessionStatus st = SessionStatus.Working, AgentTool tool = AgentTool.Claude, string cwd = @"C:\Code\App") =>
        new("id", cwd, "n", st, Now.AddMinutes(-3), Tool: tool);
    static string[] Lines(CardInfo c) => c.Lines.Split('\n');

    [Fact] public void Title_is_name_and_folder_and_claude_has_no_dot()
    {
        var c = HoverCard.For(S(), null, "Bob", TimeSpan.Zero, 0, Now);
        Assert.Equal("Bob · App", c.Title);
        Assert.Null(c.Dot);
        Assert.Equal("App", HoverCard.For(S(), null, null, TimeSpan.Zero, 0, Now).Title);
    }

    [Fact] public void Other_tools_get_their_label_and_badge_colour()
    {
        var c = HoverCard.For(S(tool: AgentTool.Codex), null, null, TimeSpan.Zero, 0, Now);
        Assert.Equal("App · Codex", c.Title);
        Assert.Equal(Tune.ToolColor(AgentTool.Codex), c.Dot);
    }

    [Fact] public void Only_the_folder_name_is_shown()
    {
        var c = HoverCard.For(S(cwd: @"C:\Secret\Deep\App"), new Signals(Model: "m"), null, TimeSpan.Zero, 0, Now);
        Assert.DoesNotContain("Secret", c.Title + c.Lines + c.Warning);
    }

    [Fact] public void Model_line_first_when_known_and_clipped()
    {
        Assert.Equal("claude-opus-4", Lines(HoverCard.For(S(), new Signals(Model: "claude-opus-4"), null, TimeSpan.Zero, 0, Now))[0]);
        var longId = new string('x', 40);
        Assert.Equal(new string('x', 31) + "…", Lines(HoverCard.For(S(), new Signals(Model: longId), null, TimeSpan.Zero, 0, Now))[0]);
        Assert.Equal("Working", Lines(HoverCard.For(S(), null, null, TimeSpan.Zero, 0, Now))[0]);
    }

    [Fact] public void Claude_working_on_an_open_tool_shows_how_long()
    {
        var sig = new Signals(Tool: "Bash", ToolAt: Now.AddSeconds(-65), Open: 1);
        Assert.Contains("Working: Bash 1:05 on it", Lines(HoverCard.For(S(), sig, null, TimeSpan.Zero, 0, Now)));
    }

    [Fact] public void Claude_working_with_no_open_tool_is_thinking()
    {
        var sig = new Signals(Tool: "Bash", ToolAt: Now.AddSeconds(-65), Open: 0);
        Assert.Contains("Working: thinking", Lines(HoverCard.For(S(), sig, null, TimeSpan.Zero, 0, Now)));
    }

    [Fact] public void Hook_cat_working_shows_its_last_tool()
    {
        var sig = new Signals(Tool: "Edit", ToolAt: Now.AddSeconds(-12));
        Assert.Contains("Working · last: Edit 0:12 ago", Lines(HoverCard.For(S(tool: AgentTool.Cursor), sig, null, TimeSpan.Zero, 0, Now)));
        Assert.Contains("Working", Lines(HoverCard.For(S(tool: AgentTool.Cursor), new Signals(), null, TimeSpan.Zero, 0, Now)));
    }

    [Fact] public void Waiting_counts_from_the_status_change()
    {
        Assert.Contains("Waiting for you: 3 min", Lines(HoverCard.For(S(SessionStatus.Waiting), null, null, TimeSpan.Zero, 0, Now)));
    }

    [Fact] public void Idle_says_when_the_user_last_typed()
    {
        var sig = new Signals(LastHuman: Now.AddMinutes(-12));
        Assert.Contains("Idle: you last typed 12 min ago", Lines(HoverCard.For(S(SessionStatus.Idle), sig, null, TimeSpan.Zero, 0, Now)));
        var registryOnly = S(SessionStatus.Idle) with { LastHumanAt = Now.AddMinutes(-2) };
        Assert.Contains("Idle: you last typed 2 min ago", Lines(HoverCard.For(registryOnly, null, null, TimeSpan.Zero, 0, Now)));
        Assert.Equal(["Idle"], Lines(HoverCard.For(S(SessionStatus.Idle), null, null, TimeSpan.Zero, 0, Now)));
    }

    [Fact] public void Busy_line_has_run_and_nonzero_counts()
    {
        var sig = new Signals(Edits: 3, Errors: 1);
        Assert.Contains("Busy 5 min · 3 edits · 1 error", Lines(HoverCard.For(S(), sig, null, TimeSpan.FromMinutes(5), 0, Now)));
        Assert.Contains("Busy <1 min", Lines(HoverCard.For(S(), new Signals(), null, TimeSpan.FromSeconds(20), 0, Now)));
        Assert.Contains("1 edit", Lines(HoverCard.For(S(SessionStatus.Idle), new Signals(Edits: 1), null, TimeSpan.Zero, 0, Now)));
        Assert.DoesNotContain(Lines(HoverCard.For(S(SessionStatus.Idle), new Signals(), null, TimeSpan.Zero, 0, Now)), l => l.StartsWith("Busy"));
    }

    [Fact] public void Hook_cats_never_show_edits_or_errors()
    {
        var c = HoverCard.For(S(tool: AgentTool.Gemini), new Signals(Edits: 4, Errors: 2), null, TimeSpan.FromMinutes(2), 0, Now);
        Assert.Contains("Busy 2 min", Lines(c));
        Assert.DoesNotContain("edit", c.Lines);
    }

    [Theory]
    [InlineData(0, null)] [InlineData(1, "1 sub-agent running")] [InlineData(3, "3 sub-agents running")]
    public void Sub_agent_line_only_when_some_run(int n, string? line)
    {
        var lines = Lines(HoverCard.For(S(), null, null, TimeSpan.Zero, n, Now));
        if (line is null) Assert.DoesNotContain(lines, l => l.Contains("sub-agent")); else Assert.Contains(line, lines);
    }

    [Fact] public void Context_bar_against_the_window()
    {
        Assert.Contains("Context [###-------] 30%", Lines(HoverCard.For(S(), new Signals(Context: 80_100, MaxContext: 80_100), null, TimeSpan.Zero, 0, Now)));
        Assert.Contains("Context [#####-----] 50%", Lines(HoverCard.For(S(), new Signals(Context: 500_000, MaxContext: 500_000), null, TimeSpan.Zero, 0, Now)));
        Assert.Contains("Context [##########] 100%",Lines(HoverCard.For(S(), new Signals(Context: 999_999, MaxContext: 280_000), null, TimeSpan.Zero, 0, Now)));
        Assert.DoesNotContain("Context", HoverCard.For(S(), new Signals(), null, TimeSpan.Zero, 0, Now).Lines);
    }

    [Fact] public void Api_trouble_is_a_warning()
    {
        Assert.Equal("! Rate limited: waiting to retry", HoverCard.For(S(), new Signals(ApiError: "rate_limit"), null, TimeSpan.Zero, 0, Now).Warning);
        Assert.Equal("! API error: overloaded", HoverCard.For(S(), new Signals(ApiError: "overloaded"), null, TimeSpan.Zero, 0, Now).Warning);
        Assert.Null(HoverCard.For(S(), new Signals(), null, TimeSpan.Zero, 0, Now).Warning);
    }

    [Fact] public void Same_inputs_give_equal_cards() =>   // the window redraws only on change
        Assert.Equal(HoverCard.For(S(), new Signals(Model: "m"), "Bob", TimeSpan.FromMinutes(1), 2, Now),
                     HoverCard.For(S(), new Signals(Model: "m"), "Bob", TimeSpan.FromMinutes(1), 2, Now));

    [Theory]
    [InlineData(0, "<1 min")] [InlineData(59, "<1 min")] [InlineData(60, "1 min")] [InlineData(3599, "59 min")]
    [InlineData(3600, "1 h 0 min")] [InlineData(7500, "2 h 5 min")] [InlineData(-30, "<1 min")]
    public void Mins_format(int seconds, string text) => Assert.Equal(text, HoverCard.Mins(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, "0:00")] [InlineData(65, "1:05")] [InlineData(3599, "59:59")] [InlineData(3600, "1:00:00")] [InlineData(-5, "0:00")]
    public void Clock_format(int seconds, string text) => Assert.Equal(text, HoverCard.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(true, 0.5, false, false, true)]
    [InlineData(true, 0.3, false, false, false)]   // not rested long enough
    [InlineData(false, 5, false, false, false)]    // switched off: the tooltip instead
    [InlineData(true, 5, true, false, false)]      // teaser wand
    [InlineData(true, 5, false, true, false)]      // a cat menu is open
    public void Card_shows_only_when_rested_and_nothing_else_is_going_on(bool on, double rested, bool play, bool menu, bool shown) =>
        Assert.Equal(shown, HoverCard.Shows(on, rested, play, menu));

    [Fact] public void Card_sits_centred_above_the_anchor_inside_the_monitor()
    {
        var mon = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        Assert.Equal((400, 900), HoverCard.Place(500, 1000, 200, 100, mon));
        Assert.Equal((0, 900), HoverCard.Place(30, 1000, 200, 100, mon));       // left edge
        Assert.Equal((1720, 900), HoverCard.Place(1900, 1000, 200, 100, mon));  // right edge
        Assert.Equal((400, 0), HoverCard.Place(500, 50, 200, 100, mon));        // top edge
        var left = new Native.RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
        Assert.Equal((-1920, 900), HoverCard.Place(-1900, 1000, 200, 100, left)); // a monitor left of the primary
    }

    static void Sta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw new Exception("failed on the STA thread", err);
    }

    [Fact] public void Card_window_never_activates() => Sta(() =>
    {
        var w = new CardWindow();
        Assert.False(w.ShowActivated);
        Assert.False(w.Focusable);
        Assert.True(w.Topmost);
        Assert.False(w.ShowInTaskbar);
        Assert.False(w.IsVisible);
    });

    [Fact] public void Fill_draws_title_lines_warning_and_footer() => Sta(() =>
    {
        var p = new StackPanel();
        CardWindow.Fill(p, new CardInfo("App · Codex", 0xFF10A37F, "gpt-5\nWorking", "! Rate limited: waiting to retry"));
        Assert.Equal(4, p.Children.Count);
        var title = (StackPanel)p.Children[0];
        Assert.Equal(2, title.Children.Count);                       // dot + name
        Assert.Equal("App · Codex", ((TextBlock)title.Children[1]).Text);
        Assert.Equal("gpt-5\nWorking", ((TextBlock)p.Children[1]).Text);
        Assert.Equal(HoverCard.Footer, ((TextBlock)p.Children[3]).Text);
    });

    [Fact] public void Fill_without_dot_or_warning() => Sta(() =>
    {
        var p = new StackPanel();
        CardWindow.Fill(p, new CardInfo("App", null, "Idle", null));
        Assert.Equal(3, p.Children.Count);
        Assert.Single(((StackPanel)p.Children[0]).Children);
    });
}
