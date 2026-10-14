namespace TaskbarCats;

public enum Anim { Walk, Sit, Lick, Wash, Stretch, Yawn, Sleep, Run, PawUp, Hop, Hiss, Crouch, Leap, Dangle, Eat, Fly, Sniff, Bat, LookUp, Box, Pumpkin, Loaf, Sneeze, Nuzzle, Slip, Curl, Flat }
public enum Bubble { None, Meow, Bang, Zzz, GoodJob }
public enum ClickResult { None, Focus }

/// Width = strip width (DIP). CursorX = cursor x in strip DIP when the cursor is in the pounce band, else null.
/// MinX = left walking limit (the sardine can sits left of it on its strip).
public readonly record struct World(double Width, double? CursorX, IReadOnlyList<Cat> Cats, double MinX = 0);

/// One cat. Pure logic, no WPF: the overlay ticks it and draws Anim/Frame/X/Lift/FacingRight/Bubble.
public sealed partial class Cat
{
    public static readonly IReadOnlyDictionary<Anim, (int Frames, int Ms)> Timing = new Dictionary<Anim, (int, int)>
    {
        [Anim.Walk] = (6, 100), [Anim.Sit] = (4, 150), [Anim.Lick] = (4, 180), [Anim.Wash] = (4, 180),
        [Anim.Stretch] = (3, 250), [Anim.Yawn] = (3, 200), [Anim.Sleep] = (2, 800), [Anim.Run] = (6, 70),
        [Anim.PawUp] = (2, 400), [Anim.Hop] = (3, 167), [Anim.Hiss] = (2, 200),
        [Anim.Crouch] = (2, 100), [Anim.Leap] = (2, 250), [Anim.Dangle] = (4, 600), [Anim.Eat] = (2, 200), [Anim.Fly] = (2, 200),
        [Anim.Sniff] = (4, 150), [Anim.Bat] = (3, 150), [Anim.LookUp] = (2, 400),   // v4 tool poses
        [Anim.Box] = (2, 700), [Anim.Pumpkin] = (2, 700), [Anim.Loaf] = (2, 900),
        [Anim.Sneeze] = (2, 300), [Anim.Nuzzle] = (2, 500), [Anim.Slip] = (2, 200),
        [Anim.Curl] = (2, 800), [Anim.Flat] = (2, 800),   // v5 sleep poses: a slow breath, like Sleep
    };

    public const double Size = 48;            // 16 px sprite drawn x3
    public const double WalkSpeed = 30;       // DIP/s
    public const double HopTime = 0.5, HopArc = 16;
    public const double SleepAfter = 120;     // s of un-clicked MEOW
    public const double MeowOn = 1.2, MeowPeriod = 2.0;
    public const double ChaseSpeed = 75, MouseSpeed = 60, MouseDash = 150, DashTime = 0.8, CatchRange = 60, MouseLead = 120;
    /// Nearest the toy mouse may sit to the cat: its 48 DIP box plus 3 sprite px (9 DIP) of air, so it never covers the face.
    public const double ToyGap = Size + 9;
    public const double PounceRange = 80, PounceCooldown = 10, CrouchTime = 0.4, LeapTime = 0.5, LeapArc = 30;
    public const double FaceOffTime = 1.0, HissCooldown = 15;
    public const double PinnedMouseRange = 110, EatTime = 2, GoodJobTime = 3, BinHopFrom = 24, BinHopArc = 30;
    public const double LandTime = 0.3, BoostFactor = 2;   // landing crouch (s); chase speed x2 after the user helped the mouse

    enum Mode { HopOn, Live, HopOff, Gone, Held, Binned, Land }

    readonly Random _rng;
    Mode _mode;
    SessionStatus _status = SessionStatus.Idle;
    double _t;          // seconds since current Anim began
    double _left;       // seconds left in current idle behaviour
    double _meow;       // seconds of un-acknowledged MEOW
    double _mouseDir = 1, _dash, _mouseT;
    double _twitchIn = 3, _twitchLeft;
    int _twitchFrame;
    bool _asleep;
    Anim _sleepPose = Anim.Sleep;   // one of Tune.SleepPoses, picked each time the cat dozes off
    double _pounceCd, _hissCd, _leapFrom, _leapTo;
    bool _faceOff;
    double _hopFrom, _hopTo, _hopTime, _hopArc;   // generic hop: Lift goes from -> to over hopTime with a sine arc
    Bubble _say;
    double _sayLeft;
    bool _pinned;
    bool _boost;
    double _landTime = LandTime;   // this landing's crouch (or dizzy slip) length

    public Cat(string id, double x, Random rng)
    {
        Id = id; X = x; _rng = rng;
        StartHop(Mode.HopOn, -Size, 0, HopTime, HopArc);
    }

    public string Id { get; }
    public double X { get; private set; }
    public double Lift { get; private set; }          // DIP above the taskbar edge; negative = below it (clipped)
    public bool FacingRight { get; private set; } = true;
    public Anim Anim { get; private set; }
    public int Frame { get; private set; }
    public Bubble Bubble { get; private set; }
    public bool Acknowledged { get; private set; }
    public bool IsAsleep => _asleep;
    public bool IsLive => _mode == Mode.Live;
    public bool IsGone => _mode == Mode.Gone;
    public bool IsHeld => _mode == Mode.Held;
    public bool IsBinned => _mode == Mode.Binned;
    /// Sprinting back to a mouse the user gave a head start, until the next pounce.
    public bool Boosting => _boost;
    /// The project's "Quiet" choice (right-click menu): the MEOW stays up without blinking, and murmurs are not drawn.
    public bool Quiet { get; set; }

    /// "Stay here": no wandering, no pouncing. Idle + news = sit and MEOW; idle + seen = nap; busy = watch the mouse.
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (value == _pinned) return;
            _pinned = value;
            _faceOff = false;
            if (_mode == Mode.Live && !Committed) Begin(StatusAnim(), 1.0);
        }
    }

    /// The mouse a working cat chases. Same 48 DIP box as the cat; the sprite faces right.
    public bool HasMouse { get; private set; }
    public double MouseX { get; private set; }
    public bool MouseFacingRight => _mouseDir > 0;
    public int MouseFrame => (int)(_mouseT * 10) % 2;

    /// A pounce or a meal is a committed move: status changes wait until it ends. A crouch in a game of tag is not one.
    bool Committed => _mode == Mode.Live && _tag is null && Anim is Anim.Crouch or Anim.Leap or Anim.Eat;

    public SessionStatus Status
    {
        get => _status;
        set
        {
            if (value == _status) return;
            if (value == SessionStatus.Idle) { Acknowledged = false; _meow = 0; }   // task finished: fresh MEOW
            _asleep = false;
            _faceOff = false;
            _boost = false;
            OnStatus(value);   // toys and props go when work stops, also while held (CatPlay.cs)
            _status = value;
            if (_mode == Mode.Live && !Committed) Begin(StatusAnim(), 1.0);
        }
    }

    Anim StatusAnim() => _status switch { SessionStatus.Working => Anim.Run, SessionStatus.Waiting => Anim.PawUp, _ => Anim.Sit };

    /// Hop off from wherever the cat is now (mid hop-on, mid leap): no teleport back to the ground first.
    /// A held or binned cat is not on the strip, so it just goes.
    public void Despawn()
    {
        if (_mode is Mode.Held or Mode.Binned) { _mode = Mode.Gone; return; }
        if (_mode is Mode.HopOn or Mode.Live or Mode.Land) { StartHop(Mode.HopOff, Lift, -Size, HopTime, HopArc); Bubble = Bubble.None; _sayLeft = 0; }
    }

    /// Picked up by the mouse pointer. False when the cat is leaving or already gone.
    /// A busy cat's mouse stays where it is (frozen) while the cat is held, so the user can give it a head start.
    public bool Grab()
    {
        if (_mode is not (Mode.HopOn or Mode.Live or Mode.Land)) return false;
        _mode = Mode.Held; Bubble = Bubble.None; _faceOff = false; _boost = false;
        if (_fetchDir != 0) _yarnAway = false;   // a trip to a ball on another monitor is off: it gets a fresh ball
        _fetch = false; _fetchDir = 0; _chasing = false;
        Begin(Anim.Dangle, 0);
        return true;
    }

    /// Back on the strip after a throw, at x (strip DIP), facing the way it flew: a short landing crouch
    /// (always on its feet), then it carries on. keepMouse false (it landed on another monitor): the mouse starts
    /// afresh. A busy cat that kept its mouse sprints (BoostFactor) until it is close enough to pounce again.
    /// hard = it came down faster than Tune.HardLanding: a dizzy slip with "!?" instead of the crouch.
    public void Land(double x, bool facingRight, bool keepMouse = true, bool hard = false)
    {
        if (_mode != Mode.Held) return;
        X = x; FacingRight = facingRight; Lift = 0;
        if (!keepMouse) ClearToys();
        _boost = HasMouse;
        _mode = Mode.Land;
        _landTime = hard ? Tune.HardLandTime : LandTime;
        Begin(hard ? Anim.Slip : Anim.Crouch, _landTime);
        if (hard) Mutter(Murmur.Oops);
    }

    /// Dropped into the bin: off screen until Unbin.
    public void Bin()
    {
        if (_mode != Mode.Held) return;
        _mode = Mode.Binned; ClearToys(); Bubble = Bubble.None; _sayLeft = 0;
    }

    /// Something new happened in the conversation: hop out of the bin (at x) and roam again.
    public void Unbin(double x)
    {
        if (_mode != Mode.Binned) return;
        X = x; _pinned = false;
        StartHop(Mode.HopOn, BinHopFrom, 0, HopTime, BinHopArc);
    }

    /// Given a sardine: eat it and say "good job !".
    public void Feed()
    {
        if (_mode == Mode.Live) { EndTag(); HasMouse = false; Begin(Anim.Eat, EatTime); }
        Say(Bubble.GoodJob, GoodJobTime);
    }

    /// Show a bubble for a few seconds, over whatever the cat would normally say.
    public void Say(Bubble b, double seconds) { _say = b; _sayLeft = seconds; }

    public ClickResult Click()
    {
        if (_mode != Mode.Live) return ClickResult.None;
        if (_status == SessionStatus.Idle && _asleep) { _asleep = false; _meow = 0; Begin(Anim.Sit, 1.0); return ClickResult.None; }
        if (_status == SessionStatus.Idle) Acknowledged = true;
        return ClickResult.Focus;
    }

    internal void StartWalk(bool right, double seconds) { FacingRight = right; Begin(Anim.Walk, seconds); }

    public void Tick(double dt, World w)
    {
        TickBody(dt, w);
        TickMurmur(dt);
        if (_sayLeft > 0 && _mode is not (Mode.Gone or Mode.Binned)) { _sayLeft -= dt; Bubble = _say; }
    }

    void TickBody(double dt, World w)
    {
        _t += dt; _pounceCd -= dt; _hissCd -= dt; _tagCd -= dt; _roamCd -= dt;
        if (_mode is Mode.Gone or Mode.Binned) return;
        if (_mode == Mode.Held) { Lift = 0; StopActs(); Loop(); return; }   // the mouse is not ticked: it waits where it was
        if (_mode == Mode.Land)
        {
            Lift = 0; Loop();
            if (_t >= _landTime) { _mode = Mode.Live; Begin(StatusAnim(), 1.0); }
            return;
        }
        if (_mode != Mode.Live) { TickHop(); return; }
        X = Math.Clamp(X, w.MinX, Math.Max(w.MinX, w.Width - Size));   // strip shrank or cat moved strips
        if (Anim == Anim.Eat) { TickEat(); return; }
        _lo = w.MinX; _hi = Math.Max(w.MinX, w.Width - Size);
        if (TickChase(dt)) return;   // play mode (CatChase.cs): replaces the toys and the normal behaviour
        if (_status == SessionStatus.Working) TickToys(dt, w); else ClearToys();
        if (Committed) { TickPounce(w); return; }
        if (_actLeft > 0) { TickAct(dt); return; }
        Lift = 0;
        switch (_status)
        {
            case SessionStatus.Working: if (!(_fetch && TickFetch(dt, w))) TickBusy(dt, w); break;
            case SessionStatus.Waiting: Bubble = Bubble.Bang; Anim = Anim.PawUp; Loop(); break;
            default: TickIdle(dt, w); break;
        }
    }

    void TickEat()
    {
        Lift = 0; Bubble = Bubble.None; Loop();
        if (_t >= EatTime) Begin(StatusAnim(), 1.0);
    }

    void StartHop(Mode mode, double from, double to, double seconds, double arc)
    {
        _mode = mode; _hopFrom = from; _hopTo = to; _hopTime = seconds; _hopArc = arc;
        Begin(Anim.Hop, seconds);
        Lift = from;
    }

    void TickHop()
    {
        double p = Math.Min(_t / _hopTime, 1);
        Lift = _hopFrom + (_hopTo - _hopFrom) * p + _hopArc * Math.Sin(Math.PI * p);
        Frame = Math.Min((int)(p * 3), 2);
        if (p < 1) return;
        if (_mode == Mode.HopOff) { _mode = Mode.Gone; return; }
        _mode = Mode.Live; Lift = 0;
        Begin(StatusAnim(), 1.0);
    }

    void TickPounce(World w)
    {
        if (_status == SessionStatus.Idle)
            Bubble = !Acknowledged && (Quiet || _meow % MeowPeriod < MeowOn) ? Bubble.Meow : Bubble.None;
        if (Anim == Anim.Crouch)
        {
            Lift = 0; Loop();
            if (_t >= CrouchTime + _wiggle) Begin(Anim.Leap, LeapTime);
            return;
        }
        double p = Math.Min(_t / LeapTime, 1);
        X = Math.Clamp(_leapFrom + (_leapTo - _leapFrom) * p, w.MinX, Math.Max(w.MinX, w.Width - Size));
        if (HasMouse && Math.Abs(MouseX - X) < ToyGap)   // the mouse moved into the landing: stop on the side we leapt from
            X = Math.Clamp(MouseX + (_leapFrom >= MouseX ? ToyGap : -ToyGap), w.MinX, Math.Max(w.MinX, w.Width - Size));
        Lift = LeapArc * Math.Sin(Math.PI * p);
        Once();
        if (p < 1) return;
        Lift = 0; _pounceCd = PounceCooldown;
        if (Landed()) return;   // a catch: the mouse is under the paw (CatCues.cs)
        if (_status == SessionStatus.Idle) Begin(Anim.Sit, 1.5); else Begin(StatusAnim(), 1.0);
    }

    /// The mouse runs back and forth along the strip; it sprints (MouseDash) for DashTime after the cat pounces.
    /// Next to a pinned cat it scurries within PinnedMouseRange instead.
    void TickMouse(double dt, World w)
    {
        double lo = w.MinX, hi = Math.Max(w.MinX, w.Width - Size);
        if (_pinned || Activity != Activity.Toy) { lo = Math.Max(lo, X - PinnedMouseRange); hi = Math.Max(lo, Math.Min(hi, X + PinnedMouseRange)); }
        if (!HasMouse)
        {
            HasMouse = true; _dash = 0;
            _mouseDir = FacingRight ? 1 : -1;
            MouseX = X + _mouseDir * MouseLead;
            if (MouseX < lo || MouseX > hi) { _mouseDir = -_mouseDir; MouseX = X + _mouseDir * MouseLead; }
            MouseX = Math.Clamp(MouseX, lo, hi);
        }
        _mouseT += dt;
        _dash -= dt;
        MouseX += _mouseDir * (_dash > 0 ? MouseDash : MouseSpeed) * dt;
        if (MouseX <= lo) { MouseX = lo; _mouseDir = 1; }
        if (MouseX >= hi) { MouseX = hi; _mouseDir = -1; }
        KeepOffFace(lo, hi);
    }

    /// The mouse never sits within ToyGap of the cat (its art would cover the face): shove it out to the side it is on,
    /// or the other side when a strip end is in the way.
    void KeepOffFace(double lo, double hi)
    {
        if (Math.Abs(MouseX - X) >= ToyGap) return;
        int side = MouseX > X || (MouseX == X && _mouseDir > 0) ? 1 : -1;
        double x = Math.Clamp(X + side * ToyGap, lo, hi);
        if (Math.Abs(x - X) < ToyGap) { side = -side; x = Math.Clamp(X + side * ToyGap, lo, hi); }
        MouseX = x; _mouseDir = side;
    }

    /// Busy = chasing the mouse: run at it, pounce when close, the mouse sprints off, repeat. Never caught.
    void TickWorking(double dt, World w)
    {
        Bubble = Bubble.None;
        double gap = MouseX - X;
        if (_pinned)
        {
            if (Anim != Anim.Sit) Begin(Anim.Sit, 0);
            FacingRight = gap >= 0;
            SitFrame(dt);
            return;
        }
        if (Math.Abs(gap) < CatchRange && _dash <= 0)
        {
            _boost = false;                    // caught up: back to normal speed after this pounce
            _dash = DashTime;
            _mouseDir = gap >= 0 ? 1 : -1;     // flee away from the cat
            StartPounce(MouseX - (gap >= 0 ? ToyGap : -ToyGap), w);   // land short: the mouse never ends up under the cat
            return;
        }
        if (Anim != Anim.Run) Begin(Anim.Run, 0);
        FacingRight = gap >= 0;
        X += Math.Sign(gap) * Math.Min(ChaseSpeed * Speed * (_boost ? BoostFactor : 1) * dt, Math.Max(0, Math.Abs(gap) - ToyGap));   // never closer than ToyGap
        Loop();
    }

    void TickIdle(double dt, World w)
    {
        if (_asleep) { Bubble = Bubble.Zzz; Anim = _sleepPose; Loop(); return; }
        if (!Acknowledged && (_meow += dt) >= SleepAfter)
        {
            EndTag(); _roam = 0;   // a cat that dozes off stops playing (its friend sees it asleep) and stays on this strip
            _asleep = true; _sleepPose = Tune.SleepPoses[_rng.Next(Tune.SleepPoses.Length)];
            Begin(_sleepPose, 0); Bubble = Bubble.Zzz;
            return;
        }
        Bubble = !Acknowledged && (Quiet || _meow % MeowPeriod < MeowOn) ? Bubble.Meow : Bubble.None;
        if (Zoomies(dt) || Roaming(dt) || Tagging(dt, w) || Greeting(dt)) return;   // a roam goes before EdgeX / MoveApart can stop it

        if (_pinned)
        {
            if (Acknowledged)
            {
                // ponytail: a nap, not _asleep, so one click still focuses the conversation
                if (Anim != _sleepPose) { _sleepPose = Tune.SleepPoses[_rng.Next(Tune.SleepPoses.Length)]; Begin(_sleepPose, 0); }
                Bubble = Bubble.Zzz; Loop();
            }
            else
            {
                if (Anim != Anim.Sit) Begin(Anim.Sit, 0);
                SitFrame(dt);
            }
            return;
        }
        if (_faceOff)
        {
            if (Anim == Anim.Hiss) Loop(); else Frame = 0;
            if ((_left -= dt) <= 0)
            {
                _faceOff = false; _hissCd = HissCooldown;
                StartWalk(!FacingRight, 2 + _rng.NextDouble() * 3);
            }
            return;
        }
        if (TryPounce(w)) return;
        if (Anim == Anim.Walk && TryFaceOff(w)) return;

        _left -= dt;
        if (Anim == Anim.Walk)
        {
            X += (FacingRight ? 1 : -1) * WalkSpeed * dt;
            if (X <= w.MinX) { X = w.MinX; FacingRight = true; }
            if (FacingRight && X >= EdgeX(w)) FacingRight = false;   // turn short of the tray, not at the very end
            if (X >= w.Width - Size) { X = Math.Max(w.MinX, w.Width - Size); FacingRight = false; }
        }
        if (_left <= 0)
        {
            if (Anim == Anim.Stretch) Begin(Anim.Yawn, 3 * 0.2 + 0.5);
            else PickBehaviour(w);
        }
        if (Anim == Anim.Sit) SitFrame(dt);
        else if (Anim is Anim.Stretch or Anim.Yawn) Once();
        else Loop();
    }

    bool TryPounce(World w)
    {
        if (_pinned || _pounceCd > 0 || w.CursorX is not double cx) return false;
        double centre = X + Size / 2;
        if (Math.Abs(cx - centre) > PounceRange) return false;
        StartPounce(cx - Size / 2, w);
        return true;
    }

    /// Crouch, then leap so the cat's left edge lands on targetX.
    void StartPounce(double targetX, World w)
    {
        FacingRight = targetX >= X;
        _leapFrom = X; _wiggle = 0;
        _leapTo = Math.Clamp(targetX, w.MinX, Math.Max(w.MinX, w.Width - Size));
        Begin(Anim.Crouch, CrouchTime);
    }

    bool TryFaceOff(World w)
    {
        if (_hissCd > 0) return false;
        foreach (var o in w.Cats)
        {
            if (ReferenceEquals(o, this) || !o.IsLive || o.Anim != Anim.Walk || o._hissCd > 0 || o._faceOff) continue;
            double dx = o.X - X;
            bool approaching = FacingRight == (dx > 0) && o.FacingRight == (dx < 0);
            if (!approaching || Math.Abs(dx) > Size * 0.75) continue;
            bool iHiss = string.CompareOrdinal(Id, o.Id) < 0;
            StartFaceOff(iHiss);
            o.StartFaceOff(!iHiss);
            return true;
        }
        return false;
    }

    void StartFaceOff(bool hiss) { _faceOff = true; Begin(hiss ? Anim.Hiss : Anim.Sit, FaceOffTime); }

    void PickBehaviour(World w)
    {
        if (MoveApart(w)) return;
        // The friend check comes first, so a cat without a friend draws the same random numbers as before.
        if (Friend is { IsLive: true } f)
        {
            double roll = _rng.NextDouble();
            if (roll < Tune.FriendChance) { Greet(f, nuzzle: false); if (_greet is not null) return; }
            else if (roll < Tune.FriendChance + Tune.TagChance && StartTag(f)) return;
        }
        if (TryRoam()) return;
        int r = _rng.Next(100);
        if (r < 45) { if (_rng.Next(3) == 0) FacingRight = !FacingRight; Begin(Anim.Walk, 3 + _rng.NextDouble() * 5); }
        else if (r < 70) Begin(Anim.Sit, 2 + _rng.NextDouble() * 3);
        else if (r < 84) Begin(Anim.Lick, 4 * 0.18 * 3);
        else if (r < 100 - Tune.StretchPercent) Begin(Anim.Wash, 4 * 0.18 * 2);
        else Begin(Anim.Stretch, 3 * 0.25 + Tune.StretchHold);
    }

    /// Sit: frame 0 normally; random twitch = blink (frame 1, 150 ms) or tail flick (frames 2,3, 120 ms each).
    void SitFrame(double dt)
    {
        if (_twitchLeft > 0)
        {
            _twitchLeft -= dt;
            Frame = _twitchFrame == 1 ? 1 : (_twitchLeft > 0.12 ? 2 : 3);
            return;
        }
        Frame = 0;
        if ((_twitchIn -= dt) > 0) return;
        _twitchIn = 2 + _rng.NextDouble() * 3;
        if (_rng.Next(2) == 0) { _twitchFrame = 1; _twitchLeft = 0.15; } else { _twitchFrame = 2; _twitchLeft = 0.24; }
    }


    /// Right limit for wandering: EdgeMargin short of the strip end, so nobody parks against the tray/clock.
    static double EdgeX(World w) => Math.Max(w.MinX, w.Width - Size - Tune.EdgeMargin);

    /// Soft personal space, picked at every idle decision (the one shared place all wandering goes through): a cat inside
    /// PersonalSpace of another, or parked in the end margin, walks off instead. Of two idle cats the lower Id stays.
    bool MoveApart(World w)
    {
        double edge = EdgeX(w);
        foreach (var o in w.Cats)
        {
            if (ReferenceEquals(o, this) || !o.IsLive || Math.Abs(o.X - X) >= Tune.PersonalSpace) continue;
            if (o._status == SessionStatus.Idle && !o._pinned && !o._asleep && string.CompareOrdinal(Id, o.Id) < 0) continue;   // it leaves
            bool right = o.X == X ? string.CompareOrdinal(Id, o.Id) > 0 : X > o.X;
            if (right ? X >= edge : X <= w.MinX) right = !right;
            StartWalk(right, (Tune.PersonalSpace * 1.5 - Math.Abs(o.X - X)) / WalkSpeed + _rng.NextDouble() * 2);   // leave with room to spare
            return true;
        }
        if (X <= edge) return false;
        StartWalk(false, (X - edge) / WalkSpeed + 1 + _rng.NextDouble() * 2);
        return true;
    }

    /// Where a new (or returning) cat appears: the best of a few random spots in the strip, away from the others and the end.
    public static double SpawnX(World w, IEnumerable<double> others, Random rng)
    {
        double lo = w.MinX, span = Math.Max(0, EdgeX(w) - lo), best = lo, bestGap = -1;
        var xs = others.ToList();
        for (int i = 0; i < 8; i++)
        {
            double x = lo + rng.NextDouble() * span, gap = xs.Count == 0 ? 0 : xs.Min(o => Math.Abs(o - x));
            if (gap > bestGap) { best = x; bestGap = gap; }
        }
        return best;
    }
    void Begin(Anim a, double seconds) { Anim = a; _t = 0; _left = seconds; Frame = 0; }
    void Loop(double rate = 1) { var (n, ms) = Timing[Anim]; Frame = (int)(_t * 1000 * Math.Min(rate, Tune.RunRateMax) / ms) % n; }
    void Once() { var (n, ms) = Timing[Anim]; Frame = Math.Min((int)(_t * 1000 / ms), n - 1); }
}
