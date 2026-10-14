using TaskbarCats;
using Xunit;

public class ChaseTests
{
    static Cat Busy(double x = 300)
    {
        var c = new Cat("a", x, new Random(1)) { Status = SessionStatus.Working };
        CatTests.Run(c, 0.6);
        return c;
    }

    [Fact] public void Mouse_appears_ahead_of_the_cat_when_work_starts()
    {
        var c = Busy();
        Assert.True(c.HasMouse);
        Assert.True(Math.Abs(c.MouseX - c.X) > Cat.CatchRange);
    }

    [Fact] public void Cat_runs_after_the_mouse_and_pounces_repeatedly()
    {
        var c = Busy();
        int leaps = 0; var prev = c.Anim;
        double minX = c.X, maxX = c.X;
        CatTests.Run(c, 60, each: k =>
        {
            if (k.Anim == Anim.Leap && prev != Anim.Leap) leaps++;
            prev = k.Anim;
            minX = Math.Min(minX, k.X); maxX = Math.Max(maxX, k.X);
        });
        Assert.True(leaps >= 3, $"only {leaps} pounces in 60 s");
        Assert.True(maxX - minX > 200, "cat barely moved");
    }

    [Fact] public void Mouse_and_cat_stay_on_the_strip()
    {
        var c = Busy(60);
        var w = new World(400, null, [c], MinX: 56);
        CatTests.Run(c, 60, w, k =>
        {
            Assert.InRange(k.MouseX, 56, 400 - Cat.Size);
            Assert.InRange(k.X, 56, 400 - Cat.Size);
        });
    }

    [Fact] public void Mouse_vanishes_when_work_stops()
    {
        var c = Busy();
        c.Status = SessionStatus.Idle;
        CatTests.Run(c, 1);
        Assert.False(c.HasMouse);
        c.Status = SessionStatus.Waiting;
        CatTests.Run(c, 1);
        Assert.False(c.HasMouse);
    }

    [Fact] public void Held_busy_cat_leaves_its_mouse_frozen_where_it_was()
    {
        var c = Busy();
        double mx = c.MouseX;
        Assert.True(c.Grab());
        CatTests.Run(c, 3, each: k => { Assert.True(k.HasMouse); Assert.Equal(mx, k.MouseX); });
    }

    [Fact] public void After_helping_the_mouse_the_cat_sprints_until_it_pounces()
    {
        var c = Busy(100);
        var w = new World(2000, null, [c]);
        c.Grab();
        double mx = c.MouseX;
        c.Land(mx > 1000 ? mx - 600 : mx + 600, facingRight: true);     // carried 600 DIP away from the mouse
        Assert.True(c.Boosting);
        CatTests.Run(c, Cat.LandTime + 0.05, w);
        double x0 = c.X;
        CatTests.Run(c, 0.5, w);
        double sprint = Cat.ChaseSpeed * Cat.BoostFactor * 0.5;
        Assert.InRange(Math.Abs(c.X - x0), sprint - 10, sprint + 10);
        bool pounced = false;
        CatTests.Run(c, 10, w, k => pounced |= k.Anim == Anim.Crouch);
        Assert.True(pounced);
        Assert.False(c.Boosting);
    }

    [Fact] public void Dropped_right_on_the_mouse_pounces_at_once_without_a_sprint()
    {
        var c = Busy();
        c.Grab();
        c.Land(c.MouseX, facingRight: true);
        CatTests.Run(c, Cat.LandTime + 0.1);
        Assert.Equal(Anim.Crouch, c.Anim);        // the pounce crouch, straight after the landing one
        Assert.False(c.Boosting);
    }

    [Fact] public void Landing_on_another_monitor_starts_a_fresh_mouse_and_no_sprint()
    {
        var c = Busy();
        c.Grab();
        c.Land(300, facingRight: true, keepMouse: false);
        Assert.False(c.HasMouse);
        Assert.False(c.Boosting);
        CatTests.Run(c, Cat.LandTime + 0.1);
        Assert.True(c.HasMouse);
        Assert.InRange(Math.Abs(c.MouseX - c.X), Cat.CatchRange, Cat.MouseLead + 10);
    }

    [Fact] public void Work_ending_while_held_takes_the_frozen_mouse_away()
    {
        var c = Busy();
        c.Grab();
        c.Status = SessionStatus.Idle;
        Assert.False(c.HasMouse);
        c.Land(300, true);
        Assert.False(c.Boosting);
    }
}