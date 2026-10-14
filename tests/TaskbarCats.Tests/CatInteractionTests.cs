using TaskbarCats;
using Xunit;

public class CatInteractionTests
{
    static Cat Landed(string id, double x)
    {
        var c = new Cat(id, x, new Random(id.GetHashCode()));
        CatTests.Run(c, 0.6, new World(1000, null, [c]));
        c.Click();   // acknowledged: no sleep
        return c;
    }

    [Fact] public void Approaching_walkers_face_off_and_lower_id_hisses()
    {
        var a = Landed("a", 100);
        var b = Landed("b", 150);
        a.StartWalk(right: true, 10);
        b.StartWalk(right: false, 10);
        var w = new World(1000, null, [a, b]);
        bool aHissed = false, bHissed = false;
        for (int i = 0; i < 30; i++)
        {
            a.Tick(1.0 / 30, w); b.Tick(1.0 / 30, w);
            aHissed |= a.Anim == Anim.Hiss; bHissed |= b.Anim == Anim.Hiss;
        }
        Assert.True(aHissed);
        Assert.False(bHissed);
        for (int i = 0; i < 60; i++) { a.Tick(1.0 / 30, w); b.Tick(1.0 / 30, w); }
        Assert.False(a.FacingRight);   // turned around and walked away
        Assert.True(b.FacingRight);
    }

    [Fact] public void Walkers_moving_apart_do_not_hiss()
    {
        var a = Landed("a", 100);
        var b = Landed("b", 140);
        a.StartWalk(right: false, 10);
        b.StartWalk(right: true, 10);
        var w = new World(1000, null, [a, b]);
        for (int i = 0; i < 60; i++)
        {
            a.Tick(1.0 / 30, w); b.Tick(1.0 / 30, w);
            Assert.NotEqual(Anim.Hiss, a.Anim);
            Assert.NotEqual(Anim.Hiss, b.Anim);
        }
    }

    [Fact] public void Cursor_close_triggers_crouch_then_leap_to_cursor()
    {
        var c = Landed("a", 100);
        var w = new World(1000, 190, [c]);   // cursor 66 DIP from centre (124)
        var anims = new List<Anim>();
        CatTests.Run(c, 1.2, w, k => anims.Add(k.Anim));
        Assert.Contains(Anim.Crouch, anims);
        Assert.Contains(Anim.Leap, anims);
        Assert.InRange(c.X + Cat.Size / 2, 185, 195);
        Assert.True(c.FacingRight);
    }

    [Fact] public void Pounce_has_cooldown()
    {
        var c = Landed("a", 100);
        var w = new World(1000, 190, [c]);
        CatTests.Run(c, 1.2, w);
        int crouches = 0;
        CatTests.Run(c, 5, w, k => { if (k.Anim == Anim.Crouch) crouches++; });
        Assert.Equal(0, crouches);
    }

    [Fact] public void Far_cursor_never_triggers_a_pounce()
    {
        var far = Landed("a", 100);
        CatTests.Run(far, 3, new World(1000, 400, [far]), k => Assert.NotEqual(Anim.Crouch, k.Anim));
    }

    [Fact] public void Working_cat_ignores_the_cursor()
    {
        var withCursor = Landed("b", 500);
        var without = Landed("b", 500);
        withCursor.Status = without.Status = SessionStatus.Working;
        for (int i = 0; i < 300; i++)
        {
            withCursor.Tick(1.0 / 30, new World(1000, withCursor.X + 30, [withCursor]));
            without.Tick(1.0 / 30, new World(1000, null, [without]));
            Assert.Equal(without.X, withCursor.X);
        }
    }
}
