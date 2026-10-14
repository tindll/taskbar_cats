using TaskbarCats;
using Xunit;

public class CatPlayTests
{
    static Cat Busy(Activity a = Activity.Toy, Toy toy = Toy.Mouse, int seed = 1, double x = 300)
    {
        var c = new Cat("a", x, new Random(seed)) { Status = SessionStatus.Working, Toy = toy, Activity = a };
        CatTests.Run(c, 0.6);
        return c;
    }

    [Theory]
    [InlineData(Activity.Watch, Anim.LookUp)] [InlineData(Activity.Butterfly, Anim.LookUp)]
    [InlineData(Activity.Box, Anim.Box)] [InlineData(Activity.Rest, Anim.Loaf)]
    [InlineData(Activity.Nap, Anim.Sleep)] [InlineData(Activity.Limit, Anim.Sleep)] [InlineData(Activity.Sniff, Anim.Sniff)]
    public void Each_activity_has_its_pose_and_no_bubble(Activity a, Anim anim)
    {
        var c = Busy(a);
        CatTests.Run(c, 1);
        Assert.Equal(anim, c.Anim);
        Assert.Equal(Bubble.None, c.Bubble);
    }

    [Fact] public void Poses_bring_their_props()
    {
        var c = Busy(Activity.Watch);
        CatTests.Run(c, 0.5);
        Assert.Equal(Prop.Flyer, c.Prop);
        Assert.InRange(c.PropLift, Cat.Size, OverlayWindow.BandHeight - Cat.Size);
        c.Activity = Activity.Butterfly;
        CatTests.Run(c, 0.5);
        Assert.Equal(Prop.Butterfly, c.Prop);
    }

    [Fact] public void While_a_pose_runs_the_mouse_waits_close_by()
    {
        var c = Busy(Activity.Watch);
        CatTests.Run(c, 10, each: x => Assert.InRange(x.MouseX - x.X, -Cat.PinnedMouseRange - 1, Cat.PinnedMouseRange + 1));
        Assert.True(c.HasMouse);
    }

    [Fact] public void Thinking_never_sends_the_mouse_into_a_hole()
    {
        var c = Busy();
        CatTests.Run(c, 1);
        c.Activity = Activity.Box;
        CatTests.Run(c, 0.5);
        Assert.True(c.HasMouse);                             // it scurries close by, as during any pose
        Assert.InRange(c.MouseX - c.X, -Cat.PinnedMouseRange - 1, Cat.PinnedMouseRange + 1);
        Assert.Equal(Prop.None, c.Prop);
    }

    [Fact] public void A_long_run_rests_beside_the_mouse_nibbling_cheese()
    {
        var c = Busy(Activity.Rest);
        var frames = new HashSet<int>();
        CatTests.Run(c, 2, each: x => frames.Add(x.MouseFrame));
        Assert.True(c.HasMouse);
        Assert.Equal(Tune.MouseBeside, Math.Abs(c.MouseX - c.X), 3);
        Assert.Equal(Prop.Cheese, c.Prop);
        Assert.Equal(2, frames.Count);
        c.Activity = Activity.Limit;
        CatTests.Run(c, 0.1);
        Assert.Equal(Prop.None, c.Prop);                     // rate limited: no cheese, just waiting
    }

    [Fact] public void A_yarn_cat_bats_its_ball_and_runs_after_it()
    {
        var c = Busy(toy: Toy.Yarn);
        Assert.NotNull(c.Yarn);
        Assert.False(c.HasMouse);
        double ball = c.Yarn!.X, cat = c.X;
        bool batted = false;
        CatTests.Run(c, 10, each: x => batted |= x.Anim == Anim.Bat);
        Assert.True(batted);
        Assert.NotEqual(ball, c.Yarn!.X);
        Assert.NotEqual(cat, c.X);
    }

    [Fact] public void Edit_brings_out_a_yarn_ball_for_a_mouse_cat_until_the_pose_ends()
    {
        var c = Busy(Activity.Bat);
        CatTests.Run(c, 0.1);
        Assert.NotNull(c.Yarn);
        c.Activity = Activity.Toy;
        CatTests.Run(c, 0.1);
        Assert.Null(c.Yarn);
        Assert.True(c.HasMouse);
    }

    [Fact] public void A_bird_cat_stalks_and_pounces_but_never_catches()
    {
        var c = Busy(toy: Toy.Bird);
        var seen = new HashSet<Anim>();
        bool flew = false;
        CatTests.Run(c, 60, each: x =>
        {
            seen.Add(x.Anim);
            flew |= x.Bird!.Flying;
            Assert.False(!x.Bird.Flying && x.Anim != Anim.Leap && Math.Abs(x.Bird.X - x.X) < Tune.BirdFlee / 2, "caught the bird");
        });
        Assert.True(flew);
        Assert.Contains(Anim.Crouch, seen);
        Assert.Contains(Anim.Leap, seen);
        Assert.False(c.HasMouse);
    }

    [Fact] public void An_idle_cat_has_no_toys_or_props()
    {
        var c = Busy(Activity.Box, Toy.Yarn);
        Assert.NotNull(c.Yarn);
        c.Status = SessionStatus.Idle;
        CatTests.Run(c, 0.1);
        Assert.Null(c.Yarn);
        Assert.Null(c.Bird);
        Assert.False(c.HasMouse);
        Assert.Equal(Prop.None, c.Prop);
    }

    [Fact] public void Kittens_trail_behind_and_play_in_front_when_supervising()
    {
        var c = Busy(Activity.Watch);
        c.Kittens = 2;
        CatTests.Run(c, 0.1);
        bool right = c.FacingRight;
        Assert.Equal(right, c.KittenX(0) < c.X);
        Assert.True(Math.Abs(c.KittenX(1) - c.X) > Math.Abs(c.KittenX(0) - c.X));
        c.Activity = Activity.Supervise;
        double maxLift = 0;
        CatTests.Run(c, 2, each: x => maxLift = Math.Max(maxLift, x.KittenLift(0)));
        Assert.Equal(c.FacingRight, c.KittenX(0) > c.X);
        Assert.True(maxLift > 0);
        Assert.Equal(Anim.Sit, c.Anim);
    }

    [Fact] public void Trait_speed_scales_the_chase()
    {
        double Moved(double speed)
        {
            var c = Busy();
            c.Speed = speed;
            double x0 = c.X;
            CatTests.Run(c, 0.5);
            return Math.Abs(c.X - x0);
        }
        Assert.True(Moved(1.3) > Moved(1) * 1.2);
    }

    [Theory]
    [InlineData(Activity.Toy)] [InlineData(Activity.Watch)] [InlineData(Activity.Box)] [InlineData(Activity.Rest)]
    [InlineData(Activity.Nap)] [InlineData(Activity.Sniff)] [InlineData(Activity.Limit)]
    public void The_mouse_never_covers_the_cats_face(Activity a)
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var c = Busy(a, seed: seed);
            CatTests.Run(c, 40, each: x =>
            {
                if (!x.HasMouse) return;
                // 16 px art x3: the mouse covers cols 0-12 (x .. x+39), mirrored 3-15 (x+9 .. x+48); the head is the cat's front 6 cols.
                double m0 = x.MouseX + (x.MouseFacingRight ? 0 : 9), m1 = m0 + 39;
                double h0 = x.X + (x.FacingRight ? 30 : 0), h1 = h0 + 18;
                Assert.True(m1 + 9 <= h0 || m0 >= h1 + 9, $"{a} seed {seed}: mouse {m0}..{m1} touches head {h0}..{h1}");
            });
        }
    }
}
