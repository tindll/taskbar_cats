using TaskbarCats;
using Xunit;

public class DrawRulesTests
{
    [Fact] public void Murmurs_never_look_like_the_status_bubbles()
    {
        var texts = Enum.GetValues<Murmur>().Where(m => m != Murmur.None).Select(Tune.MurmurText).ToList();
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.All(texts, t => Assert.DoesNotContain(t, new[] { "!", "MEOW!", "good job !" }));
        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.Equal("", Tune.MurmurText(Murmur.None));
    }

    [Fact] public void Every_look_keeps_whole_screen_pixels()
    {
        foreach (var look in Enum.GetValues<Look>())
        {
            double perPixel = Cat.Size * Tune.LookScale(look) / 16;
            Assert.Equal(Math.Round(perPixel), perPixel, 9);
        }
        Assert.True(Tune.LookScale(Look.Kitten) < Tune.LookScale(Look.Cat));
        Assert.True(Tune.LookScale(Look.Big) > Tune.LookScale(Look.Cat));
        Assert.True(Cat.Size * Tune.LookScale(Look.Big) + 24 <= OverlayWindow.BandHeight);   // head and bubble fit the strip
    }

    [Fact] public void The_belly_grows_only_on_poses_with_a_torso()
    {
        Assert.DoesNotContain(Anim.Sniff, Tune.BellyAnims);
        Assert.DoesNotContain(Anim.Sleep, Tune.BellyAnims);
        foreach (var a in Tune.BellyAnims)
            foreach (var f in Sprites.Frames[a])
                Assert.NotSame(f, Sprites.Chubby(f, 1));
    }
}
