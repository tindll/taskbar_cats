using System.Windows;
using TaskbarCats;
using Xunit;

public class HerdTests
{
    static readonly DateTimeOffset BinnedAt = DateTimeOffset.Parse("2026-10-08T15:00:00Z");

    [Theory]
    [InlineData(SessionStatus.Working, SessionStatus.Idle, true)]      // new question asked
    [InlineData(SessionStatus.Waiting, SessionStatus.Working, true)]   // needs a permission answer
    [InlineData(SessionStatus.Working, SessionStatus.Working, false)]  // binned while busy: stays binned
    [InlineData(SessionStatus.Idle, SessionStatus.Working, false)]     // finished: stays binned
    [InlineData(SessionStatus.Idle, SessionStatus.Idle, false)]
    public void Unbin_on_new_activity(SessionStatus now, SessionStatus previous, bool expected) =>
        Assert.Equal(expected, Herd.ShouldUnbin(now, previous, null, BinnedAt));

    [Fact] public void Unbin_when_the_user_typed_after_binning()
    {
        Assert.True(Herd.ShouldUnbin(SessionStatus.Idle, SessionStatus.Idle, BinnedAt.AddSeconds(5), BinnedAt));
        Assert.False(Herd.ShouldUnbin(SessionStatus.Idle, SessionStatus.Idle, BinnedAt.AddMinutes(-5), BinnedAt));
    }

    [Fact] public void Landing_cat_is_centred_where_it_came_down()
    {
        Assert.Equal(476, Herd.LandX(500, new World(1000, null, [])));
    }

    [Fact] public void Landing_cat_stays_between_the_can_and_the_bin()
    {
        Assert.Equal(56, Herd.LandX(-300, new World(1000, null, [], MinX: 56)));
        Assert.Equal(944 - Cat.Size, Herd.LandX(5000, new World(944, null, [])));
    }

    [Theory]
    [InlineData(0.0, 0, 0, false)] [InlineData(0.1, 3, 3, false)] [InlineData(0.1, 4, 0, true)] [InlineData(0.1, 0, -4, true)]
    [InlineData(0.34, 0, 0, false)] [InlineData(0.35, 0, 0, true)]
    public void A_press_lifts_on_a_small_move_or_a_short_hold(double held, int dx, int dy, bool lift) =>
        Assert.Equal(lift, Herd.ShouldLift(held, dx, dy));

    [Fact] public void The_ball_grab_box_is_bigger_than_any_ball_and_stops_at_the_floor()
    {
        var r = Herd.YarnGrabBox(100);
        Assert.Equal(OverlayWindow.BandHeight, r.Bottom, 6);            // never over the taskbar
        Assert.Equal(100 + Cat.Size / 2, r.Left + r.Width / 2, 6);       // centred on the ball's box
        double Width(string[] f) =>                                      // the ball itself, without its trailing thread (row 15)
            Enumerable.Range(0, 16).Count(x => f.Take(15).Any(row => row[x] != '.')) * Cat.Size / Sprites.Px;
        var widths = Sprites.Yarn.Select(s => Width(s[0])).ToList();
        Assert.True(Tune.YarnGrab >= 2 * widths.Min(), "the small ball needs a bigger grab box");
        Assert.True(Tune.YarnGrab >= 1.3 * widths.Max(), "the big ball needs a bigger grab box");
    }

    [Fact] public void Where_a_cat_and_a_ball_overlap_the_nearer_centre_wins()
    {
        Assert.True(Herd.BallWins(pressX: 170, catX: 100, ballX: 130));    // ball centre 154, cat centre 124
        Assert.False(Herd.BallWins(pressX: 130, catX: 100, ballX: 130));
        Assert.False(Herd.BallWins(pressX: 139, catX: 100, ballX: 130));   // a tie goes to the cat
    }
    [Fact] public void Hidden_project_folders_are_not_shown()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TaskbarCatsTests", Guid.NewGuid().ToString("N"));
        try
        {
            var prefs = new CatPrefs(Path.Combine(dir, "cats.json"));
            prefs.Set(@"C:\Code\Hidden", new CatLook { Hidden = true });
            SessionInfo S(string id, string cwd) => new(id, cwd, id, SessionStatus.Idle, DateTimeOffset.Now);
            var shown = Herd.Shown([S("a", @"c:/code/hidden/"), S("b", @"C:\Code\App")], prefs);
            Assert.Equal(["b"], shown.Select(s => s.SessionId));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact] public void A_carried_sardine_is_put_back_by_a_click_away_Esc_a_right_click_or_waiting()
    {
        Assert.False(Herd.SardineDone(clickedAway: false, cancelKey: false, heldFor: 1));
        Assert.True(Herd.SardineDone(clickedAway: true, cancelKey: false, heldFor: 1));
        Assert.True(Herd.SardineDone(clickedAway: false, cancelKey: true, heldFor: 1));
        Assert.True(Herd.SardineDone(clickedAway: false, cancelKey: false, heldFor: Herd.SardineTimeout + 0.1));
    }

    [Fact] public void Only_claude_sessions_get_the_typed_praise()
    {
        var claude = new SessionInfo("a", @"c:\x\P", "P", SessionStatus.Idle, DateTimeOffset.Now);
        Assert.True(Herd.MayPraise(claude));
        Assert.False(Herd.MayPraise(claude with { Status = SessionStatus.Waiting }));
        Assert.False(Herd.MayPraise(claude with { Tool = AgentTool.Codex }));
        Assert.False(Herd.MayPraise(claude with { Host = "terminal" }));
    }

    [Fact] public void Tooltip_names_the_tool_for_non_claude()
    {
        var s = new SessionInfo("a", @"c:\x\Proj", "Proj", SessionStatus.Idle, DateTimeOffset.Now);
        Assert.Equal("Proj · lazy", Herd.TipFor(s, Trait.Lazy));
        Assert.Equal("Cursor · Proj · lazy", Herd.TipFor(s with { Tool = AgentTool.Cursor }, Trait.Lazy));
    }
}
