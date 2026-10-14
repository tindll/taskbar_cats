namespace TaskbarCats;

/// A small bubble shown only when the cat has nothing more important to say (Bubble.None),
/// so MEOW, "!" and Zzz are never hidden.
public enum Murmur { None, Mrrp, Wonder, Heart, Oops, Achoo, Limit }

/// v4: one-off reactions to conversation events (Mood's cues), petting, the fish present. Pure logic.
public sealed partial class Cat
{
    enum Act { None, Catch, Slip, Sneeze, Startle, Nuzzle }

    Act _act;
    double _actLeft, _actTime, _murmurLeft, _zoom, _petCd;
    bool _catchOnLand;

    public Murmur Murmur { get; private set; }
    public bool HasPresent { get; private set; }
    public double PresentX { get; private set; }

    public void React(Cue cue)
    {
        switch (cue)
        {
            case Cue.Mrrp: Mutter(Murmur.Mrrp); break;
            case Cue.Wonder: Mutter(Murmur.Wonder); break;
            case Cue.Sneeze: if (StartAct(Act.Sneeze, Anim.Sneeze, Tune.SneezeTime)) Mutter(Murmur.Achoo); break;
            case Cue.Startle: if (StartAct(Act.Startle, Anim.Hiss, Tune.StartleTime)) Mutter(Murmur.Oops); break;
            case Cue.Slip:
                if (!StartAct(Act.Slip, Anim.Slip, Tune.SlipTime)) break;
                Mutter(Murmur.Oops);
                Escape();
                break;
            case Cue.Catch: Catch(); break;
            case Cue.Reward: Reward(); break;
        }
    }

    public void Pet()
    {
        if (_mode != Mode.Live || _petCd > 0) return;
        _petCd = Tune.PetCooldown;
        Mutter(Murmur.Heart);
    }

    public bool TakePresent()
    {
        if (!HasPresent) return false;
        HasPresent = false;
        return true;
    }

    void Mutter(Murmur m) { Murmur = m; _murmurLeft = Tune.MurmurTime; }

    void TickMurmur(double dt)
    {
        _petCd -= dt;
        if (_mode is Mode.Gone or Mode.Binned) { Murmur = Murmur.None; return; }
        if (_status == SessionStatus.Working && Activity == Activity.Limit) { Murmur = Murmur.Limit; return; }
        if ((_murmurLeft -= dt) <= 0) Murmur = Murmur.None;
    }

    /// One-shot pose. Refused while held, binned, hopping, mid-pounce or eating.
    bool StartAct(Act a, Anim anim, double seconds)
    {
        if (_mode != Mode.Live || Committed) return false;
        BeginAct(a, anim, seconds);
        return true;
    }

    void BeginAct(Act a, Anim anim, double seconds)
    {
        _act = a; _actLeft = _actTime = seconds;
        Begin(anim, seconds);
    }

    /// Held, or the status changed: any reaction, zoomies or pending catch ends.
    void StopActs() { _act = Act.None; _actLeft = 0; _zoom = 0; _catchOnLand = false; _greet = null; EndTag(); _roam = 0; }

    void TickAct(double dt)
    {
        _actLeft -= dt;
        Bubble = StatusBubble();
        double p = Math.Clamp(1 - _actLeft / _actTime, 0, 1);
        Lift = _act == Act.Startle ? Tune.StartleArc * Math.Sin(Math.PI * p) : 0;
        if (_act == Act.Catch) Frame = 2; else Once();
        if (_actLeft > 0) return;
        if (_act == Act.Catch && HasMouse) { _mouseDir = FacingRight ? 1 : -1; _dash = DashTime; }   // let go: it runs for it
        _act = Act.None; Lift = 0;
        Begin(StatusAnim(), 1.0);
    }

    Bubble StatusBubble() => _status switch
    {
        SessionStatus.Waiting => Bubble.Bang,
        SessionStatus.Idle when _asleep => Bubble.Zzz,
        SessionStatus.Idle => !Acknowledged && (Quiet || _meow % MeowPeriod < MeowOn) ? Bubble.Meow : Bubble.None,
        _ => Bubble.None,
    };

    void Catch()
    {
        if (_status != SessionStatus.Working || _mode != Mode.Live || Committed || _actLeft > 0) return;
        switch (Toy)
        {
            case Toy.Mouse when HasMouse && Activity == Activity.Toy && !_pinned && Math.Abs(MouseX - X) <= Tune.CatchRange:
                FacingRight = MouseX >= X;
                _leapFrom = X; _leapTo = Math.Clamp(MouseX - (FacingRight ? ToyGap : -ToyGap), _lo, _hi); _wiggle = 0;   // land ToyGap short of it
                _catchOnLand = true;
                Begin(Anim.Crouch, CrouchTime);
                return;
            case Toy.Yarn when Yarn is { } y:
                FacingRight = y.X >= X;
                y.Push((FacingRight ? 1 : -1) * Tune.YarnBat * 2);
                return;
            case Toy.Bird when Bird is { } b:
                b.Flee(X, _lo, _hi);
                return;
        }
        Mutter(Murmur.Mrrp);
    }

    /// Called as a pounce lands. True when it was a catch: the mouse is pinned under a front paw for a moment.
    bool Landed()
    {
        if (!_catchOnLand) return false;
        _catchOnLand = false;
        if (_status != SessionStatus.Working || Toy != Toy.Mouse) return false;
        HasMouse = true;
        MouseX = Near(ToyGap); _mouseDir = MouseX >= X ? 1 : -1;   // pinned in front of the nose, never under the face
        BeginAct(Act.Catch, Anim.Bat, Tune.CatchTime);
        return true;
    }

    void Escape()
    {
        if (HasMouse) { _mouseDir = MouseX >= X ? 1 : -1; _dash = DashTime * 2; }
        if (Yarn is { } y) y.Push((y.X >= X ? 1 : -1) * Tune.YarnBat * 2);
        Bird?.Flee(X, _lo, _hi);
    }

    /// The user clicked the bird: it flies off to another perch (the cat reacts as it does when the bird flees).
    public bool ScareBird() => _status == SessionStatus.Working && Bird?.Scare(X, _lo, _hi) == true;

    void Reward()
    {
        if (_mode != Mode.Live) return;
        HasPresent = true;
        PresentX = Near(Size);
        _zoom = Tune.ZoomiesTime;
    }

    /// After a reward: a few seconds of running back and forth. The idle bubble (MEOW) keeps blinking.
    bool Zoomies(double dt)
    {
        if (_zoom <= 0 || _pinned) return false;
        _zoom -= dt;
        if (Anim != Anim.Run) Begin(Anim.Run, 0);
        X += (FacingRight ? 1 : -1) * Tune.ZoomiesSpeed * dt;
        if (X <= _lo) { X = _lo; FacingRight = true; }
        else if (X >= _hi) { X = _hi; FacingRight = false; }
        Loop();
        if (_zoom <= 0) Begin(Anim.Sit, 1.5);
        return true;
    }
}
