using TaskbarCats;
using Xunit;

public class NativeTests
{
    static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    static readonly Native.RECT Mon = R(0, 0, 1920, 1080);

    [Fact] public void IsFullscreen_exact_cover() => Assert.True(Native.IsFullscreen(R(0, 0, 1920, 1080), Mon));
    [Fact] public void IsFullscreen_overhanging_cover() => Assert.True(Native.IsFullscreen(R(-8, -8, 1928, 1088), Mon));
    [Fact] public void IsFullscreen_maximized_with_taskbar_is_not() => Assert.False(Native.IsFullscreen(R(-8, -8, 1928, 1040), Mon));
    [Fact] public void IsFullscreen_small_window_is_not() => Assert.False(Native.IsFullscreen(R(100, 100, 800, 600), Mon));
}
