using TaskbarCats;
using Xunit;

public class YarnThrowTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    static readonly Screen Main = new(@"\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1032), true, 1.0);

    static void Fly(Flight f) { for (int i = 0; i < 300 && !f.Landed; i++) f.Advance(1 / 30.0, [Main]); }

    [Fact] public void A_flight_remembers_its_sideways_speed_at_touchdown()
    {
        var f = new Flight(300, 500, 600, 0, Main, facingRight: true);
        Fly(f);
        Assert.True(f.Landed);
        Assert.Equal(0, f.Vx);
        Assert.Equal(600, f.LandVx, 3);
    }

    [Fact] public void A_straight_drop_lands_still()
    {
        var f = new Flight(300, 500, 0, 0, Main, facingRight: true);
        Fly(f);
        Assert.Equal(0, f.LandVx);
    }

    const double Floor = 1032 - 24;

    [Fact] public void A_thrown_ball_bounces_on_the_taskbar_then_settles_and_never_sinks_into_it()
    {
        var f = new Flight(300, 300, 400, 0, Main, facingRight: true, floorBounce: Tune.YarnFloorBounce);
        int bounces = 0;
        double t = 0, lastVy = 0;
        while (!f.Landed && t < 5)
        {
            f.Advance(1 / 30.0, [Main]); t += 1 / 30.0;
            Assert.True(f.Y <= Floor + 1e-9, $"sank to {f.Y}");
            if (lastVy > 0 && f.Vy < 0) bounces++;
            lastVy = f.Vy;
        }
        Assert.True(f.Landed);
        Assert.InRange(bounces, 1, 6);
        Assert.InRange(t, 0.5, 3);
        Assert.True(f.LandVx > 0 && f.LandVx < 400, $"sideways {f.LandVx}");   // the bounces cost it some speed
    }

    [Fact] public void Each_bounce_is_lower_than_the_last()
    {
        var f = new Flight(300, 100, 0, 0, Main, facingRight: true, floorBounce: Tune.YarnFloorBounce);
        var peaks = new List<double>();
        double lastVy = 0;
        for (int i = 0; i < 2400 && !f.Landed; i++)
        {
            f.Advance(Flight.Step, [Main]);
            if (lastVy < 0 && f.Vy >= 0) peaks.Add(f.Y);    // top of a hop (y grows downwards)
            lastVy = f.Vy;
        }
        Assert.True(peaks.Count >= 2);
        for (int k = 1; k < peaks.Count; k++) Assert.True(peaks[k] > peaks[k - 1], "a bounce went higher than the one before");
        Assert.True(f.Landed);
    }
}
