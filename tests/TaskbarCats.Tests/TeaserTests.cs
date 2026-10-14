using TaskbarCats;
using Xunit;

public class TeaserTests
{
    [Fact] public void Lure_is_the_cursor_in_strip_dips_with_height_above_the_cats_feet()
    {
        var l = Teaser.For(true, 300, 20, OverlayWindow.BandHeight)!.Value;
        Assert.Equal(300, l.X);
        Assert.Equal(OverlayWindow.BandHeight - 20, l.Height);
        Assert.True(Teaser.For(true, 300, OverlayWindow.BandHeight + 24, OverlayWindow.BandHeight)!.Value.Height < 0);   // on the taskbar
    }

    [Fact] public void Cursor_on_another_monitor_or_off_the_strip_is_no_lure()
    {
        Assert.Null(Teaser.For(false, 300, 20, 120));
        Assert.Null(Teaser.For(true, double.NaN, double.NaN, 120));
    }

    [Fact] public void Play_stops_by_itself_after_the_limit()
    {
        Assert.False(Teaser.Expired(10, 10 + Tune.PlayMax - 0.1));
        Assert.True(Teaser.Expired(10, 10 + Tune.PlayMax));
    }

    [Theory]
    [InlineData(-20, Reach.Jump)]
    [InlineData(0, Reach.Jump)]
    [InlineData(Tune.JumpReach, Reach.Jump)]
    [InlineData(Tune.JumpReach + 1, Reach.Run)]
    [InlineData(Tune.PlayReach, Reach.Run)]
    [InlineData(Tune.PlayReach + 1, Reach.Watch)]
    public void Reach_by_height(double height, Reach expected) => Assert.Equal(expected, Teaser.ReachOf(height));

    [Fact] public void Jump_height_follows_the_lure_up_to_a_maximum()
    {
        Assert.Equal(Tune.JumpMin, Teaser.Peak(-30));
        Assert.Equal(Tune.JumpMin, Teaser.Peak(0));
        Assert.Equal(40, Teaser.Peak(40));
        Assert.Equal(Tune.JumpMax, Teaser.Peak(Tune.JumpReach));
    }

    [Fact] public void The_jump_stays_inside_the_strip() =>
        // The top of a jumping cat (band - size - peak) must stay on the strip, or the jump is clipped.
        Assert.True(OverlayWindow.BandHeight - Cat.Size - Tune.JumpMax >= 0);
}
