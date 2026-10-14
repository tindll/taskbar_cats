using TaskbarCats;
using Xunit;

public class BirdTests
{
    static void Run(Bird b, double seconds, double catX, double lo = 0, double hi = 1000, Action? each = null)
    {
        for (double t = 0; t < seconds; t += 1 / 60.0) { b.Step(1 / 60.0, catX, lo, hi); each?.Invoke(); }
    }

    [Fact] public void It_flies_off_when_the_cat_comes_close_and_lands_farther_on()
    {
        var b = new Bird(300, new Random(1));
        b.Step(1 / 60.0, 250, 0, 1000);
        Assert.True(b.Flying);
        Assert.True(b.FacingRight);
        Run(b, Tune.BirdFlyTime + 0.1, 900);
        Assert.False(b.Flying);
        Assert.Equal(0, b.Lift);
        Assert.InRange(b.X, 300 + Tune.BirdFlyDist - 1, 300 + Tune.BirdFlyDist + 1);
    }

    [Fact] public void Cornered_it_flies_back_over_the_cat_and_stays_on_the_strip()
    {
        var b = new Bird(980, new Random(1));
        double maxLift = 0;
        b.Flee(940, 0, 1000);
        Run(b, Tune.BirdFlyTime + 0.1, -5000, each: () => maxLift = Math.Max(maxLift, b.Lift));
        Assert.InRange(b.X, 980 - Tune.BirdFlyDist - 1, 980 - Tune.BirdFlyDist + 1);
        Assert.InRange(maxLift, Cat.Size, OverlayWindow.BandHeight - Cat.Size);   // clears the cat, stays in the strip
    }

    [Fact] public void Far_from_the_cat_it_pecks_and_hops_about()
    {
        var b = new Bird(500, new Random(2));
        var frames = new HashSet<int>();
        Run(b, 10, 0, each: () => { Assert.False(b.Flying); frames.Add(b.Frame); });
        Assert.Equal([0, 1], frames.Order());
        Assert.InRange(b.X, 500 - 5 * Tune.BirdHop, 500 + 5 * Tune.BirdHop);
    }

    [Fact] public void Its_wings_flap_in_flight()
    {
        var b = new Bird(500, new Random(3));
        b.Flee(450, 0, 1000);
        var frames = new HashSet<int>();
        Run(b, 0.5, -5000, each: () => frames.Add(b.Frame));
        Assert.Equal(2, frames.Count);
    }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void A_click_scares_it_into_flight_and_it_lands_elsewhere_inside_the_strip(int seed)
    {
        var b = new Bird(300, new Random(seed));
        Assert.True(b.Scare(100, 0, 1000));
        Assert.True(b.Flying);
        Assert.False(b.Scare(100, 0, 1000));       // already in the air
        Run(b, Tune.BirdFlyTime + 0.1, 100);
        Assert.False(b.Flying);
        Assert.True(Math.Abs(b.X - 300) >= Tune.BirdFlyDist / 2);
        Assert.True(Math.Abs(b.X - 100) >= Tune.BirdFlee);
        Assert.InRange(b.X, 0, 1000);
    }

    [Fact] public void A_click_on_a_cramped_strip_still_scares_it_and_keeps_it_on_the_strip()
    {
        var b = new Bird(50, new Random(1));
        Assert.True(b.Scare(40, 0, 100));          // no spot far enough: the usual flee hop
        Assert.True(b.Flying);
        Run(b, Tune.BirdFlyTime + 0.1, 40, 0, 100);
        Assert.InRange(b.X, 0, 100);
    }

    [Fact] public void Clicking_a_bird_cats_bird_makes_it_fly_but_not_when_the_cat_stopped_working()
    {
        var c = new Cat("a", 100, new Random(1)) { Status = SessionStatus.Working, Toy = Toy.Bird, Activity = Activity.Rest };
        CatTests.Run(c, 0.6);
        Assert.True(c.ScareBird());
        Assert.True(c.Bird!.Flying);
        c.Status = SessionStatus.Idle;
        Assert.False(c.ScareBird());
    }
}
