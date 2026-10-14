using TaskbarCats;
using Xunit;

public class CatFriendTests
{
    static Cat Landed(double x, SessionStatus s = SessionStatus.Idle, Toy toy = Toy.Mouse, string id = "a")
    {
        var c = new Cat(id, x, new Random(1)) { Status = s, Toy = toy };
        CatTests.Run(c, 0.6);
        return c;
    }

    [Fact] public void A_greeting_cat_walks_over_and_nuzzles_with_a_heart()
    {
        var a = Landed(100);
        var b = Landed(300, id: "b");
        a.Greet(b);
        bool nuzzled = false, heart = false;
        double closest = double.MaxValue;
        CatTests.Run(a, 9, new World(1000, null, [a, b]), x =>
        {
            nuzzled |= x.Anim == Anim.Nuzzle;
            heart |= x.Murmur == Murmur.Heart;
            closest = Math.Min(closest, Math.Abs(b.X - x.X));
        });
        Assert.True(nuzzled);
        Assert.True(heart);
        Assert.True(closest <= Cat.Size);
    }

    [Fact] public void Only_an_idle_cat_that_is_up_and_about_greets()
    {
        var b = Landed(300, id: "b");
        var pinned = Landed(100);
        pinned.Pinned = true;
        var asleep = Landed(100);
        CatTests.Run(asleep, Cat.SleepAfter + 1);
        var cats = new[] { pinned, asleep, Landed(100, SessionStatus.Waiting), Landed(100, SessionStatus.Working) };
        foreach (var c in cats)
        {
            double x0 = c.X;
            c.Greet(b);
            CatTests.Run(c, 3, new World(1000, null, [c, b]), x => Assert.NotEqual(Murmur.Heart, x.Murmur));
            if (c.Status != SessionStatus.Working) Assert.Equal(x0, c.X);
        }
    }

    [Fact] public void A_greeting_gives_up_after_a_while()
    {
        var a = Landed(100);
        var b = Landed(950, id: "b");
        a.Greet(b);
        bool nuzzled = false;
        CatTests.Run(a, Tune.GreetTimeout + 2, new World(1000, null, [a, b]), x => nuzzled |= x.Anim == Anim.Nuzzle);
        Assert.False(nuzzled);
        Assert.True(b.X - a.X > Cat.Size);
    }

    [Fact] public void Idle_friends_visit_each_other_now_and_then()
    {
        var a = Landed(100);
        var b = Landed(400, id: "b");
        a.Friend = b;
        double closest = double.MaxValue;
        CatTests.Run(a, 90, new World(1000, null, [a, b]), x => closest = Math.Min(closest, Math.Abs(b.X - x.X)));
        Assert.True(closest <= Cat.Size);
    }

    [Fact] public void A_thrown_yarn_ball_lands_rolling_and_the_cat_runs_after_it()
    {
        var c = Landed(300, SessionStatus.Working, Toy.Yarn);
        Assert.True(c.TakeYarn());
        Assert.Null(c.Yarn);
        CatTests.Run(c, 1);
        Assert.Null(c.Yarn);   // still in the user's hand
        c.DropYarn(700, 300);
        Assert.Equal(700, c.Yarn!.X);
        Assert.Equal(300 * Tune.YarnLandKeep, c.Yarn.Vx);
        double x0 = c.X;
        CatTests.Run(c, 2);
        Assert.True(c.X > x0 + 50);
    }

    [Fact] public void A_ball_thrown_off_the_strip_lands_at_the_nearest_end_and_not_too_fast()
    {
        var c = Landed(300, SessionStatus.Working, Toy.Yarn);
        c.TakeYarn();
        c.DropYarn(5000, 5000);
        Assert.Equal(1000 - Cat.Size, c.Yarn!.X);
        Assert.Equal(Tune.YarnMaxRoll, c.Yarn.Vx);
    }

    [Fact] public void A_ball_thrown_to_a_cat_that_stopped_working_is_gone()
    {
        var c = Landed(300, SessionStatus.Working, Toy.Yarn);
        c.TakeYarn();
        c.Status = SessionStatus.Idle;
        c.DropYarn(500, 0);
        Assert.Null(c.Yarn);
    }

    [Fact] public void Only_a_ball_that_is_there_can_be_taken()
    {
        var c = Landed(300, SessionStatus.Working, Toy.Mouse);
        Assert.False(c.TakeYarn());
    }

    static void RunBoth(Cat a, Cat b, double seconds, World w, Action? each = null)
    {
        const double dt = 1.0 / 30;
        for (double t = 0; t < seconds; t += dt) { a.Tick(dt, w); b.Tick(dt, w); each?.Invoke(); }
    }

    [Fact] public void Two_friends_play_tag_the_chaser_taps_and_roles_swap()
    {
        var a = Landed(300);
        var b = Landed(400, id: "b");
        var w = new World(1000, null, [a, b]);
        Assert.True(a.StartTag(b));
        Assert.True(a.It);
        Assert.False(b.It);
        int swaps = 0;
        bool wasIt = a.It, ran = false, bat = false;
        RunBoth(a, b, Tune.TagTime - 0.5, w, () =>
        {
            if (a.It != wasIt) { swaps++; wasIt = a.It; }
            ran |= a.Anim == Anim.Run || b.Anim == Anim.Run;
            bat |= a.Anim == Anim.Bat || b.Anim == Anim.Bat;
            Assert.InRange(a.X, 0, 1000 - Cat.Size);
            Assert.InRange(b.X, 0, 1000 - Cat.Size);
        });
        Assert.True(ran);
        Assert.True(bat);
        Assert.True(swaps >= 1);
        RunBoth(a, b, 1.5, w);
        Assert.False(a.IsTagging);
        Assert.False(b.IsTagging);
    }

    [Fact] public void Tag_needs_both_cats_idle_awake_and_free()
    {
        var b = Landed(400, id: "b");
        var pinned = Landed(100);
        pinned.Pinned = true;
        var asleep = Landed(100);
        CatTests.Run(asleep, Cat.SleepAfter + 1);
        foreach (var c in new[] { pinned, asleep, Landed(100, SessionStatus.Waiting), Landed(100, SessionStatus.Working) })
        {
            Assert.False(c.StartTag(b));
            Assert.False(b.StartTag(c));
        }
        Assert.False(b.StartTag(b));
    }

    [Fact] public void Picking_up_either_cat_or_work_starting_ends_the_game_for_both()
    {
        var a = Landed(300);
        var b = Landed(400, id: "b");
        var w = new World(1000, null, [a, b]);
        Assert.True(a.StartTag(b));
        RunBoth(a, b, 1, w);
        Assert.True(b.Grab());
        RunBoth(a, b, 0.2, w);
        Assert.False(a.IsTagging);
        Assert.False(b.IsTagging);

        var c = Landed(300, id: "c");
        var d = Landed(400, id: "d");
        w = new World(1000, null, [c, d]);
        Assert.True(c.StartTag(d));
        RunBoth(c, d, 1, w);
        d.Status = SessionStatus.Working;
        RunBoth(c, d, 0.2, w);
        Assert.False(c.IsTagging);
        Assert.False(d.IsTagging);
    }

    [Fact] public void After_a_game_neither_cat_plays_again_for_a_few_minutes()
    {
        var a = Landed(300);
        var b = Landed(400, id: "b");
        a.Click(); b.Click();   // seen: they stay up instead of napping
        var w = new World(1000, null, [a, b]);
        Assert.True(a.StartTag(b));
        RunBoth(a, b, Tune.TagTime + 1, w);
        Assert.False(a.StartTag(b));
        Assert.False(b.StartTag(a));
        RunBoth(a, b, Tune.TagCooldown, w);
        Assert.True(a.StartTag(b));
    }

    [Fact] public void Idle_friends_start_a_game_of_tag_now_and_then()
    {
        var a = Landed(300);
        var b = Landed(600, id: "b");
        a.Click(); b.Click();
        a.Friend = b; b.Friend = a;
        bool tagged = false;
        RunBoth(a, b, 600, new World(1000, null, [a, b]), () => tagged |= a.IsTagging);
        Assert.True(tagged);
    }

    [Fact] public void A_tag_crouch_is_never_a_pounce()
    {
        var a = Landed(300);
        var b = Landed(400, id: "b");
        var w = new World(1000, null, [a, b]);
        Assert.True(a.StartTag(b));   // a crouches before chasing
        a.Pinned = true;
        RunBoth(a, b, 1, w, () => Assert.NotEqual(Anim.Leap, a.Anim));
        Assert.Equal(300, a.X);
        Assert.False(a.IsTagging);
    }

    [Fact] public void A_cat_that_leaves_the_strip_is_forgotten()
    {
        var a = Landed(300);
        var b = Landed(400, id: "b");
        a.Friend = b;
        Assert.True(a.StartTag(b));
        a.Forget(b);
        Assert.Null(a.Friend);
        Assert.False(a.IsTagging);
    }
}
