using TaskbarCats;
using Xunit;

public class PursuitsTests
{
    static string Id(int i) => new Guid(i, 5, 5, new byte[8]).ToString();
    static readonly TimeSpan Min = TimeSpan.FromMinutes(1);

    [Fact] public void Three_busy_cats_get_three_different_toys()
    {
        var toys = Pursuits.Assign([(Id(1), Min), (Id(2), 2 * Min), (Id(3), 3 * Min)]);
        Assert.Equal(3, toys.Values.Distinct().Count());
    }

    [Fact] public void The_longest_running_cat_keeps_its_favourite()
    {
        var ids = Enumerable.Range(0, 100).Select(Id).ToList();
        var a = ids.First();
        var b = ids.Skip(1).First(i => Tune.FavouriteToy(i) == Tune.FavouriteToy(a));
        var toys = Pursuits.Assign([(b, Min), (a, 5 * Min)]);
        Assert.Equal(Tune.FavouriteToy(a), toys[a]);
        Assert.NotEqual(toys[a], toys[b]);
    }

    [Fact] public void A_cat_moves_to_the_next_toy_every_20_minutes()
    {
        var fav = (int)Tune.FavouriteToy(Id(7));
        Assert.Equal((Toy)fav, Pursuits.Assign([(Id(7), 19 * Min)])[Id(7)]);
        Assert.Equal((Toy)((fav + 1) % 3), Pursuits.Assign([(Id(7), 21 * Min)])[Id(7)]);
        Assert.Equal((Toy)((fav + 2) % 3), Pursuits.Assign([(Id(7), 41 * Min)])[Id(7)]);
    }

    [Fact] public void A_fourth_cat_shares_a_toy()
    {
        var toys = Pursuits.Assign(Enumerable.Range(1, 4).Select(i => (Id(i), i * Min)));
        Assert.Equal(4, toys.Count);
        Assert.Equal(3, toys.Values.Distinct().Count());
    }

    [Fact] public void A_batted_ball_rolls_to_a_stop()
    {
        var y = new YarnBall(100);
        y.Push(Tune.YarnBat);
        Assert.True(y.Rolling);
        for (int i = 0; i < 600 && y.Rolling; i++) y.Step(1 / 60.0, 0, 2000);
        Assert.False(y.Rolling);
        double expected = Tune.YarnBat * Tune.YarnBat / (2 * Tune.YarnFriction);   // v² / 2a
        Assert.InRange(y.X - 100, expected - 10, expected + 10);
    }

    [Fact] public void The_ball_bounces_off_the_strip_ends()
    {
        var y = new YarnBall(10);
        y.Push(-200);
        y.Step(0.1, 0, 500);
        Assert.InRange(y.X, 0, 10);                // hit the end inside this step and came back off it
        Assert.True(y.Vx > 0);
        Assert.True(y.Vx < 200 * Tune.YarnBounce + 1);
    }

    [Fact] public void Rolling_turns_the_ball_and_friction_stops_it()
    {
        var y = new YarnBall(0);
        int f0 = y.Frame;
        y.Push(120);
        y.Step(0.11, -1000, 1000);
        Assert.NotEqual(f0, y.Frame);
        for (int i = 0; i < 300; i++) y.Step(1 / 30.0, -1000, 1000);
        Assert.False(y.Rolling);
    }

    [Fact] public void Roll_distance_does_not_depend_on_the_frame_rate()
    {
        double Rolled(double dt)
        {
            var y = new YarnBall(100);
            y.Push(Tune.YarnBat);
            for (int i = 0; i < 1000 && y.Rolling; i++) y.Step(dt, 0, 5000);
            return y.X - 100;
        }
        Assert.InRange(Math.Abs(Rolled(1 / 120.0) - Rolled(0.1)), 0, 1);
    }

    [Fact] public void A_ball_is_either_rolling_or_dead_still()
    {
        var y = new YarnBall(100);
        y.Push(Tune.YarnBat);
        for (int i = 0; i < 600; i++)
        {
            y.Step(1 / 30.0, 0, 5000);
            Assert.True(y.Vx == 0 || Math.Abs(y.Vx) > Tune.YarnStop, $"creeping at {y.Vx}");
        }
    }

    [Fact] public void Roll_frames_never_flip_faster_than_the_screen_can_show()
    {
        var y = new YarnBall(0);
        y.Push(300);
        int flips = 0, last = y.Frame;
        for (int i = 0; i < 30; i++) { y.Step(1 / 30.0, 0, 100000); if (y.Frame != last) flips++; last = y.Frame; }
        Assert.InRange(flips, 1, (int)Tune.YarnFlipHz);   // one second of fast rolling
    }
    [Fact] public void A_picked_toy_is_kept_however_long_the_run()
    {
        var picked = new Dictionary<string, Toy> { [Id(7)] = Toy.Bird };
        Assert.Equal(Toy.Bird, Pursuits.Assign([(Id(7), 61 * Min)], picked)[Id(7)]);
    }

    [Fact] public void A_picked_toy_beats_a_longer_running_cats_favourite()
    {
        var a = Id(1);
        var fav = Tune.FavouriteToy(a);
        var toys = Pursuits.Assign([(a, 5 * Min), (Id(2), Min)], new Dictionary<string, Toy> { [Id(2)] = fav });
        Assert.Equal(fav, toys[Id(2)]);
        Assert.NotEqual(fav, toys[a]);
    }
}
