namespace TaskbarCats;

/// v5: an idle cat now and then walks off the end of its strip onto the next monitor's (Herd.RoamOffer picks the way
/// and the chance; Herd.Roam moves it across, the same Move as the cross-screen yarn chase). Pure logic.
public sealed partial class Cat
{
    int _roam;          // +1 / -1 while walking off that end of the strip
    double _roamCd;     // s before it may wander off again

    /// Set by Herd each poll: the way to the monitor this cat may wander to (0 = none) and the chance per idle pick.
    public int RoamDir { get; set; }
    public double RoamChance { get; set; }
    public bool IsRoaming => _roam != 0;
    /// +1 / -1 once a wandering cat stands at that end of its strip and Herd must move it across (Arrive); else 0.
    public int RoamingTo => _mode != Mode.Live || _asleep ? 0
        : _roam > 0 && X >= _hi - Tune.CrossSlop ? 1 : _roam < 0 && X <= _lo + Tune.CrossSlop ? -1 : 0;

    /// From PickBehaviour. Draws a random number only when there is somewhere to go: a single monitor is unchanged.
    bool TryRoam()
    {
        if (RoamDir == 0 || _roamCd > 0 || _pinned || _rng.NextDouble() >= RoamChance) return false;
        _roam = RoamDir; FacingRight = _roam > 0;
        Begin(Anim.Walk, 0);
        return true;
    }

    /// Walk to the strip's very end (past the end margin: this exit is on purpose), then wait there for Herd.
    bool Roaming(double dt)
    {
        if (_roam == 0) return false;
        if (_pinned) { _roam = 0; return false; }
        double gap = (_roam > 0 ? _hi : _lo) - X;
        FacingRight = _roam > 0; _faceOff = false;   // a walker hissing at it is ignored: no face-off left over for the arrival
        if (Math.Abs(gap) < 0.5) { if (Anim != Anim.Sit) Begin(Anim.Sit, 0); Frame = 0; return true; }   // Herd crosses next frame
        if (Anim != Anim.Walk) Begin(Anim.Walk, 0);
        X += Math.Sign(gap) * Math.Min(WalkSpeed * dt, Math.Abs(gap));
        Loop();
        return true;
    }

    /// Herd moved the cat onto the next strip at its near end x: it walks on in, clear of the end margin, and stays a while.
    public void Arrive(double x)
    {
        X = x; _roam = 0; _roamCd = Tune.RoamStay;
        Begin(Anim.Walk, (Tune.EdgeMargin + Size) / WalkSpeed + 1);   // still facing the way it was going: inwards
    }

    /// The way is shut (no monitor there now, fullscreen, the cat is pressed or its menu is open): turn round, walk back in.
    public void CancelRoam()
    {
        if (_roam == 0) return;
        _roam = 0; _roamCd = Tune.RoamStay;   // ponytail: the same wait as after a crossing, so it does not retry at once
        StartWalk(!FacingRight, (Tune.EdgeMargin + Size) / WalkSpeed + 1);
    }
}
