namespace TaskbarCats;

public enum Chime { Done, Waiting }

/// When a status change earns a sound (Herd plays it, only with Sounds on): working → idle = Done, anything → waiting
/// = Waiting. Only registry/hook status changes count, never murmurs or cues (tag's "mrrp" stays silent). At most one
/// per cat every Tune.ChimeCatGap s and one from all cats every Tune.ChimeGap s; a refused chime is dropped, never
/// queued. t = Herd's clock, seconds.
public sealed class Chimes
{
    // ponytail: one entry per session id, never pruned (bytes per conversation); prune if that ever matters.
    readonly Dictionary<string, double> _cat = new();
    double _any = double.NegativeInfinity;

    public Chime? Decide(string id, SessionStatus was, SessionStatus now, double t)
    {
        Chime? c = was == SessionStatus.Working && now == SessionStatus.Idle ? Chime.Done
            : now == SessionStatus.Waiting && was != SessionStatus.Waiting ? Chime.Waiting : null;
        if (c is null || t - _any < Tune.ChimeGap - 1e-9) return null;
        if (_cat.TryGetValue(id, out var last) && t - last < Tune.ChimeCatGap - 1e-9) return null;
        _any = _cat[id] = t;
        return c;
    }
}
