using TaskbarCats;
using Xunit;

/// The carry: a little lag behind the pointer, then a throw with the pointer's recent speed. Physical px.
public class CarryTests
{
    [Fact] public void A_carried_thing_lags_the_pointer_then_catches_up()
    {
        var c = new Carry(0, 0, 0);
        c.Sample(0, 100, 0);
        c.Follow(1 / 30.0);
        Assert.InRange(c.X, 1, 90);                   // behind the pointer after one frame
        for (int i = 0; i < 15; i++) c.Follow(1 / 30.0);
        Assert.InRange(c.X, 99, 100.000001);          // there within about half a second
    }

    [Fact] public void It_never_overshoots_a_still_pointer_at_any_frame_rate()
    {
        foreach (double dt in new[] { 1 / 240.0, 1 / 60.0, 1 / 30.0, 0.1 })
        {
            var c = new Carry(0, 0, 0);
            c.Sample(0, 100, -50);
            for (double t = 0; t < 1; t += dt)
            {
                c.Follow(dt);
                Assert.InRange(c.X, 0, 100.000001);
                Assert.InRange(c.Y, -50.000001, 0);
            }
        }
    }

    [Fact] public void The_follow_is_about_the_same_at_any_frame_rate()
    {
        double After(double dt)
        {
            var c = new Carry(0, 0, 0);
            c.Sample(0, 100, 0);
            for (int i = 0; i < Math.Round(0.1 / dt); i++) c.Follow(dt);
            return c.X;
        }
        Assert.InRange(Math.Abs(After(1 / 120.0) - After(1 / 30.0)), 0, 5);
    }

    [Fact] public void Release_throws_with_the_recent_pointer_speed_not_the_lagging_body()
    {
        var c = new Carry(0, 0, 0);
        for (int i = 1; i <= 10; i++) { c.Sample(i / 100.0, i * 20, 0); c.Follow(0.01); }   // a 2000 px/s flick
        var (vx, vy) = c.Throw();
        Assert.Equal(2000, vx, 3);
        Assert.Equal(0, vy, 3);
        Assert.True(c.X < 200, "the body lags behind the pointer");
    }

    [Fact] public void Late_samples_are_ignored()
    {
        var c = new Carry(1, 0, 0);
        c.Sample(0.5, 500, 500);
        c.Follow(1);
        Assert.Equal(0, c.X);
        Assert.Equal(0, c.Y);
    }
}
