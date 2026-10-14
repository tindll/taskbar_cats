using TaskbarCats;
using Xunit;

/// Throw physics: pure, in physical pixels. Monitors as on this machine (see ScreensTests).
public class FlightTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    static readonly Screen Main = new(@"\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1032), true, 1.0);
    static readonly Screen Right = new(@"\.\DISPLAY2", R(1920, -78, 3840, 1002), R(1920, -78, 3840, 954), false, 1.0);
    static readonly Screen Hi = new(@"\.\DISPLAY3", R(0, 0, 2560, 1440), R(0, 0, 2560, 1392), true, 1.5);
    static readonly Screen[] Both = [Main, Right];

    /// Runs until landed (or 10 s), 30 fps like Herd. Returns seconds in the air.
    static double Fly(Flight f, IReadOnlyList<Screen> screens, Action<Flight>? each = null)
    {
        double t = 0;
        while (!f.Landed && t < 10) { f.Advance(1 / 30.0, screens); each?.Invoke(f); t += 1 / 30.0; }
        return t;
    }

    [Fact] public void Let_go_without_moving_falls_straight_down_onto_the_taskbar_edge()
    {
        var f = new Flight(500, 100, 0, 0, Main, facingRight: true);
        double t = Fly(f, Both, k => Assert.Equal(500, k.X));
        Assert.True(f.Landed);
        Assert.Equal(1032 - 24, f.Y);
        Assert.Same(Main, f.Screen);
        Assert.InRange(t, 0.75, 0.95);       // sqrt(2 * 908 / 2500) = 0.85 s
    }

    [Fact] public void Let_go_on_the_taskbar_lands_at_once()
    {
        var f = new Flight(500, 1050, 300, 0, Main, facingRight: true);
        Assert.True(f.Landed);
        Assert.Equal(1032 - 24, f.Y);
    }

    [Fact] public void A_throw_keeps_going_the_way_the_pointer_went()
    {
        var f = new Flight(300, 500, 800, -400, Main, facingRight: false);
        Assert.True(f.FacingRight);
        double prevX = f.X;
        Fly(f, Both, k => { Assert.True(k.X >= prevX); prevX = k.X; });
        Assert.InRange(f.X, 900, 1920 - 24);
    }

    [Fact] public void Bounces_off_the_outer_wall_and_the_ceiling_and_stays_on_screen()
    {
        var f = new Flight(200, 300, -2000, -2000, Main, facingRight: false);
        bool bouncedX = false, bouncedY = false;
        Fly(f, Both, k =>
        {
            Assert.InRange(k.X, 24, 1920 - 24);
            Assert.True(k.Y >= 24, $"y {k.Y} went through the ceiling");
            bouncedX |= k.Vx > 0;
            bouncedY |= k.Vy > 0 && k.Y < 100;
        });
        Assert.True(bouncedX && bouncedY);
        Assert.True(f.FacingRight);           // turned round at the wall
        Assert.Equal(1032 - 24, f.Y);
    }

    [Fact] public void Flies_onto_the_neighbour_monitor_and_lands_on_its_taskbar()
    {
        var f = new Flight(1700, 300, 1200, -300, Main, facingRight: true);
        Fly(f, Both);
        Assert.Same(Right, f.Screen);
        Assert.Equal(954 - 24, f.Y);
        Assert.True(f.X > 1920);
    }

    [Fact] public void Bounces_where_the_neighbour_monitor_does_not_reach()
    {
        // DISPLAY2 starts 78 px higher than the primary: at y = -50 there is nothing left of x = 1920.
        var f = new Flight(2000, -40, -1500, 0, Right, facingRight: false);
        Fly(f, Both, k => Assert.True(k.X >= 1920 + 24 || k.Y >= 0, $"({k.X}, {k.Y}) left the desktop"));
        Assert.True(f.Landed);
    }

    [Fact] public void A_wild_fling_is_capped()
    {
        var f = new Flight(500, 500, 90000, 0, Main, facingRight: true);
        Assert.Equal(Flight.MaxSpeed, f.Vx, 3);
    }

    [Fact] public void Speeds_scale_with_the_monitor_dpi_and_the_cat_lands_on_its_feet_there()
    {
        var f = new Flight(500, 500, 90000, 0, Hi, facingRight: true);
        Assert.Equal(Flight.MaxSpeed * 1.5, f.Vx, 3);
        Fly(f, [Hi]);
        Assert.Equal(1392 - 36, f.Y);         // half of 48 DIP at 150 %
    }

    [Fact] public void Fixed_step_gives_the_same_flight_at_any_frame_rate()
    {
        var a = new Flight(300, 200, 700, -900, Main, facingRight: true);
        var b = new Flight(300, 200, 700, -900, Main, facingRight: true);
        for (int i = 0; i < 30; i++) a.Advance(1 / 30.0, Both);
        for (int i = 0; i < 10; i++) b.Advance(0.1, Both);
        Assert.Equal(a.X, b.X, 6);
        Assert.Equal(a.Y, b.Y, 6);
    }

    [Fact] public void Fly_frame_spreads_legs_going_up_and_reaches_down_coming_down()
    {
        var f = new Flight(500, 500, 0, -1000, Main, facingRight: true);
        Assert.Equal(0, f.FlyFrame);
        f.Advance(0.5, Both);
        Assert.Equal(1, f.FlyFrame);
    }

    [Fact] public void Velocity_uses_the_last_sample_window()
    {
        Assert.Equal((0.0, 0.0), Flight.Velocity([]));
        Assert.Equal((0.0, 0.0), Flight.Velocity([(1.0, 5, 5)]));
        // Slow drag, then a quick flick right in the last 0.1 s: only the flick counts.
        var v = Flight.Velocity([(0.0, 0, 0), (0.5, 10, 0), (0.6, 10, 0), (0.7, 110, -50)]);
        Assert.Equal(1000, v.Vx, 3);
        Assert.Equal(-500, v.Vy, 3);
        // Held still for the last 0.1 s before letting go: no throw.
        Assert.Equal((0.0, 0.0), Flight.Velocity([(0.0, 0, 0), (0.1, 300, 0), (0.2, 300, 0), (0.3, 300, 0)]));
    }

    [Fact] public void Carried_cat_sways_when_still_and_swings_against_the_drag()
    {
        Assert.Equal(2, Flight.SwingFrame(400, 0));      // pointer going right: body swings left
        Assert.Equal(3, Flight.SwingFrame(-400, 0));
        Assert.Equal(0, Flight.SwingFrame(50, 0.1));
        Assert.Equal(1, Flight.SwingFrame(0, 0.7));
    }

    [Fact] public void A_flight_remembers_how_hard_it_came_down()
    {
        var f = new Flight(500, 100, 0, 0, Main, facingRight: true);
        Fly(f, Both);
        Assert.InRange(f.LandVy, 2000, 2250);     // sqrt(2 * 2500 * 908) = 2131 px/s
    }

    [Fact] public void Time_aloft_counts_the_fixed_steps_in_the_air()
    {
        var f = new Flight(500, 100, 0, -500, Main, facingRight: true);
        f.Advance(0.25, Both);
        Assert.InRange(f.Aloft, 0.24, 0.26);
    }
}
