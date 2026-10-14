using TaskbarCats;
using Xunit;

public class ScreensTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    // This machine: DISPLAY2 sits right of the primary, 78 px higher, with its own taskbar.
    static readonly Screen Main = new(@"\\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1032), true, 1.0);
    static readonly Screen Right = new(@"\\.\DISPLAY2", R(1920, -78, 3840, 1002), R(1920, -78, 3840, 954), false, 1.0);
    static readonly Screen Left150 = new(@"\\.\DISPLAY3", R(-2560, 0, 0, 1440), R(-2560, 0, 0, 1392), false, 1.5);

    [Fact] public void Primary_is_the_flagged_one() => Assert.Same(Main, Screens.Primary([Right, Main]));

    [Fact] public void At_finds_the_monitor_under_a_point()
    {
        Screen[] all = [Main, Right];
        Assert.Same(Right, Screens.At(all, 2000, -50));
        Assert.Same(Main, Screens.At(all, 1919, 1079));
        Assert.Null(Screens.At(all, 100, -50));   // above the primary, left of DISPLAY2
    }

    [Fact] public void Strip_sits_on_the_work_area_bottom_scaled_to_the_monitor()
    {
        var s = Screens.Strip(Right, 120);
        Assert.Equal((1920, 834, 3840, 954), (s.Left, s.Top, s.Right, s.Bottom));
        var hi = Screens.Strip(Left150, 120);
        Assert.Equal(1392 - 180, hi.Top);
    }

    [Fact] public void Real_machine_has_one_primary_monitor()
    {
        var all = Screens.All();
        Assert.NotEmpty(all);
        Assert.Single(all, s => s.Primary);
        Assert.All(all, s => Assert.StartsWith(@"\\.\", s.Key));
    }
}
