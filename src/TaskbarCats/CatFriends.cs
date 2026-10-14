namespace TaskbarCats;

/// v4: friends (cats on one strip working in the same folder) and the yarn ball the user can throw. Pure logic.
public sealed partial class Cat
{
    Cat? _greet;
    bool _nuzzle;
    double _greetLeft;

    /// Another cat on this strip whose conversation runs in the same folder (set by Herd), or null.
    public Cat? Friend { get; set; }

    /// Walk over to friend, then nuzzle it with a heart (or just sit beside it). Only an idle cat that is up and about goes.
    public void Greet(Cat friend, bool nuzzle = true)
    {
        if (_mode != Mode.Live || _pinned || Committed || _actLeft > 0 || _status != SessionStatus.Idle || _asleep
            || ReferenceEquals(friend, this) || !friend.IsLive) return;
        _greet = friend; _nuzzle = nuzzle; _greetLeft = Tune.GreetTimeout; _faceOff = false;
    }

    bool Greeting(double dt)
    {
        if (_greet is not { } f) return false;
        if ((_greetLeft -= dt) <= 0 || !f.IsLive) { _greet = null; return false; }
        double gap = f.X - X, beside = Size * 0.75;
        FacingRight = gap >= 0;
        if (Math.Abs(gap) > beside)
        {
            if (Anim != Anim.Walk) Begin(Anim.Walk, 0);
            X += Math.Sign(gap) * Math.Min(WalkSpeed * dt, Math.Abs(gap) - beside);
            Loop();
            return true;
        }
        _greet = null;
        if (_nuzzle) { BeginAct(Act.Nuzzle, Anim.Nuzzle, Tune.NuzzleTime); Mutter(Murmur.Heart); }
        else Begin(Anim.Sit, 3 + _rng.NextDouble() * 3);
        return true;
    }

    // Tag (v5): the cat that is "it" runs at its friend and taps it with a paw; then the other one is it.
    Cat? _tag;          // the playmate, while a game is on
    bool _it;           // this cat is the chaser
    double _tagLeft, _tagWait, _tagCd;

    public bool IsTagging => _tag is not null;
    /// The chaser, while a game of tag is on.
    public bool It => _tag is not null && _it;

    /// Up for a game: live, idle, awake, free to move, not busy with anything else, and not just played.
    bool CanTag => _mode == Mode.Live && !_pinned && !Committed && _actLeft <= 0 && _status == SessionStatus.Idle && !_asleep
        && _greet is null && _tag is null && _tagCd <= 0 && _zoom <= 0 && _roam == 0;

    /// A game of tag with friend (on this strip). This cat is it first. False when either is not up for it.
    public bool StartTag(Cat friend)
    {
        if (ReferenceEquals(friend, this) || !CanTag || !friend.CanTag) return false;
        _tag = friend; _it = true; _tagLeft = Tune.TagTime; _tagWait = Tune.TagWait; _faceOff = false;
        friend._tag = this; friend._it = false; friend._tagLeft = Tune.TagTime; friend._tagWait = 0; friend._faceOff = false;
        Begin(Anim.Crouch, 0);   // wiggle, then go
        Mutter(Murmur.Mrrp);
        return true;
    }

    void EndTag()
    {
        if (_tag is null) return;
        _tag = null; _tagCd = Tune.TagCooldown;
        if (_mode == Mode.Live && Anim == Anim.Crouch) Begin(Anim.Sit, Tune.TagRest);   // never a pounce (Committed) from a tag crouch
    }

    /// Another cat left this strip (or this one did): drop any greeting or game with it, and the friendship until the next poll.
    public void Forget(Cat other)
    {
        if (ReferenceEquals(_greet, other)) _greet = null;
        if (ReferenceEquals(_tag, other)) EndTag();
        if (ReferenceEquals(Friend, other)) Friend = null;
    }

    /// One frame of tag. Ends for both when either stops playing (the other sees _tag no longer points back).
    bool Tagging(double dt, World w)
    {
        if (_tag is not { } o) return false;
        if (_pinned || !o.IsLive || o._asleep || !ReferenceEquals(o._tag, this) || (_tagLeft -= dt) <= 0)
        {
            EndTag();
            Begin(Anim.Sit, Tune.TagRest);
            return true;
        }
        if (_tagWait > 0) { _tagWait -= dt; Loop(); return true; }   // the tap (Bat) or the crouch before chasing
        double gap = o.X - X;
        if (_it)
        {
            FacingRight = gap >= 0;
            if (Math.Abs(gap) <= Tune.TagReach)
            {
                Begin(Anim.Bat, Tune.BatTime); _tagWait = Tune.BatTime; _it = false;
                o._it = true; o._tagWait = Tune.TagWait; o.Begin(Anim.Crouch, 0);
                return true;
            }
            Scamper(Math.Sign(gap) * Tune.TagRun * Tune.TagChaseBoost, w.MinX, Math.Max(w.MinX, w.Width - Size), dt);   // the chaser may go anywhere
            return true;
        }
        int away = gap > 0 ? -1 : gap < 0 ? 1 : string.CompareOrdinal(Id, o.Id) < 0 ? -1 : 1;
        double edge = EdgeX(w);   // the runner keeps out of the end margin (one already in it walks out, never jumps)
        if (away > 0 ? X >= edge : X <= w.MinX)   // cornered: crouch and wait to be tapped
        {
            FacingRight = gap >= 0;
            if (Anim != Anim.Crouch) Begin(Anim.Crouch, 0);
            Loop();
            return true;
        }
        Scamper(away * Tune.TagRun, w.MinX, Math.Max(edge, X), dt);
        return true;
    }

    void Scamper(double v, double lo, double hi, double dt)
    {
        if (Anim != Anim.Run) Begin(Anim.Run, 0);
        FacingRight = v >= 0;
        X = Math.Clamp(X + v * Speed * dt, lo, Math.Max(lo, hi));
        Loop();
    }

    /// The user picked up this cat's yarn ball, also one parked on another monitor's strip (the trip is off).
    /// False when there is none to take.
    public bool TakeYarn()
    {
        if (_fetchDir != 0) { _fetch = false; _fetchDir = 0; _yarnAway = true; return true; }
        if (Yarn is null) return false;
        Yarn = null; _yarnAway = true;
        return true;
    }

    /// The thrown ball comes down at x (this cat's strip DIP; off the strip = the nearest end) moving at vx DIP/s.
    /// A cat that stopped working meanwhile has no ball any more.
    public void DropYarn(double x, double vx)
    {
        _yarnAway = false; _playLeft = 0;
        if (_status != SessionStatus.Working || _mode is Mode.Gone or Mode.Binned) return;
        Yarn = new YarnBall(Math.Clamp(x, _lo, _hi));
        Yarn.Push(Math.Clamp(vx * Tune.YarnLandKeep, -Tune.YarnMaxRoll, Tune.YarnMaxRoll));
        _fetch = true;
        _fetchRun = Tune.FetchRun(Yarn.X - X);
    }

    // A thrown ball is chased whatever the mood (Activity), until the cat has caught up with it.
    bool _fetch;
    int _fetchDir;
    double _playLeft;   // > 0: the cat has the ball and plays with it (TickYarn) for this many more seconds
    double _fetchRun = Tune.YarnRun;   // this fetch's run speed (Tune.FetchRun of the throw distance)

    /// +1 / -1 while a thrown ball lies on another monitor's strip, to the right / left of this one; else 0.
    public int FetchDir => _fetchDir;
    /// +1 / -1 once the cat has run to that edge of its strip and Herd must move it across (Cross); else 0.
    public int CrossingTo => _fetchDir > 0 && X >= _hi - Tune.CrossSlop ? 1 : _fetchDir < 0 && X <= _lo + Tune.CrossSlop ? -1 : 0;

    /// The thrown ball came down on another monitor's strip (dir = the way to it): the cat runs to the strip's edge.
    /// False when the cat will not go (it stopped working, or "Stay here"): the caller drops the ball back instead.
    /// beyond = DIP from that strip's near end to the ball: the whole run sets the sprint.
    public bool DropYarnAcross(int dir, double beyond = 0)
    {
        if (_status != SessionStatus.Working || _mode != Mode.Live || _pinned) return false;
        Yarn = null; _yarnAway = true; _fetch = true; _fetchDir = dir < 0 ? -1 : 1; _playLeft = 0;
        _fetchRun = Tune.FetchRun((_fetchDir > 0 ? _hi - X : X - _lo) + beyond);
        return true;
    }

    /// The trip is off (the strip it was heading for went away): the ball is simply lost.
    public void CancelFetch() { _fetch = false; _fetchDir = 0; _yarnAway = false; }

    /// Herd moved the cat to the other strip, at x (DIP); the ball lies still at ballX there. The chase goes on.
    /// The mouse and the bird of the old strip stay behind.
    public void Cross(double x, double ballX)
    {
        X = x; FacingRight = ballX >= x; _fetchDir = 0; _yarnAway = false;
        HasMouse = false; Bird = null; Prop = Prop.None;
        Yarn = new YarnBall(ballX);
    }

    /// Run at the thrown ball (or at the strip edge on the way to it), faster than any other chase and faster the farther
    /// the ball came down (Tune.FetchRun), then play with it
    /// for Tune.YarnPlay seconds (the yarn cat's own TickYarn: bat it, run after it). False = done.
    bool TickFetch(double dt, World w)
    {
        if (_pinned || (_fetchDir == 0 && Yarn is null)) { _fetch = false; _fetchDir = 0; return false; }
        double target = _fetchDir > 0 ? _hi : _fetchDir < 0 ? _lo : Yarn!.X, gap = target - X;
        if (_fetchDir == 0 && _playLeft <= 0 && Math.Abs(gap) <= Tune.YarnReach && (!Yarn!.Rolling || Coming(Yarn, gap))) _playLeft = Tune.YarnPlay;
        Bubble = Bubble.None;
        if (_playLeft > 0)
        {
            if ((_playLeft -= dt) > 0) { TickYarn(dt, w); return true; }
            _fetch = false; _playLeft = 0;
            return false;
        }
        if (_fetchDir != 0 && Math.Abs(gap) < 0.5) { if (Anim != Anim.Sit) Begin(Anim.Sit, 0); SitFrame(dt); return true; }   // Herd crosses next frame
        FacingRight = gap >= 0;
        if (Anim != Anim.Run) Begin(Anim.Run, 0);
        X += Math.Sign(gap) * Math.Min(_fetchRun * Speed * dt, Math.Abs(gap));
        Loop(_fetchRun / Tune.YarnRun);   // quicker strides for a sprint
        return true;
    }
}
