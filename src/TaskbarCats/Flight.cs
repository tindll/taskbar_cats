namespace TaskbarCats;

/// A thrown (or let-go) cat in the air, in physical screen pixels: X, Y = centre of the 48 DIP sprite.
/// Pure logic, no WPF. Herd advances it every frame and draws the cat in its own small window until Landed.
/// Walls and ceiling: the cat bounces off any edge with no monitor beyond it, so it can fly onto a neighbour
/// monitor. Floor: the taskbar edge (work-area bottom) of the monitor under the cat. The yarn ball bounces on the floor (floorBounce); a cat always lands on its feet.
public sealed class Flight
{
    // Tuning knobs, all in DIP (multiplied by the monitor's DPI scale at each step).
    public const double Gravity = 2500;        // DIP/s²: a fall from the top of a 1080 px screen takes about 0.9 s
    public const double Bounce = 0.5;          // speed kept after hitting a wall or the ceiling
    public const double MaxSpeed = 2500;       // DIP/s: a wild fling is capped (no tunnelling: one step < half a cat)
    public const double ThrowScale = 1.0;      // pointer speed -> cat speed
    public const double SampleWindow = 0.1;    // s of pointer history used for the release speed
    public const double SwingSpeed = 150;      // DIP/s of pointer speed before a carried cat swings
    public const double ScruffY = 2.0 / 16;    // the pointer holds the cat here (sprite row 2, the scruff)
    public const double Step = 1 / 120.0;      // fixed physics step, s

    double _acc;
    readonly double _floorBounce;

    /// x, y = sprite centre (px); vx, vy = pointer speed at release (px/s); facingRight = the cat's facing before.
    /// floorBounce = vertical speed kept off the taskbar (0: lands at the first touch, like a cat; the yarn ball bounces).
    public Flight(double x, double y, double vx, double vy, Screen start, bool facingRight, double floorBounce = 0)
    {
        X = x; Y = y; Screen = start; FacingRight = facingRight; _floorBounce = floorBounce;
        double sx = vx * ThrowScale, sy = vy * ThrowScale, v = Math.Sqrt(sx * sx + sy * sy), cap = MaxSpeed * start.Scale;
        double k = v > cap ? cap / v : 1;
        Vx = sx * k; Vy = sy * k;
        if (Math.Abs(Vx) > 1) FacingRight = Vx > 0;
        Land();   // let go on (or below) the taskbar: lands at once
    }

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Vx { get; private set; }
    public double Vy { get; private set; }
    public Screen Screen { get; private set; }
    public bool Landed { get; private set; }
    public double LandVx { get; private set; }      // sideways speed (px/s) at touchdown: a thrown yarn ball keeps rolling
    public double LandVy { get; private set; }      // downward speed (px/s) at the final touchdown: a hard landing
    public double Aloft { get; private set; }        // s in the air so far (the ball spins with it)
    public bool FacingRight { get; private set; }   // the way it flies; kept after a bounce until it moves the other way

    /// Moves the cat by dt seconds in fixed steps. screens = every monitor (Screens.All()).
    public void Advance(double dt, IReadOnlyList<Screen> screens)
    {
        _acc += dt;
        while (_acc >= Step && !Landed)
        {
            _acc -= Step;
            StepOnce(screens);
        }
    }

    void StepOnce(IReadOnlyList<Screen> screens)
    {
        Aloft += Step;
        double s = Screen.Scale, h = Cat.Size / 2 * s;
        Vy += Gravity * s * Step;
        X += Vx * Step;
        Y += Vy * Step;
        if (At(screens, X, Y) is { } here) Screen = here;
        var b = Screen.Bounds;
        // A wall is an edge with no monitor beyond it: probe the leading edge of the sprite.
        if (Vx < 0 && At(screens, X - h, Y) is null) { X = Math.Max(X, b.Left + h); Vx = -Vx * Bounce; }
        if (Vx > 0 && At(screens, X + h, Y) is null) { X = Math.Min(X, b.Right - h); Vx = -Vx * Bounce; }
        if (Vy < 0 && At(screens, X, Y - h) is null) { Y = Math.Max(Y, b.Top + h); Vy = -Vy * Bounce; }
        if (Math.Abs(Vx) > 1) FacingRight = Vx > 0;
        Land();
    }

    // Floor, not a cast: (int)-0.5 is 0, which would put a point just off the left edge back on screen.
    static Screen? At(IReadOnlyList<Screen> screens, double x, double y) => Screens.At(screens, (int)Math.Floor(x), (int)Math.Floor(y));

    void Land()
    {
        double h = Cat.Size / 2 * Screen.Scale;
        if (Vy < 0 || Y + h < Screen.Work.Bottom) return;
        Y = Screen.Work.Bottom - h;
        if (Vy * _floorBounce > Tune.YarnBounceMin * Screen.Scale)
        {
            Vy = -Vy * _floorBounce; Vx *= Tune.YarnGroundKeep;   // both < 1: a bounce never gains energy
            return;
        }
        LandVx = Vx; LandVy = Vy;
        Vx = Vy = 0;
        Landed = true;
    }

    /// Pointer speed (px/s) from samples (t in s, x and y in px), oldest first: the movement over the last
    /// SampleWindow seconds. Fewer than two samples, or no time between them: (0, 0).
    public static (double Vx, double Vy) Velocity(IReadOnlyList<(double T, double X, double Y)> samples)
    {
        if (samples.Count < 2) return (0, 0);
        var last = samples[^1];
        var first = samples.LastOrDefault(p => last.T - p.T >= SampleWindow - 1e-9, samples[0]);   // 0.7 - 0.6 < 0.1 in doubles
        double dt = last.T - first.T;
        return dt <= 1e-3 ? (0, 0) : ((last.X - first.X) / dt, (last.Y - first.Y) / dt);
    }

    /// Dangle frame for a carried cat. vxDip = pointer speed in DIP/s. Still: a slow sway (frames 0, 1).
    /// Pointer moving right: the body lags behind, swinging left (2). Moving left: swinging right (3).
    public static int SwingFrame(double vxDip, double t) =>
        vxDip > SwingSpeed ? 2 : vxDip < -SwingSpeed ? 3 : (int)(t * 1000 / Cat.Timing[Anim.Dangle].Ms) % 2;

    /// Fly frame: legs spread while going up (0), legs reaching down for the landing while coming down (1).
    public int FlyFrame => Vy < 0 ? 0 : 1;
}

/// Something the pointer carries (a cat or the yarn ball), in physical px. It trails the pointer on a critically
/// damped spring (time constant Tune.CarryLag), so a drag has a little weight and lag and never wobbles; let go, it
/// is thrown with the pointer's recent speed (Flight.Velocity over Flight.SampleWindow). One model for cat and ball.
public sealed class Carry
{
    readonly List<(double T, double X, double Y)> _trail = [];
    double _tx, _ty;

    public Carry(double t, double x, double y) { X = _tx = x; Y = _ty = y; _trail.Add((t, x, y)); }

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Vx { get; private set; }   // its own speed, px/s (a dangling cat swings with it)
    public double Vy { get; private set; }

    /// A pointer sample: on every mouse move, every frame and at the release. A sample older than the last is ignored.
    public void Sample(double t, double x, double y)
    {
        if (t < _trail[^1].T) return;
        _trail.Add((t, x, y));
        _trail.RemoveAll(p => t - p.T > 1);
        _tx = x; _ty = y;
    }

    /// Moves towards the latest sample by dt seconds. Closed form: stable at any dt, no overshoot.
    public void Follow(double dt)
    {
        (X, Vx) = Damp(X, Vx, _tx, dt);
        (Y, Vy) = Damp(Y, Vy, _ty, dt);
    }

    /// Release speed (px/s): the pointer's movement over the last Flight.SampleWindow. A quick flick throws hard.
    public (double Vx, double Vy) Throw() => Flight.Velocity(_trail);

    // Critically damped spring (Game Programming Gems 4, "Critically Damped Ease-In/Ease-Out Smoothing").
    static (double X, double V) Damp(double x, double v, double target, double dt)
    {
        double w = 2 / Tune.CarryLag, k = w * dt, e = 1 / (1 + k + 0.48 * k * k + 0.235 * k * k * k);
        double d = x - target, tmp = (v + w * d) * dt;
        return (target + (d + tmp) * e, (v - w * tmp) * e);
    }
}
