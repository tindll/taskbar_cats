using TaskbarCats;
using Xunit;

/// Animation glitches seen on screen: teleports, pops and cats stranded off the strip.
public class CatMotionTests
{
    const double Dt = 1.0 / 30, MaxStep = 10;   // fastest real motion is ~6.5 DIP per 1/30 s tick; a teleport is 20+

    static Cat Landed(string id = "a", double x = 100)
    {
        var c = new Cat(id, x, new Random(1));
        CatTests.Run(c, 0.6);
        c.Click();   // acknowledged: no sleep
        return c;
    }

    static void AssertSmooth(Cat c, double seconds, World w, Action<int>? at = null)
    {
        double x = c.X, lift = c.Lift;
        int i = 0;
        for (double t = 0; t < seconds; t += Dt, i++)
        {
            at?.Invoke(i);
            c.Tick(Dt, w);
            Assert.True(Math.Abs(c.X - x) <= MaxStep, $"tick {i}: X jumped {x:F1} -> {c.X:F1}");
            Assert.True(Math.Abs(c.Lift - lift) <= MaxStep, $"tick {i}: Lift jumped {lift:F1} -> {c.Lift:F1}");
            x = c.X; lift = c.Lift;
        }
    }

    [Fact] public void Status_change_mid_leap_finishes_the_leap_smoothly()
    {
        var c = Landed();
        var w = new World(1000, 190, [c]);
        CatTests.Run(c, 0.5, w);              // crouch done, leap under way
        Assert.Equal(Anim.Leap, c.Anim);
        Assert.True(c.Lift > 0);
        c.Status = SessionStatus.Working;
        AssertSmooth(c, 0.45, w);             // the rest of the leap plus a step or two
        Assert.Equal(0, c.Lift);
        Assert.Equal(Anim.Run, c.Anim);       // landed, then started chasing
        Assert.InRange(c.X + Cat.Size / 2, 180, 200);
    }

    [Fact] public void Despawn_mid_hop_on_continues_from_current_height()
    {
        var c = new Cat("a", 100, new Random(1));
        CatTests.Run(c, 0.25);
        Assert.False(c.IsLive);
        c.Despawn();
        AssertSmooth(c, 0.6, new World(1000, null, [c]));
        Assert.True(c.IsGone);
    }

    [Fact] public void Despawn_mid_leap_continues_from_current_height()
    {
        var c = Landed();
        var w = new World(1000, 190, [c]);
        CatTests.Run(c, 0.55, w);
        Assert.True(c.Lift > 10);
        c.Despawn();
        AssertSmooth(c, 0.6, w);
        Assert.True(c.IsGone);
    }

    [Fact] public void Spawn_and_hop_off_are_smooth()
    {
        var c = new Cat("a", 100, new Random(1));
        var w = new World(1000, null, [c]);
        AssertSmooth(c, 0.7, w);
        c.Despawn();
        AssertSmooth(c, 0.6, w);
        Assert.True(c.IsGone);
    }

    [Fact] public void Shrinking_strip_pulls_a_sitting_cat_back_on_screen()
    {
        var c = Landed(x: 900);
        c.Status = SessionStatus.Working;     // working cats never walk, so only the clamp can move it
        CatTests.Run(c, 0.1, new World(300, null, [c]));
        Assert.InRange(c.X, 0, 300 - Cat.Size);
    }

    [Fact] public void MinX_is_a_wall()
    {
        var c = Landed(x: 10);
        var w = new World(1000, null, [c], MinX: 56);
        CatTests.Run(c, 300, w, k => Assert.InRange(k.X, 56, 1000 - Cat.Size));
    }
}
