namespace TaskbarCats;

/// What the cats chase in play mode, as one strip sees it (strip DIP): X along the strip, Height above the cats'
/// feet (negative = down on the taskbar).
public readonly record struct Lure(double X, double Height);

/// How a cat answers a lure at some height: sit and watch, run underneath it, or run underneath and jump for it.
public enum Reach { Watch, Run, Jump }

/// The teaser wand's play mode (the cursor is the lure). Pure rules; Herd owns the switch and the clock.
public static class Teaser
{
    /// The lure for the strip of one monitor, from the cursor in that strip's DIP. Null when the cursor is on another
    /// monitor (those cats carry on as normal) or could not be placed.
    public static Lure? For(bool onThisMonitor, double dipX, double dipY, double band) =>
        onThisMonitor && !double.IsNaN(dipX) && !double.IsNaN(dipY) ? new Lure(dipX, band - dipY) : null;

    /// Play mode ends by itself, so the cats do not chase forever.
    public static bool Expired(double since, double now) => now - since >= Tune.PlayMax;

    public static Reach ReachOf(double height) =>
        height > Tune.PlayReach ? Reach.Watch : height > Tune.JumpReach ? Reach.Run : Reach.Jump;

    /// How high a jump goes: as high as the lure, within what a cat can do (and what fits on the strip).
    public static double Peak(double height) => Math.Clamp(height, Tune.JumpMin, Tune.JumpMax);
}
