using TaskbarCats;
using Xunit;

public class CatChaseTests
{
    // A settled cat on a 1000 DIP strip; an idle one is acknowledged (no MEOW), so only the chase moves it.
    static Cat Settled(SessionStatus s = SessionStatus.Idle, double x = 300)
    {
        var c = new Cat("a", x, new Random(1)) { Status = s };
        CatTests.Run(c, 0.6);
        if (s == SessionStatus.Idle) c.Click();
        return c;
    }

    static double CentreOf(Cat c) => c.X + Cat.Size / 2;

    [Fact] public void Runs_along_the_strip_to_the_lure_at_the_play_run_speed()
    {
        var c = Settled();
        c.Lure = new Lure(800, 10);
        double x0 = c.X;
        CatTests.Run(c, 1.0);
        Assert.Equal(Anim.Run, c.Anim);
        Assert.True(c.FacingRight);
        Assert.InRange(c.X - x0, Tune.PlayRun * 0.9, Tune.PlayRun * 1.1);
    }

    [Fact] public void Runs_left_too_and_waits_under_a_lure_that_is_too_high_to_jump_for()
    {
        var c = Settled(x: 600);
        c.Lure = new Lure(150, Tune.JumpReach + 20);
        CatTests.Run(c, 8);
        Assert.InRange(CentreOf(c), 150 - 8, 150 + 8);
        Assert.False(c.FacingRight);
        Assert.Equal(Anim.LookUp, c.Anim);        // waits underneath, head up
        Assert.Equal(0, c.Lift);
    }

    [Fact] public void Jumps_again_and_again_when_the_lure_is_overhead_in_reach()
    {
        var c = Settled();
        c.Lure = new Lure(CentreOf(c), 50);
        int jumps = 0; bool wasUp = false; double max = 0;
        CatTests.Run(c, 4, each: x => { bool up = x.Lift > 0; if (up && !wasUp) jumps++; wasUp = up; max = Math.Max(max, x.Lift); });
        Assert.True(jumps >= 3, $"{jumps} jumps");
        Assert.InRange(max, Teaser.Peak(50) * 0.95, Teaser.Peak(50) + 0.01);
    }

    [Fact] public void Jump_height_is_scaled_to_the_lure_and_capped()
    {
        double Highest(double height)
        {
            var c = Settled();
            c.Lure = new Lure(CentreOf(c), height);
            double max = 0;
            CatTests.Run(c, 3, each: x => max = Math.Max(max, x.Lift));
            return max;
        }
        Assert.True(Highest(30) < Highest(Tune.JumpReach) - 10);
        Assert.InRange(Highest(Tune.JumpReach), Tune.JumpMax * 0.95, Tune.JumpMax + 0.01);
    }

    [Fact] public void A_jump_uses_the_leap_pose_then_a_short_look_up_rest_and_never_teleports()
    {
        var c = Settled();
        c.Lure = new Lure(CentreOf(c), 60);
        var seen = new HashSet<Anim>();
        double last = 0;
        CatTests.Run(c, 3, each: x => { seen.Add(x.Anim); Assert.True(Math.Abs(x.Lift - last) <= 14, $"lift {last} -> {x.Lift}"); last = x.Lift; });
        Assert.Contains(Anim.Leap, seen);
        Assert.Contains(Anim.LookUp, seen);
    }

    [Fact] public void Too_far_above_it_just_sits_and_watches_with_its_head_up()
    {
        var c = Settled();
        double x0 = c.X;
        c.Lure = new Lure(900, Tune.PlayReach + 50);
        CatTests.Run(c, 3);
        Assert.Equal(x0, c.X);
        Assert.Equal(Anim.LookUp, c.Anim);
        Assert.True(c.FacingRight);               // looking at it
        Assert.Equal(0, c.Lift);
    }

    [Fact] public void Now_and_then_it_catches_a_lure_at_strip_level_then_lets_go()
    {
        var c = Settled();
        c.Lure = new Lure(CentreOf(c), 0);
        bool caught = false, jumped = false, letGo = false;
        CatTests.Run(c, 120, each: x =>
        {
            if (x.Anim == Anim.Bat && x.Frame == 2) caught = true;
            else if (caught && x.Anim != Anim.Bat) letGo = true;
            if (x.Lift > 0) jumped = true;
        });
        Assert.True(caught, "never caught it");
        Assert.True(jumped, "never jumped");
        Assert.True(letGo, "never let go");
    }

    [Fact] public void A_lure_above_strip_level_is_only_ever_missed()
    {
        var c = Settled();
        c.Lure = new Lure(CentreOf(c), Tune.CatchHeight + 20);
        bool caught = false;
        CatTests.Run(c, 60, each: x => caught |= x.Anim == Anim.Bat);
        Assert.False(caught);
    }

    [Fact] public void Back_to_normal_when_the_lure_goes()
    {
        var c = Settled();
        c.Lure = new Lure(CentreOf(c), 60);
        CatTests.Run(c, 1.2);
        c.Lure = null;
        CatTests.Run(c, 0.1);
        Assert.Equal(0, c.Lift);
        Assert.NotEqual(Anim.Leap, c.Anim);
        Assert.NotEqual(Anim.LookUp, c.Anim);
    }

    [Fact] public void Stay_here_cats_ignore_it()
    {
        var c = Settled();
        c.Pinned = true;
        double x0 = c.X;
        c.Lure = new Lure(900, 10);
        CatTests.Run(c, 3);
        Assert.Equal(x0, c.X);
        Assert.False(c.Playable);
    }

    [Fact] public void Hopping_held_and_binned_cats_are_not_playable()
    {
        var hopping = new Cat("a", 100, new Random(1));
        Assert.False(hopping.Playable);
        var c = Settled();
        Assert.True(c.Playable);
        c.Grab();
        Assert.False(c.Playable);
        c.Bin();
        Assert.False(c.Playable);
    }

    [Fact] public void A_cat_fetching_a_thrown_ball_is_not_playable_until_it_has_it()
    {
        var c = Settled(SessionStatus.Working);
        c.DropYarn(500, 0);
        Assert.False(c.Playable);
    }

    [Fact] public void The_right_wall_holds_for_a_lure_beyond_it()
    {
        var c = Settled();
        c.Lure = new Lure(5000, 10);
        CatTests.Run(c, 20, new World(700, null, [c]));
        Assert.InRange(c.X, 700 - Cat.Size - Tune.PlayStop, 700 - Cat.Size);   // stops within PlayStop of the wall
    }

    [Fact] public void The_left_wall_holds_for_a_lure_beyond_it()
    {
        var c = Settled(x: 400);
        c.Lure = new Lure(-300, 10);
        CatTests.Run(c, 20, new World(1000, null, [c], MinX: 56));
        Assert.InRange(c.X, 56, 56 + Tune.PlayStop);
    }

    [Fact] public void A_finished_cats_meow_still_blinks_over_its_head_while_it_chases()
    {
        var c = Settled(SessionStatus.Working);
        c.Status = SessionStatus.Idle;            // finished: a fresh, unacknowledged MEOW
        c.Lure = new Lure(900, 10);
        var seen = new HashSet<Bubble>();
        bool ran = false;
        CatTests.Run(c, 5, each: x => { seen.Add(x.Bubble); ran |= x.Anim == Anim.Run; });
        Assert.Contains(Bubble.Meow, seen);
        Assert.Contains(Bubble.None, seen);
        Assert.True(ran);
    }

    [Fact] public void A_waiting_cat_keeps_its_bang_while_it_chases()
    {
        var c = Settled(SessionStatus.Waiting);
        c.Lure = new Lure(900, 10);
        CatTests.Run(c, 2);
        Assert.Equal(Bubble.Bang, c.Bubble);
        Assert.Equal(Anim.Run, c.Anim);
    }

    [Fact] public void A_sleeping_cat_wakes_up_to_chase_and_a_click_still_focuses()
    {
        var c = Settled(SessionStatus.Working);
        c.Status = SessionStatus.Idle;
        CatTests.Run(c, 130);                     // unclicked MEOW: asleep
        Assert.True(c.IsAsleep);
        c.Lure = new Lure(900, 10);
        CatTests.Run(c, 1);
        Assert.False(c.IsAsleep);
        Assert.Equal(Anim.Run, c.Anim);
        Assert.Equal(ClickResult.Focus, c.Click());
    }

    [Fact] public void Chasing_replaces_the_busy_cats_toys_and_they_come_back_after()
    {
        var c = Settled(SessionStatus.Working);
        CatTests.Run(c, 1);
        Assert.True(c.HasMouse);
        c.Lure = new Lure(900, 10);
        CatTests.Run(c, 1);
        Assert.False(c.HasMouse);
        Assert.Null(c.Yarn);
        Assert.Null(c.Bird);
        Assert.Equal(Prop.None, c.Prop);
        c.Lure = null;
        CatTests.Run(c, 1);
        Assert.True(c.HasMouse);
    }

    [Fact] public void A_status_change_while_chasing_does_not_stop_the_chase()
    {
        var c = Settled(SessionStatus.Working);
        c.Lure = new Lure(900, 10);
        CatTests.Run(c, 0.5);
        c.Status = SessionStatus.Waiting;
        CatTests.Run(c, 0.5);
        Assert.Equal(Anim.Run, c.Anim);
        Assert.Equal(Bubble.Bang, c.Bubble);
    }
}
