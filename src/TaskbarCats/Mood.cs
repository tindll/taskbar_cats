namespace TaskbarCats;

/// A cat's state of mind, from its conversation's live signals. Pure logic, no WPF: Herd calls Update once per poll
/// and hands the result to the Cat. Unknown signals (null) mean today's behaviour: chase the toy, no cues.
public sealed class Mood
{
    readonly Random _rng;
    Signals? _last;
    SessionStatus _status = SessionStatus.Idle;
    DateTimeOffset _since, _dwellUntil;
    DateTimeOffset? _wonderedFor;
    DateTimeOffset _mrrpAt = DateTimeOffset.MinValue, _catchAt = DateTimeOffset.MinValue,
        _slipAt = DateTimeOffset.MinValue, _wonderAt = DateTimeOffset.MinValue;

    public Mood(string sessionId, Random rng) { Trait = Tune.TraitOf(sessionId); _rng = rng; }

    public Trait Trait { get; set; }   // the menu's Personality overrides the session hash (Herd.Restyle)
    public Activity Activity { get; private set; } = Activity.Toy;
    public Look Look { get; private set; } = Look.Cat;
    public int Belly { get; private set; }
    public int Yarn { get; private set; }
    public int Kittens { get; private set; }
    /// How long the current busy streak has run; zero while idle. Waiting (a permission prompt) is part of the run.
    public TimeSpan Run { get; private set; }

    /// One poll. statusAt = SessionInfo.UpdatedAt (while busy, the start of the busy streak).
    public IReadOnlyList<Cue> Update(Signals? s, SessionStatus status, DateTimeOffset statusAt, int subagents, DateTimeOffset now)
    {
        var cues = new List<Cue>();
        if (status != SessionStatus.Idle && _status == SessionStatus.Idle) _since = statusAt;
        var ran = now - _since;
        Run = status == SessionStatus.Idle ? TimeSpan.Zero : ran;
        Kittens = Math.Clamp(subagents, 0, Tune.MaxKittens);
        Look = Tune.LookOf(s?.Model);
        Belly = s is null ? 0 : Tune.BellyStage(s.Context, s.MaxContext);
        Yarn = Tune.YarnStage(s?.Edits ?? 0);
        if (s is not null && _last is not null) Events(_last, s, status, ran, now, cues);
        _last = s;   // null (unreadable) forgets the counters: no burst of cues when it comes back
        Activity = status == SessionStatus.Working ? Pick(s, now) : Activity.Toy;
        if (Activity == Activity.Box && s?.LastAt is { } at && now - at >= Tune.WonderAfter
            && _wonderedFor != at && Ready(ref _wonderAt, Tune.WonderEvery, now))
        {
            _wonderedFor = at;
            cues.Add(Cue.Wonder);
        }
        _status = status;
        return cues;
    }

    void Events(Signals was, Signals s, SessionStatus status, TimeSpan ran, DateTimeOffset now, List<Cue> cues)
    {
        bool busy = status == SessionStatus.Working;
        if (busy && s.ToolAt is not null && s.ToolAt != was.ToolAt && Ready(ref _mrrpAt, Tune.MrrpEvery, now)) cues.Add(Cue.Mrrp);
        if (s.Errors > was.Errors) { if (Ready(ref _slipAt, Tune.SlipEvery, now)) cues.Add(Cue.Slip); }
        else if (busy && s.Results > was.Results && Ready(ref _catchAt, Tune.CatchEvery, now)) cues.Add(Cue.Catch);
        if (s.Compactions > was.Compactions) cues.Add(Cue.Sneeze);
        bool startle = s.ApiErrors > was.ApiErrors && s.ApiError is not null and not "rate_limit";
        if (_status == SessionStatus.Working && status == SessionStatus.Idle)
        {
            // ponytail: "interrupted" is inferred from a turn that ended without end_turn; Claude Code writes no flag for it.
            if (s.Stop == "end_turn") { if (ran >= Tune.RewardAfter) cues.Add(Cue.Reward); }
            else if (s.ApiError != "rate_limit") startle = true;
        }
        if (startle) cues.Add(Cue.Startle);
    }

    Activity Pick(Signals? s, DateTimeOffset now)
    {
        if (s is null) return Activity.Toy;
        if (s.ApiError == "rate_limit") return Activity.Limit;
        if (s.Stop == "end_turn" && Kittens > 0) return Activity.Supervise;
        if (Run >= Tune.NapAfter) return Activity.Nap;
        bool running = s.Stop == "tool_use" && s.Open > 0;
        if (!running && s.LastAt is { } at && now - at >= Tune.BoxAfter) return Activity.Box;

        var pose = Tune.PoseFor(s.Tool);
        bool stale = Activity is Activity.Box or Activity.Supervise or Activity.Limit or Activity.Nap
            || (Tune.IsPose(Activity) && Activity != pose)
            || (Activity == Activity.Toy && Run >= Tune.RestAfter);
        if (!stale && now < _dwellUntil) return Activity;
        _dwellUntil = now + Tune.MinDwell + (Tune.MaxDwell - Tune.MinDwell) * _rng.NextDouble();

        var options = new List<(Activity A, double W)>();
        if (pose is { } p) options.Add((p, Tune.ToolWeight));
        options.Add((Run >= Tune.RestAfter ? Activity.Rest : Activity.Toy, Tune.ToyWeight * Tune.ToyBias(Trait)));
        options.Add((Activity.Butterfly, Tune.RareWeight * Tune.RareBias(Trait)));
        options.RemoveAll(o => o.A == Activity);   // no back-to-back repeats (there are always at least two options)
        double r = _rng.NextDouble() * options.Sum(o => o.W);
        foreach (var (a, w) in options)
            if ((r -= w) < 0) return a;
        return options[^1].A;
    }

    static bool Ready(ref DateTimeOffset last, TimeSpan every, DateTimeOffset now)
    {
        if (now - last < every) return false;
        last = now;
        return true;
    }
}
