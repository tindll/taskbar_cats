using TaskbarCats;
using Xunit;

/// Grab, drop, stay-here, bin and sardine: the Cat side (no WPF).
public class CatHandlingTests
{
    static Cat Landed(SessionStatus s = SessionStatus.Idle, double x = 300)
    {
        var c = new Cat("a", x, new Random(1)) { Status = s };
        CatTests.Run(c, 0.6);
        return c;
    }

    [Fact] public void Grabbed_cat_dangles_silently_and_cannot_be_clicked()
    {
        var c = Landed();
        Assert.True(c.Grab());
        CatTests.Run(c, 1);
        Assert.True(c.IsHeld);
        Assert.Equal(Anim.Dangle, c.Anim);
        Assert.Equal(Bubble.None, c.Bubble);
        Assert.Equal(ClickResult.None, c.Click());
    }

    [Fact] public void Grab_refused_while_hopping_off()
    {
        var c = Landed();
        c.Despawn();
        Assert.False(c.Grab());
    }

    [Fact] public void Thrown_cat_lands_on_its_feet_with_a_crouch_then_carries_on()
    {
        var c = Landed();
        c.Grab();
        c.Land(700, facingRight: false);
        Assert.Equal(700, c.X);
        Assert.False(c.FacingRight);
        Assert.Equal(Anim.Crouch, c.Anim);
        CatTests.Run(c, Cat.LandTime - 0.05, each: k => { Assert.Equal(Anim.Crouch, k.Anim); Assert.Equal(0, k.Lift); });
        Assert.False(c.IsLive);
        CatTests.Run(c, 0.1);
        Assert.True(c.IsLive);
        Assert.Equal(0, c.Lift);
    }

    [Fact] public void Land_only_applies_to_a_held_cat()
    {
        var c = Landed();
        c.Land(700, facingRight: true);
        Assert.NotEqual(700, c.X);
        Assert.True(c.IsLive);
    }

    [Fact] public void Landing_cat_can_be_grabbed_again_and_can_leave()
    {
        var c = Landed();
        c.Grab(); c.Land(500, true);
        Assert.True(c.Grab());
        c.Land(500, true);
        c.Despawn();
        CatTests.Run(c, 0.6);
        Assert.True(c.IsGone);
    }

    [Fact] public void Pinned_idle_cat_stays_put_and_meows_until_seen()
    {
        var c = Landed();
        c.Pinned = true;
        double x = c.X;
        var bubbles = new HashSet<Bubble>();
        CatTests.Run(c, 60, new World(1000, x + 60, [c]), k => { Assert.Equal(x, k.X); bubbles.Add(k.Bubble); });
        Assert.Contains(Bubble.Meow, bubbles);
    }

    [Fact] public void Pinned_seen_cat_naps_but_a_click_still_focuses()
    {
        var c = Landed();
        c.Pinned = true;
        c.Click();
        CatTests.Run(c, 2);
        Assert.Contains(c.Anim, Tune.SleepPoses);
        Assert.Equal(Bubble.Zzz, c.Bubble);
        Assert.Equal(ClickResult.Focus, c.Click());
    }

    [Fact] public void Pinned_busy_cat_sits_and_watches_a_nearby_mouse()
    {
        var c = Landed(SessionStatus.Working);
        c.Pinned = true;
        double x = c.X;
        CatTests.Run(c, 20, each: k =>
        {
            Assert.Equal(x, k.X);
            Assert.Equal(Anim.Sit, k.Anim);
            Assert.InRange(k.MouseX, x - Cat.PinnedMouseRange, x + Cat.PinnedMouseRange);
            Assert.Equal(k.MouseX >= k.X, k.FacingRight);
        });
    }

    [Fact] public void Unpinned_cat_wanders_again()
    {
        var c = Landed();
        c.Click();
        c.Pinned = true;
        CatTests.Run(c, 1);
        c.Pinned = false;
        double x = c.X;
        bool moved = false;
        CatTests.Run(c, 120, each: k => moved |= k.X != x);
        Assert.True(moved);
    }

    [Fact] public void Binned_cat_is_off_screen_until_unbinned_then_hops_out_free()
    {
        var c = Landed();
        c.Pinned = true;
        c.Grab();
        c.Bin();
        CatTests.Run(c, 5);
        Assert.True(c.IsBinned);
        Assert.False(c.IsLive);
        Assert.Equal(ClickResult.None, c.Click());
        c.Unbin(900);
        Assert.Equal(900, c.X);
        Assert.True(c.Lift > 0);              // starts at the bin's rim
        CatTests.Run(c, 0.6);
        Assert.True(c.IsLive);
        Assert.False(c.Pinned);
    }

    [Fact] public void Despawn_while_held_or_binned_is_immediate()
    {
        var held = Landed();
        held.Grab();
        held.Despawn();
        Assert.True(held.IsGone);

        var binned = Landed();
        binned.Grab(); binned.Bin();
        binned.Despawn();
        Assert.True(binned.IsGone);
    }

    [Fact] public void Fed_cat_eats_then_says_good_job_then_carries_on()
    {
        var c = Landed(SessionStatus.Working);
        c.Feed();
        var anims = new HashSet<Anim>();
        CatTests.Run(c, 1, each: k => { anims.Add(k.Anim); Assert.Equal(Bubble.GoodJob, k.Bubble); });
        Assert.Contains(Anim.Eat, anims);
        c.Status = SessionStatus.Idle;        // status change mid-meal waits for the meal
        CatTests.Run(c, 0.5);
        Assert.Equal(Anim.Eat, c.Anim);
        CatTests.Run(c, 3);
        Assert.NotEqual(Anim.Eat, c.Anim);
        Assert.NotEqual(Bubble.GoodJob, c.Bubble);
    }
}
