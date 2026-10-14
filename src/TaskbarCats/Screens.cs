using System.Runtime.InteropServices;

namespace TaskbarCats;

/// One monitor, in physical pixels. Key = device name (\\.\DISPLAY1), stable while the monitor stays plugged in.
public sealed record Screen(string Key, Native.RECT Bounds, Native.RECT Work, bool Primary, double Scale);

public static class Screens
{
    /// Every monitor, from Windows. Needs the PerMonitorV2 manifest for Scale to be the real per-monitor value.
    public static List<Screen> All()
    {
        var list = new List<Screen>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(m, ref mi)) return true;
            double scale = Native.GetDpiForMonitor(m, 0 /*MDT_EFFECTIVE_DPI*/, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            list.Add(new Screen(mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, scale));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Screen Primary(IReadOnlyList<Screen> all) => all.FirstOrDefault(s => s.Primary) ?? all[0];

    /// The monitor containing a physical-pixel point, or null (between monitors).
    public static Screen? At(IReadOnlyList<Screen> all, int x, int y) =>
        all.FirstOrDefault(s => x >= s.Bounds.Left && x < s.Bounds.Right && y >= s.Bounds.Top && y < s.Bounds.Bottom);

    /// The cat strip for a monitor, in physical pixels: full work-area width, bandDip tall, on the work-area bottom
    /// (just above that monitor's taskbar, or on the screen edge when it has none).
    public static Native.RECT Strip(Screen s, double bandDip) => new()
    {
        Left = s.Work.Left, Right = s.Work.Right, Bottom = s.Work.Bottom,
        Top = s.Work.Bottom - (int)Math.Round(bandDip * s.Scale),
    };
}
