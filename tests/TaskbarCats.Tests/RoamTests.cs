using TaskbarCats;
using Xunit;

public class RoamTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    static readonly Screen Main = new(@"\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1032), true, 1.0);
    static readonly Screen Right = new(@"\.\DISPLAY2", R(1920, -78, 3840, 1002), R(1920, -78, 3840, 954), false, 1.0);
    static readonly Screen Left = new(@"\.\DISPLAY3", R(-2560, 0, 0, 1440), R(-2560, 0, 0, 1392), false, 1.0);

    [Fact] public void The_monitor_beside_is_the_nearest_on_that_side()
    {
        List<Screen> all = [Main, Right, Left];
        Assert.Equal(Right, Herd.Beside(all, Main, 1));
        Assert.Equal(Left, Herd.Beside(all, Main, -1));
        Assert.Equal(Main, Herd.Beside(all, Right, -1));
        Assert.Null(Herd.Beside(all, Right, 1));
        Assert.Null(Herd.Beside([Main], Main, 1));
    }

    [Fact] public void A_single_monitor_never_roams() =>
        Assert.Equal((0, 0.0), Herd.RoamOffer("a", [Main], [("a", Main.Key, @"C:\p")], null));

    [Fact] public void A_lone_cat_may_wander_to_either_side_at_the_base_chance()
    {
        Assert.Equal((1, Tune.RoamChance), Herd.RoamOffer("a", [Main, Right], [("a", Main.Key, @"C:\p")], null));
        Assert.Equal((-1, Tune.RoamChance), Herd.RoamOffer("a", [Main, Right], [("a", Right.Key, @"C:\p")], null));
    }

    [Fact] public void Cats_of_one_project_prefer_to_spread_out()
    {
        (string, string, string)[] piled = [("a", Main.Key, @"C:\p"), ("b", Main.Key, @"C:\P\"), ("c", Right.Key, @"C:\other")];
        Assert.Equal((1, Tune.RoamChance * Tune.RoamSpread), Herd.RoamOffer("a", [Main, Right], piled, null));
        (string, string, string)[] spread = [("a", Main.Key, @"C:\p"), ("b", Right.Key, @"C:\p")];
        Assert.Equal((1, Tune.RoamChance * Tune.RoamPile), Herd.RoamOffer("a", [Main, Right], spread, null));
    }

    [Fact] public void Nobody_wanders_onto_a_fullscreen_monitor() =>
        Assert.Equal((0, 0.0), Herd.RoamOffer("a", [Main, Right], [("a", Main.Key, @"C:\p")], Right.Key));

    static Cat Idle(double x, string id = "a")
    {
        var c = new Cat(id, x, new Random(1));
        CatTests.Run(c, 0.6);
        c.Click();   // seen: it stays up (no nap) for the long runs
        return c;
    }

    static void RunUntil(Cat c, Func<Cat, bool> done, double seconds, World? w = null)
    {
        var world = w ?? new World(1000, null, [c]);
        for (double t = 0; t < seconds && !done(c); t += 1.0 / 30) c.Tick(1.0 / 30, world);
        Assert.True(done(c));
    }

    [Fact] public void Neither_the_end_margin_nor_a_cat_in_the_way_stops_a_crossing()
    {
        var other = Idle(940, "b");
        var c = Idle(700);
        c.RoamDir = 1; c.RoamChance = 1;
        var w = new World(1000, null, [c, other]);
        RunUntil(c, x => x.IsRoaming, 10, w);
        Assert.False(c.StartTag(other));   // busy leaving
        RunUntil(c, x => x.RoamingTo == 1, 60, w);
        Assert.Equal(1000 - Cat.Size, c.X, 0.5);
    }

    [Fact] public void A_cat_hissed_at_on_its_way_out_still_walks_in_on_arrival()
    {
        var c = Idle(500);
        c.RoamDir = 1; c.RoamChance = 1;
        RunUntil(c, x => x.IsRoaming, 10);
        var other = Idle(c.X + 150, "b");
        other.StartWalk(false, 20);             // walking straight at it: a face-off
        var w = new World(1000, null, [c, other]);
        bool faced = false;
        for (double t = 0; t < 30 && c.RoamingTo == 0; t += 1.0 / 30)
        {
            c.Tick(1.0 / 30, w); other.Tick(1.0 / 30, w);
            faced |= other.Anim is Anim.Hiss or Anim.Sit;
        }
        Assert.True(faced);
        Assert.Equal(1, c.RoamingTo);
        c.Arrive(0);                            // in at the left end of the next strip, facing right
        CatTests.Run(c, 4);
        Assert.True(c.X > Tune.EdgeMargin);
    }

    [Fact] public void Without_a_way_to_go_a_cat_never_heads_off()
    {
        var c = Idle(500);
        CatTests.Run(c, 300, each: x => { Assert.False(x.IsRoaming); Assert.Equal(0, x.RoamingTo); });
    }

    [Fact] public void Arriving_it_walks_in_clear_of_the_end_and_stays_a_while()
    {
        var c = Idle(300);
        c.RoamDir = -1; c.RoamChance = 1;
        RunUntil(c, x => x.RoamingTo == -1, 60);
        c.Arrive(1000 - Cat.Size);              // in at the right end of the strip to the left, still facing left
        Assert.False(c.IsRoaming);
        CatTests.Run(c, 4);
        Assert.True(c.X <= 1000 - Cat.Size - Tune.EdgeMargin);
        CatTests.Run(c, Tune.RoamStay - 10, each: x => Assert.False(x.IsRoaming));
        CatTests.Run(c, 60);
        Assert.True(c.IsRoaming);               // then it may go again
    }

    [Fact] public void Picking_it_up_work_starting_or_stay_here_calls_the_trip_off()
    {
        foreach (var stop in new Action<Cat>[] { x => x.Grab(), x => x.Status = SessionStatus.Working, x => x.Pinned = true })
        {
            var c = Idle(500);
            c.RoamDir = 1; c.RoamChance = 1;
            RunUntil(c, x => x.IsRoaming, 10);
            stop(c);
            CatTests.Run(c, 1);
            Assert.False(c.IsRoaming);
            Assert.Equal(0, c.RoamingTo);
        }
    }

    [Fact] public void A_cancelled_trip_turns_round_and_waits_before_trying_again()
    {
        var c = Idle(500);
        c.RoamDir = 1; c.RoamChance = 1;
        RunUntil(c, x => x.RoamingTo == 1, 60);
        c.CancelRoam();
        Assert.False(c.FacingRight);
        Assert.Equal(0, c.RoamingTo);
        CatTests.Run(c, 10, each: x => Assert.False(x.IsRoaming));
        Assert.True(c.X < 1000 - Cat.Size - Tune.EdgeMargin);
    }

    [Fact] public void A_cat_waiting_at_the_end_that_falls_asleep_is_not_carried_across()
    {
        var c = new Cat("a", 800, new Random(1));
        CatTests.Run(c, 0.6);                   // not clicked: it naps after SleepAfter
        c.RoamDir = 1; c.RoamChance = 1;
        RunUntil(c, x => x.RoamingTo == 1, 60);
        CatTests.Run(c, Cat.SleepAfter);
        Assert.True(c.IsAsleep);
        Assert.Equal(0, c.RoamingTo);
    }
}
