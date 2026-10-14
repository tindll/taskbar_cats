using TaskbarCats;
using Xunit;

public class YarnChaseTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    static readonly Screen Main = new(@"\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1032), true, 1.0);
    static readonly Screen Right = new(@"\.\DISPLAY2", R(1920, -78, 3840, 1002), R(1920, -78, 3840, 954), false, 1.0);
    static readonly Screen Left = new(@"\.\DISPLAY3", R(-2560, 0, 0, 1440), R(-2560, 0, 0, 1392), false, 1.0);

    // A cat with a ball in the user's hand: whatever its toy, it has just had the ball taken.
    static Cat WithBall(Activity a, double x = 100, Toy toy = Toy.Yarn)
    {
        var c = new Cat("a", x, new Random(1)) { Status = SessionStatus.Working, Toy = toy, Activity = a };
        CatTests.Run(c, 0.6);
        Assert.True(c.TakeYarn());
        return c;
    }

    [Fact] public void The_run_speed_is_well_above_the_walk()
    {
        Assert.InRange(Tune.YarnRun / Cat.WalkSpeed, 2.5, 6);
        Assert.True(Tune.YarnRun > Cat.ChaseSpeed);
    }

    [Theory]
    [InlineData(Activity.Toy)] [InlineData(Activity.Bat)] [InlineData(Activity.Sniff)]
    [InlineData(Activity.Box)] [InlineData(Activity.Rest)]
    [InlineData(Activity.Nap)] [InlineData(Activity.Watch)] [InlineData(Activity.Butterfly)] [InlineData(Activity.Supervise)]
    public void A_thrown_ball_is_chased_whatever_the_mood(Activity a)
    {
        var c = WithBall(a);
        c.Activity = Activity.Watch;               // the mood moves on while the ball is in the air
        c.Activity = a;
        c.DropYarn(800, 0);
        bool ran = false, got = false;
        CatTests.Run(c, 8, each: x => { ran |= x.Anim == Anim.Run; got |= Math.Abs(x.X - 800) <= Tune.YarnReach + 5; });
        Assert.True(ran);
        Assert.True(got);
    }

    [Fact] public void A_mouse_cat_keeps_the_ball_until_it_has_caught_up_with_it()
    {
        var c = WithBall(Activity.Bat, toy: Toy.Mouse);   // an edit brought the ball out
        c.Activity = Activity.Toy;                         // and the mood moved on
        c.DropYarn(800, 0);
        CatTests.Run(c, 2, each: x => Assert.NotNull(x.Yarn));
        Assert.True(c.X < 700);
        CatTests.Run(c, 8 + Tune.YarnPlay);
        Assert.Null(c.Yarn);                       // caught and played with: back to its own toy
        Assert.True(c.HasMouse);
    }

    [Theory] [InlineData(Activity.Watch, Toy.Yarn)] [InlineData(Activity.Rest, Toy.Mouse)] [InlineData(Activity.Box, Toy.Bird)]
    public void The_cat_plays_with_the_ball_it_caught_then_goes_back_to_its_mood(Activity a, Toy toy)
    {
        var c = WithBall(Activity.Bat, x: 100, toy: toy); c.Activity = a;
        c.DropYarn(300, 0);
        CatTests.Run(c, 2);                        // arrives after about 1.5 s
        bool bat = false, moved = false;
        double x0 = c.Yarn!.X;
        CatTests.Run(c, Tune.YarnPlay - 3, each: x => { bat |= x.Anim == Anim.Bat; moved |= Math.Abs(x.Yarn!.X - x0) > 20; });
        Assert.True(bat);
        Assert.True(moved);
        CatTests.Run(c, 3 + 1);                    // past the play time
        if (a != Activity.Watch || toy != Toy.Yarn) Assert.Null(c.Yarn);
        else Assert.NotNull(c.Yarn);               // a yarn cat keeps its own ball
    }

    [Fact] public void A_second_throw_while_playing_restarts_the_chase_and_the_play()
    {
        var c = WithBall(Activity.Bat, toy: Toy.Mouse); c.Activity = Activity.Watch;
        c.DropYarn(300, 0);
        CatTests.Run(c, 5);                        // playing since about 1.5 s
        Assert.True(c.TakeYarn());
        c.DropYarn(900, 0);
        double x0 = c.X;
        CatTests.Run(c, 1);
        double run = Tune.FetchRun(900 - x0);
        Assert.InRange(c.X - x0, run * 0.9, run * 1.1);   // running again, at the fetch speed for that throw
        CatTests.Run(c, 6);                        // arrived at about 5 s, plays on
        Assert.NotNull(c.Yarn);
    }

    [Fact] public void Playing_ends_cleanly_when_the_cat_is_picked_up_the_ball_is_taken_or_work_stops()
    {
        var c = WithBall(Activity.Bat, toy: Toy.Mouse); c.Activity = Activity.Watch;
        c.DropYarn(300, 0);
        CatTests.Run(c, 4);
        Assert.True(c.Grab());
        CatTests.Run(c, 1);
        Assert.NotEqual(Anim.Bat, c.Anim);
        var d = WithBall(Activity.Bat, toy: Toy.Mouse); d.Activity = Activity.Watch;
        d.DropYarn(300, 0);
        CatTests.Run(d, 4);
        Assert.True(d.TakeYarn());
        CatTests.Run(d, 1);
        Assert.Null(d.Yarn);
        var e = WithBall(Activity.Bat, toy: Toy.Mouse); e.Activity = Activity.Watch;
        e.DropYarn(300, 0);
        CatTests.Run(e, 4);
        e.Status = SessionStatus.Idle;
        CatTests.Run(e, 1);
        Assert.Null(e.Yarn);
    }

    [Fact] public void After_crossing_the_cat_plays_with_the_ball_on_the_other_strip()
    {
        var c = WithBall(Activity.Bat, toy: Toy.Mouse); c.Activity = Activity.Box;
        c.DropYarnAcross(1);
        CatTests.Run(c, 10);
        c.Cross(48, 300);
        bool bat = false;
        CatTests.Run(c, 6, each: x => bat |= x.Anim == Anim.Bat);
        Assert.True(bat);
    }

    [Fact] public void The_cat_runs_at_the_fetch_speed_for_the_distance()
    {
        var c = WithBall(Activity.Watch);
        c.DropYarn(900, 0);
        double x0 = c.X;
        CatTests.Run(c, 1);
        double run = Tune.FetchRun(900 - x0);
        Assert.InRange(c.X - x0, run * 0.9, run * 1.1);
    }

    [Fact] public void A_ball_that_rolls_off_is_followed_until_it_stops()
    {
        var c = WithBall(Activity.Box);
        c.DropYarn(300, 600);                      // rolls on at the capped speed
        var ball = c.Yarn!;
        bool caught = false;
        CatTests.Run(c, 10, each: x => caught |= !ball.Rolling && Math.Abs(x.X - ball.X) <= Tune.YarnReach + 1);
        Assert.True(caught);                       // it stopped and the cat was at it (then it plays on)
    }

    [Fact] public void A_stay_here_cat_does_not_chase()
    {
        var c = WithBall(Activity.Watch);
        c.Pinned = true;
        c.DropYarn(800, 0);
        double x0 = c.X;
        CatTests.Run(c, 3);
        Assert.Equal(x0, c.X);
    }

    [Fact] public void A_ball_across_makes_the_cat_run_to_the_edge_and_ask_to_cross()
    {
        var c = WithBall(Activity.Watch);
        Assert.True(c.DropYarnAcross(1));
        Assert.Null(c.Yarn);
        Assert.Equal(0, c.CrossingTo);
        CatTests.Run(c, 10);
        Assert.Equal(1000 - Cat.Size, c.X);
        Assert.Equal(1, c.CrossingTo);
        Assert.Null(c.Yarn);
    }

    [Fact] public void Crossing_puts_the_cat_at_the_near_edge_with_the_ball_ahead_and_it_goes_on_chasing()
    {
        var c = WithBall(Activity.Box);
        c.DropYarnAcross(1);
        CatTests.Run(c, 10);
        c.Cross(48, 700);
        Assert.Equal(48, c.X);
        Assert.Equal(0, c.CrossingTo);
        Assert.Equal(700, c.Yarn!.X);
        bool got = false;
        CatTests.Run(c, 10, each: x => got |= Math.Abs(x.X - 700) <= Tune.YarnReach + 5);
        Assert.True(got);
    }

    [Fact] public void A_leftward_ball_is_run_after_towards_the_left_edge()
    {
        var c = WithBall(Activity.Watch, x: 500);
        c.DropYarnAcross(-1);
        CatTests.Run(c, 10, new World(1000, null, [c], MinX: 56));
        Assert.Equal(56, c.X);
        Assert.Equal(-1, c.CrossingTo);
    }

    [Fact] public void Across_is_refused_for_a_pinned_or_idle_cat_so_the_ball_comes_back_to_it()
    {
        var pinned = WithBall(Activity.Toy);
        pinned.Pinned = true;
        Assert.False(pinned.DropYarnAcross(1));
        var idle = WithBall(Activity.Toy);
        idle.Status = SessionStatus.Idle;
        Assert.False(idle.DropYarnAcross(1));
    }

    [Fact] public void Picking_the_cat_up_or_stopping_work_cancels_the_trip()
    {
        var c = WithBall(Activity.Watch);
        c.DropYarnAcross(1);
        CatTests.Run(c, 1);
        Assert.True(c.Grab());
        Assert.Equal(0, c.FetchDir);
        var d = WithBall(Activity.Watch);
        d.DropYarnAcross(1);
        d.Status = SessionStatus.Idle;
        Assert.Equal(0, d.FetchDir);
    }

    [Fact] public void Crossing_direction_follows_the_monitors_side_by_side()
    {
        Assert.Equal(1, Herd.CrossDir(Main, Right));
        Assert.Equal(-1, Herd.CrossDir(Right, Main));
        Assert.Equal(-1, Herd.CrossDir(Main, Left));
        Assert.Equal(1, Herd.CrossDir(Left, Main));
    }

    [Fact] public void A_ball_that_lands_on_the_other_strip_stays_clear_of_the_can_and_the_bin()
    {
        var withCan = new World(1000, null, [], MinX: 56);
        Assert.Equal(56, Herd.LandX(10, withCan));                       // on the can: just right of it
        Assert.Equal(1000 - Cat.Size, Herd.LandX(990, withCan));         // on the bin: just left of it
    }

    // v4.3: a ball parked on another monitor's strip next to the can could not be picked up again.
    [Fact] public void A_ball_parked_on_another_strip_can_be_picked_up_and_thrown_again()
    {
        var c = WithBall(Activity.Watch);
        Assert.True(c.DropYarnAcross(1));
        CatTests.Run(c, 1);
        Assert.True(c.TakeYarn());                 // the user grabs the parked ball
        Assert.Equal(0, c.FetchDir);
        CatTests.Run(c, 2);
        Assert.Null(c.Yarn);                       // no second ball while it is in the hand
        c.DropYarn(400, 0);                        // thrown down again
        Assert.NotNull(c.Yarn);
    }

    [Fact] public void The_cat_always_reaches_the_edge_and_asks_to_cross_whatever_the_float_rounding()
    {
        foreach (double x in new[] { 0.3, 17.7, 100, 123.456, 333.333, 499.9, 641.1, 777.77, 901.01 })
        {
            var c = WithBall(Activity.Watch, x: x);
            c.DropYarnAcross(1);
            bool asked = false;
            CatTests.Run(c, 15, each: k => asked |= k.CrossingTo == 1);
            Assert.True(asked, $"from {x}: never asked to cross");
        }
    }

    [Fact] public void A_cat_within_half_a_dip_of_the_edge_counts_as_there()
    {
        var c = WithBall(Activity.Watch, x: 1000 - Cat.Size - 0.2);
        c.DropYarnAcross(1);
        CatTests.Run(c, 3);
        Assert.Equal(1, c.CrossingTo);
    }

    [Fact] public void A_parked_ball_nobody_is_coming_for_is_orphaned_after_the_timeout()
    {
        Assert.False(Herd.YarnOrphaned(5, 5 + Tune.YarnOrphan - 0.1));
        Assert.True(Herd.YarnOrphaned(5, 5 + Tune.YarnOrphan));
    }

    [Fact] public void A_cancelled_trip_gives_the_cat_a_fresh_ball_on_its_own_strip()
    {
        var c = WithBall(Activity.Toy);
        c.DropYarnAcross(1);
        c.CancelFetch();
        CatTests.Run(c, 1);
        Assert.NotNull(c.Yarn);
        Assert.Equal(0, c.FetchDir);
    }

    [Fact] public void A_ball_never_lands_past_the_strip_walls()
    {
        double right = 1920;
        var w = new World(right, null, []);
        foreach (double x in new[] { -50.0, 100, right - 10, right, right + 100, 5000 })
            Assert.True(Herd.LandX(x, w) + Cat.Size <= right + 1e-9, $"x={x}");
    }

    [Fact] public void A_ball_rolls_on_to_rest_when_the_mood_moves_on()
    {
        var c = new Cat("a", 100, new Random(1)) { Status = SessionStatus.Working, Toy = Toy.Yarn, Activity = Activity.Toy };
        CatTests.Run(c, 0.6);
        c.Yarn!.Push(Tune.YarnBat);
        c.Activity = Activity.Watch;               // a web fetch starts mid-roll
        double x0 = c.Yarn.X;
        CatTests.Run(c, 0.2);
        Assert.True(Math.Abs(c.Yarn.X - x0) > 10, "the ball froze mid-roll");
        CatTests.Run(c, 5);
        Assert.False(c.Yarn.Rolling);
    }

    [Fact] public void A_ball_against_a_strip_end_is_batted_out_of_the_corner()
    {
        var c = WithBall(Activity.Toy, x: 700);
        c.DropYarn(1000, 0);                       // lands against the right end (952)
        double? first = null;
        CatTests.Run(c, 6, each: k => { if (first is null && k.Anim == Anim.Bat) first = k.Yarn!.Vx; });
        Assert.NotNull(first);
        Assert.True(first < 0, $"batted into the wall at {first}");
    }

    [Fact] public void A_far_throw_is_run_after_faster_but_capped()
    {
        Assert.Equal(Tune.YarnRun, Tune.FetchRun(0));
        Assert.True(Tune.FetchRun(300) > Tune.FetchRun(100));
        Assert.Equal(Tune.YarnSprint, Tune.FetchRun(Tune.YarnFar));
        Assert.Equal(Tune.YarnSprint, Tune.FetchRun(5000));
        Assert.Equal(Tune.FetchRun(400), Tune.FetchRun(-400));
    }

    [Fact] public void The_sprint_is_fast_but_every_run_frame_still_shows_at_30_fps()
    {
        Assert.InRange(Tune.YarnSprint / Tune.YarnRun, 1.5, 2.5);
        Assert.True(Cat.Timing[Anim.Run].Ms / Tune.RunRateMax >= 1000.0 / 30);
    }

    [Fact] public void A_far_throw_is_a_faster_run_with_quicker_strides()
    {
        double Covered(double ballX)
        {
            var c = WithBall(Activity.Toy, x: 100);
            c.DropYarn(ballX, 0);
            double x0 = c.X;
            CatTests.Run(c, 1);
            return c.X - x0;
        }
        Assert.True(Covered(950) > Covered(400) * 1.3);   // 850 DIP away: sprint; 300 DIP: a bit over YarnRun

        var far = WithBall(Activity.Toy, x: 100);
        far.DropYarn(950, 0);
        int changes = 0, last = far.Frame;
        CatTests.Run(far, 1, each: k => { if (k.Frame != last) { changes++; last = k.Frame; } });
        Assert.True(changes > 18);   // plain Run: ~14 frame changes a second; x RunRateMax: ~22
    }

    [Fact] public void A_far_cross_screen_throw_is_a_sprint_too()
    {
        var near = WithBall(Activity.Toy, x: 800);
        Assert.True(near.DropYarnAcross(1, beyond: 0));
        var far = WithBall(Activity.Toy, x: 800);
        Assert.True(far.DropYarnAcross(1, beyond: 1500));
        CatTests.Run(near, 0.5);
        CatTests.Run(far, 0.5);
        Assert.True(far.X > near.X + 20);
    }

    [Fact] public void A_ball_rolling_at_the_cat_is_batted_back_not_run_through()
    {
        var c = WithBall(Activity.Toy, x: 500);
        c.DropYarn(800, -400);                     // rolls back at the cat, well above YarnBat / 2
        bool passed = false, batted = false;
        CatTests.Run(c, 3, each: k =>
        {
            batted |= k.Anim == Anim.Bat;
            if (!batted && k.Yarn!.X < k.X - Tune.YarnReach) passed = true;
        });
        Assert.True(batted);
        Assert.False(passed, "the ball rolled through the cat");
    }
}
