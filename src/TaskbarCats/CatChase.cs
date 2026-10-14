namespace TaskbarCats;

/// Play mode (the teaser wand): the cat chases the cursor along its strip and jumps for it. Pure logic.
/// Chasing replaces the cat's mood visuals (toys, props), but not what it has to say: a finished cat's MEOW and a
/// waiting cat's "!" keep showing, and a click still focuses the conversation.
public sealed partial class Cat
{
    enum Chase { Run, Jump, Rest, Catch }

    Chase _chase;
    bool _chasing;
    double _chaseT, _chaseCd, _peak;

    /// The lure on this cat's strip, set by Herd every frame while play mode is on and the cursor is on this monitor.
    public Lure? Lure { get; set; }

    /// Up and about, free to go: not hopping, held, binned, "Stay here" or running after a thrown ball.
    public bool Playable => _mode == Mode.Live && !_pinned && !_fetch && _fetchDir == 0;

    /// True while the chase drives the cat this tick (TickBody then does nothing else).
    bool TickChase(double dt)
    {
        bool jumping = _chasing && _chase == Chase.Jump;   // Anim.Leap counts as Committed: this one is ours
        if (Lure is not { } l || !Playable) { EndChase(true); return false; }
        if ((Committed && !jumping) || _actLeft > 0) { EndChase(false); return false; }   // a pounce or a reaction finishes first
        if (!_chasing)
        {
            _chasing = true; _chase = Chase.Run; _chaseT = 0; _chaseCd = 0;
            StopActs(); _asleep = false; _faceOff = false; _boost = false;
        }
        HasMouse = false; Yarn = null; Bird = null; Prop = Prop.None;
        Bubble = StatusBubble();
        if (_status == SessionStatus.Idle && !Acknowledged) _meow = (_meow + dt) % MeowPeriod;   // MEOW keeps blinking, no nap

        _chaseT += dt; _chaseCd -= dt;
        double gap = Math.Clamp(l.X - Size / 2, _lo, _hi) - X;
        switch (_chase)
        {
            case Chase.Jump:
                double p = Math.Min(_chaseT / Tune.JumpTime, 1);
                Lift = _peak * Math.Sin(Math.PI * p);
                Once();
                if (p >= 1) { Lift = 0; _chase = Chase.Rest; _chaseT = 0; Begin(Anim.LookUp, 0); }   // a miss: it looks up again
                return true;
            case Chase.Catch:
                Lift = 0; Frame = 2;
                if (_chaseT >= Tune.CatchHold) { _chase = Chase.Run; _chaseCd = Tune.CatchHold; Begin(Anim.LookUp, 0); }   // lets go
                return true;
            case Chase.Rest:
                Lift = 0; Loop();
                if (_chaseT >= Tune.JumpRest) _chase = Chase.Run;
                return true;
        }
        Lift = 0;
        if (Math.Abs(gap) > 1) FacingRight = gap > 0;
        var reach = Teaser.ReachOf(l.Height);
        if (reach == Reach.Watch) { Hold(Anim.LookUp); return true; }   // too high: sits and watches, head up
        if (reach == Reach.Jump && Math.Abs(gap) <= Tune.JumpUnder && _chaseCd <= 0)
        {
            _chaseT = 0; _peak = Teaser.Peak(l.Height);
            if (l.Height <= Tune.CatchHeight && _rng.NextDouble() < Tune.CatchChance) { _chase = Chase.Catch; Begin(Anim.Bat, 0); Frame = 2; }
            else { _chase = Chase.Jump; Begin(Anim.Leap, Tune.JumpTime); }
            return true;
        }
        if (Math.Abs(gap) > Tune.PlayStop)
        {
            if (Anim != Anim.Run) Begin(Anim.Run, 0);
            X += Math.Sign(gap) * Math.Min(Tune.PlayRun * Speed * dt, Math.Abs(gap));
            Loop();
        }
        else Hold(Anim.LookUp);
        return true;
    }

    /// The chase is over (the lure went, or the cat cannot play now). resume: pick the normal behaviour up again.
    void EndChase(bool resume)
    {
        if (!_chasing) return;
        _chasing = false; _chase = Chase.Run; Lift = 0;
        if (resume && _mode == Mode.Live) Begin(StatusAnim(), 1.0);
    }
}
