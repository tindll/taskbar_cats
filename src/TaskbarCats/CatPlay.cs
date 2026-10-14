namespace TaskbarCats;

/// What a cat draws beside itself, other than its toy and kittens.
public enum Prop { None, Cheese, Flyer, Butterfly }

/// v4: the busy cat acts out Mood's activity with the toy Pursuits gave it, plus props and kittens. Pure logic, no WPF.
/// The toy is the filler between tool calls (Activity.Toy; a mouse cat chases exactly as before). Tool poses keep
/// their own props; while one runs the toy waits: the mouse scurries close by, the yarn stops, the bird perches.
public sealed partial class Cat
{
    bool _yarnAway;
    double _lo, _hi = 1000, _age, _wiggle;

    /// Mood.Activity, set by Herd each poll. Only used while busy.
    public Activity Activity { get; set; }
    /// Pursuits' toy, set by Herd each poll.
    public Toy Toy { get; set; } = Toy.Mouse;
    /// Tune.Speed of the cat's trait: chases are this much faster or slower.
    public double Speed { get; set; } = 1;
    /// Running sub-agents (Mood.Kittens).
    public int Kittens { get; set; }

    public YarnBall? Yarn { get; private set; }
    public Bird? Bird { get; private set; }
    public Prop Prop { get; private set; }
    public double PropX { get; private set; }
    public double PropLift { get; private set; }
    public int PropFrame { get; private set; }
    public bool PropFacingRight { get; private set; } = true;

    bool Supervising => _status == SessionStatus.Working && Activity == Activity.Supervise;
    /// Left edge of kitten i's 24 DIP box: trailing behind the cat, or playing in front of it while it supervises.
    public double KittenX(int i) => Math.Clamp(Supervising
        ? X + (FacingRight ? Size * 0.75 + Tune.KittenGap * i : -Tune.KittenGap * (i + 1))
        : X + (FacingRight ? -Tune.KittenGap * (i + 1) : Size * 0.75 + Tune.KittenGap * i), _lo, _hi + Size / 2);
    public double KittenLift(int i) => Supervising ? Tune.KittenHop * Math.Abs(Math.Sin(_age * 4 + i * 1.3)) : 0;
    public bool KittensMoving => Anim is Anim.Walk or Anim.Run or Anim.Sniff;

    void OnStatus(SessionStatus value)
    {
        if (value != SessionStatus.Working) ClearToys();
        else HasPresent = false;   // back to work: the fish was not collected
        StopActs();
    }

    void ClearToys()
    {
        HasMouse = false; Yarn = null; Bird = null; Prop = Prop.None; _yarnAway = false; _fetch = false; _fetchDir = 0;
    }

    /// x ahead of the cat by `ahead` DIP, or behind it when that is off the strip.
    double Near(double ahead)
    {
        double x = X + (FacingRight ? ahead : -ahead);
        if (x < _lo || x > _hi) x = X - (FacingRight ? ahead : -ahead);
        return Math.Clamp(x, _lo, _hi);
    }

    void SetProp(Prop p, double x, double lift, int frame, bool facingRight = true)
    {
        Prop = p; PropX = x; PropLift = lift; PropFrame = frame; PropFacingRight = facingRight;
    }

    void TickToys(double dt, World w)
    {
        _age += dt;
        Prop = Prop.None;
        if (Toy == Toy.Yarn || Activity == Activity.Bat || _fetch)   // a thrown ball stays until the cat has it
        {
            if (Yarn is null && !_yarnAway) Yarn = new YarnBall(Near(Tune.YarnStart));
            Yarn?.Step(dt, _lo, _hi);   // v5: it rolls to rest when the mood moves on, never freezes mid-roll
        }
        else Yarn = null;
        if (Toy == Toy.Bird) (Bird ??= new Bird(Near(Tune.BirdStart), _rng)).Step(dt, X, _lo, _hi);
        else Bird = null;
        if (Toy == Toy.Mouse) TickMouseToy(dt, w);
        else HasMouse = false;
    }

    void TickMouseToy(double dt, World w)
    {
        if (_act == Act.Catch && _actLeft > 0) { HasMouse = true; _mouseT += dt; return; }   // pinned under the paw
        if (Activity is Activity.Rest or Activity.Nap or Activity.Limit)
        {
            HasMouse = true; _mouseT += dt;                       // nibbling beside the cat; MouseFrame animates
            MouseX = Near(Tune.MouseBeside); _mouseDir = MouseX >= X ? 1 : -1;
            if (Activity != Activity.Limit) SetProp(Prop.Cheese, Math.Clamp(MouseX + _mouseDir * Tune.CheeseGap, _lo, _hi), 0, 0);
            return;
        }
        TickMouse(dt, w);
    }

    void TickBusy(double dt, World w)
    {
        Bubble = Bubble.None;
        switch (Activity)
        {
            case Activity.Toy when Toy == Toy.Mouse: TickWorking(dt, w); return;
            case Activity.Toy when Toy == Toy.Yarn:
            case Activity.Bat: TickYarn(dt, w); return;
            case Activity.Toy: TickStalk(dt, w); return;
            case Activity.Sniff: Stroll(dt); return;
            case Activity.Watch:
                Hold(Anim.LookUp);
                if (Toy != Toy.Bird)   // a bird cat watches its own bird instead
                    SetProp(Prop.Flyer, Math.Clamp(X + Tune.FlyerSwing * Math.Sin(_t * 0.9), _lo, _hi), Tune.FlyerLift,
                        (int)(_t * 8) % 2, Math.Cos(_t * 0.9) > 0);
                return;
            case Activity.Butterfly:
                Hold(Anim.LookUp);
                SetProp(Prop.Butterfly, Math.Clamp(Near(Tune.ButterflyAhead) + 12 * Math.Sin(_t * 1.3), _lo, _hi),
                    Tune.ButterflyLift + 8 * Math.Sin(_t * 3), (int)(_t * 5) % 2);
                return;
            case Activity.Supervise:
                if (Anim != Anim.Sit) Begin(Anim.Sit, 0);
                SitFrame(dt);
                return;
            case Activity.Box: Hold(Anim.Box); return;
            case Activity.Rest: FaceToy(); Hold(Anim.Loaf); return;
            default: FaceToy(); Hold(Anim.Sleep); return;   // Nap, Limit: no Zzz while busy, so it never looks idle
        }
    }

    void Hold(Anim a)
    {
        if (Anim != a) Begin(a, 0);
        Loop();
    }

    void FaceToy()
    {
        double? at = Toy switch
        {
            Toy.Mouse => HasMouse ? MouseX : null,
            Toy.Yarn => Yarn?.X,
            _ => Bird?.X,
        };
        if (at is double x && Math.Abs(x - X) > 1) FacingRight = x > X;
    }

    /// Read/Grep/Glob: walk slowly, nose down behind the magnifier, turning at the strip ends.
    void Stroll(double dt)
    {
        Hold(Anim.Sniff);
        if (_pinned) return;
        X += (FacingRight ? 1 : -1) * Tune.SniffSpeed * dt;
        if (X <= _lo) { X = _lo; FacingRight = true; }
        if (X >= _hi) { X = _hi; FacingRight = false; }
    }

    /// Yarn: run to the ball, bat it along the strip, run after it.
    void TickYarn(double dt, World w)
    {
        if (Anim == Anim.Bat && _t < Tune.BatTime) { Loop(); return; }   // finish the swipe
        if (Yarn is not { } y || _pinned)
        {
            FaceToy();
            if (Anim != Anim.Sit) Begin(Anim.Sit, 0);
            SitFrame(dt);
            return;
        }
        double gap = y.X - X;
        FacingRight = gap >= 0;
        if (Math.Abs(gap) <= Tune.YarnReach && (Math.Abs(y.Vx) < Tune.YarnBat / 2 || Coming(y, gap)))
        {
            Begin(Anim.Bat, Tune.BatTime);
            // A ball against a strip end is scooped out of the corner, not batted into the wall again.
            int dir = y.X >= _hi - Tune.YarnReach ? -1 : y.X <= _lo + Tune.YarnReach ? 1 : FacingRight ? 1 : -1;
            y.Push(dir * Tune.YarnBat * (0.7 + 0.6 * _rng.NextDouble()));
            return;
        }
        if (Anim != Anim.Run) Begin(Anim.Run, 0);
        X += Math.Sign(gap) * Math.Min(Tune.YarnRun * Speed * dt, Math.Abs(gap));
        Loop();
    }

    /// The ball rolls towards the cat (gap = ball X - cat X): it gets batted back, never rolls through the cat.
    static bool Coming(YarnBall y, double gap) => y.Vx != 0 && Math.Sign(y.Vx) == -Math.Sign(gap);

    /// Bird: walk up to it, crouch and wiggle, pounce. It is always gone by the time the cat lands.
    void TickStalk(double dt, World w)
    {
        var b = Bird!;
        double gap = b.X - X;
        FacingRight = gap >= 0;
        if (b.Flying || _pinned || _pounceCd > 0)
        {
            if (Anim != Anim.Sit) Begin(Anim.Sit, 0);
            SitFrame(dt);
            return;
        }
        if (Math.Abs(gap) > Tune.StalkRange)
        {
            if (Anim != Anim.Walk) Begin(Anim.Walk, 0);
            X += Math.Sign(gap) * Math.Min(WalkSpeed * dt, Math.Abs(gap));
            Loop();
            return;
        }
        StartPounce(b.X, w);
        _wiggle = Tune.WiggleMin + (Tune.WiggleMax - Tune.WiggleMin) * _rng.NextDouble();
    }
}
