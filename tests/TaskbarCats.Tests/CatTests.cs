using TaskbarCats;
using Xunit;

public class CatTests
{
    const double Dt = 1.0 / 30;

    internal static void Run(Cat c, double seconds, World? w = null, Action<Cat>? each = null)
    {
        var world = w ?? new World(1000, null, [c]);
        for (double t = 0; t < seconds; t += Dt) { c.Tick(Dt, world); each?.Invoke(c); }
    }

    static Cat Landed(SessionStatus s = SessionStatus.Idle)
    {
        var c = new Cat("a", 100, new Random(1)) { Status = s };
        Run(c, 0.6);
        return c;
    }

    [Fact] public void Spawn_hops_on_then_lands_meowing()
    {
        var c = new Cat("a", 100, new Random(1));
        Assert.Equal(Anim.Hop, c.Anim);
        Assert.True(c.Lift < 0);
        Run(c, 0.7);
        Assert.True(c.IsLive);
        Assert.Equal(0, c.Lift);
        Assert.Equal(Bubble.Meow, c.Bubble);
    }

    [Fact] public void Meow_bubble_blinks_on_and_off()
    {
        var c = Landed();
        var seen = new HashSet<Bubble>();
        Run(c, 4, each: x => seen.Add(x.Bubble));
        Assert.Contains(Bubble.Meow, seen);
        Assert.Contains(Bubble.None, seen);
    }

    [Fact] public void Sleeps_after_120s_of_unclicked_meow()
    {
        var c = Landed();
        Run(c, 119);
        Assert.False(c.IsAsleep);
        Run(c, 2);
        Assert.True(c.IsAsleep);
        Assert.Equal(Bubble.Zzz, c.Bubble);
        Assert.Contains(c.Anim, Tune.SleepPoses);
    }

    [Fact] public void Click_sleeping_wakes_to_meow_without_focus()
    {
        var c = Landed();
        Run(c, 121);
        Assert.Equal(ClickResult.None, c.Click());
        Run(c, 0.1);
        Assert.False(c.IsAsleep);
        Assert.Equal(Bubble.Meow, c.Bubble);
        Run(c, 100);
        Assert.False(c.IsAsleep);   // timer was reset
    }

    [Fact] public void Click_awake_idle_focuses_and_stops_meow()
    {
        var c = Landed();
        Assert.Equal(ClickResult.Focus, c.Click());
        Run(c, 5);
        Assert.True(c.Acknowledged);
        Assert.Equal(Bubble.None, c.Bubble);
        Run(c, 200);
        Assert.False(c.IsAsleep);   // acknowledged cats do not fall asleep
    }

    [Fact] public void Acknowledge_stops_meow_without_a_click_and_only_when_idle()
    {
        var c = Landed();
        c.Status = SessionStatus.Working;
        c.Acknowledge();
        Assert.False(c.Acknowledged);   // busy: nothing to acknowledge
        c.Status = SessionStatus.Idle;
        c.Acknowledge();                // the user is already looking at the window
        Run(c, 5);
        Assert.True(c.Acknowledged);
        Assert.Equal(Bubble.None, c.Bubble);
    }

    [Fact] public void Finishing_a_task_restarts_meow()
    {
        var c = Landed();
        c.Click();
        c.Status = SessionStatus.Working;
        Run(c, 1);
        c.Status = SessionStatus.Idle;
        Run(c, 0.1);
        Assert.False(c.Acknowledged);
        Assert.Equal(Bubble.Meow, c.Bubble);
    }

    [Fact] public void Working_chases_a_mouse_quietly()
    {
        var c = Landed(SessionStatus.Working);
        var anims = new HashSet<Anim>();
        Run(c, 25, each: k => { anims.Add(k.Anim); Assert.Equal(Bubble.None, k.Bubble); Assert.True(k.HasMouse); });
        Assert.Contains(Anim.Run, anims);
        Assert.Contains(Anim.Leap, anims);
        Assert.Equal(ClickResult.Focus, c.Click());
    }

    [Fact] public void Waiting_raises_paw_with_bang()
    {
        var c = Landed(SessionStatus.Waiting);
        Run(c, 1);
        Assert.Equal(Anim.PawUp, c.Anim);
        Assert.Equal(Bubble.Bang, c.Bubble);
        Assert.Equal(ClickResult.Focus, c.Click());
    }

    [Fact] public void Despawn_hops_off_then_gone()
    {
        var c = Landed();
        c.Despawn();
        Run(c, 0.3);
        Assert.False(c.IsGone);
        Assert.Equal(ClickResult.None, c.Click());
        Run(c, 0.3);
        Assert.True(c.IsGone);
    }

    [Fact] public void Idle_wandering_stays_on_screen_and_uses_every_behaviour()
    {
        var c = Landed();
        c.Click();   // acknowledged: no sleep, so it wanders for the whole run
        var anims = new HashSet<Anim>();
        Run(c, 600, each: k =>
        {
            anims.Add(k.Anim);
            Assert.InRange(k.X, 0, 1000 - Cat.Size);
            Assert.InRange(k.Frame, 0, Cat.Timing[k.Anim].Frames - 1);
        });
        foreach (var a in new[] { Anim.Walk, Anim.Sit, Anim.Lick, Anim.Wash, Anim.Stretch, Anim.Yawn })
            Assert.Contains(a, anims);
    }

    [Fact] public void Sit_blinks_and_flicks_tail()
    {
        var c = Landed();
        c.Click();
        var sitFrames = new HashSet<int>();
        Run(c, 600, each: k => { if (k.Anim == Anim.Sit) sitFrames.Add(k.Frame); });
        Assert.Equal([0, 1, 2, 3], sitFrames.Order());
    }

    [Fact] public void Stretching_is_rare()
    {
        var c = Landed();
        c.Click();   // acknowledged: it wanders the whole hour
        int stretches = 0;
        var last = c.Anim;
        Run(c, 3600, each: k => { if (k.Anim == Anim.Stretch && last != Anim.Stretch) stretches++; last = k.Anim; });
        Assert.InRange(stretches, 3, 45);   // ~900 idle picks: 10 % was ~90, 3 % is ~27
    }
    [Fact] public void A_quiet_cat_meows_without_blinking()
    {
        var c = Landed();
        c.Quiet = true;
        var seen = new HashSet<Bubble>();
        Run(c, 4, each: x => seen.Add(x.Bubble));
        Assert.Equal([Bubble.Meow], seen);
    }
}
