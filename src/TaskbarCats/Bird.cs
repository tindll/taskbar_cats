namespace TaskbarCats;

/// A little bird on a strip, in strip DIP. Perches, pecks and hops; when the cat comes within Tune.BirdFlee it flies
/// off in an arc and lands Tune.BirdFlyDist farther on (back over the cat when there is no room ahead). Never caught.
public sealed class Bird(double x, Random rng)
{
    double _t, _from, _to, _hopIn = 2 + 2 * rng.NextDouble();

    public double X { get; private set; } = x;
    public double Lift { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public bool Flying { get; private set; }
    public int Frame => Flying ? (int)(_t * 1000 / Tune.BirdFlapMs) % 2
        : _t % Tune.BirdPeckEvery > Tune.BirdPeckEvery - Tune.BirdPeckTime ? 1 : 0;

    public void Step(double dt, double catX, double lo, double hi)
    {
        _t += dt;
        if (Flying)
        {
            double p = Math.Min(_t / Tune.BirdFlyTime, 1);
            X = _from + (_to - _from) * p;
            Lift = Tune.BirdFlyArc * Math.Sin(Math.PI * p);
            if (p >= 1) { Flying = false; Lift = 0; _t = 0; }
            return;
        }
        if (Math.Abs(catX - X) < Tune.BirdFlee) { Flee(catX, lo, hi); return; }
        if ((_hopIn -= dt) > 0) return;
        _hopIn = 2 + 2 * rng.NextDouble();
        FacingRight = rng.Next(2) == 0;
        X = Math.Clamp(X + (FacingRight ? 1 : -1) * Tune.BirdHop, lo, Math.Max(lo, hi));
    }

    /// The user clicked the bird: it takes off at once and lands somewhere else on the strip, away from where it was
    /// and from the cat (Flee's hop when no such spot turns up). False when it is already in the air.
    public bool Scare(double catX, double lo, double hi)
    {
        if (Flying) return false;
        for (int i = 0; i < 8; i++)
        {
            double to = lo + (Math.Max(lo, hi) - lo) * rng.NextDouble();
            if (Math.Abs(to - X) < Tune.BirdFlyDist / 2 || Math.Abs(to - catX) < Tune.BirdFlee) continue;
            _from = X; _to = to; FacingRight = to >= X; Flying = true; _t = 0;
            return true;
        }
        Flee(catX, lo, hi);
        return true;
    }

    public void Flee(double catX, double lo, double hi)
    {
        if (Flying) return;
        double dir = X >= catX ? 1 : -1, to = X + dir * Tune.BirdFlyDist;
        if (to < lo || to > hi) to = X - dir * Tune.BirdFlyDist;
        _from = X; _to = Math.Clamp(to, lo, Math.Max(lo, hi));
        FacingRight = _to >= _from;
        Flying = true; _t = 0;
    }
}
