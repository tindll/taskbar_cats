namespace TaskbarCats;

/// Which toy each busy cat plays with, so not every cat does the same thing (item 16). Pure.
public static class Pursuits
{
    /// busy = every busy cat's session id and run length. The longest-running cat chooses first: its favourite toy
    /// (Tune.FavouriteToy) rotated one step per Tune.RotateEvery of its run, skipping toys already taken.
    /// More than three busy cats: the rest repeat a toy. picked = the right-click menu's Favourite toy per cat id:
    /// those cats choose first and always get it (no rotation).
    public static Dictionary<string, Toy> Assign(IEnumerable<(string Id, TimeSpan Run)> busy, IReadOnlyDictionary<string, Toy>? picked = null)
    {
        picked ??= new Dictionary<string, Toy>();
        var taken = new HashSet<Toy>();
        var toys = new Dictionary<string, Toy>();
        foreach (var (id, run) in busy.OrderByDescending(b => picked.ContainsKey(b.Id)).ThenByDescending(b => b.Run).ThenBy(b => b.Id, StringComparer.Ordinal))
        {
            if (picked.TryGetValue(id, out var mine)) { toys[id] = mine; taken.Add(mine); continue; }
            int first = (int)Tune.FavouriteToy(id) + (int)(Math.Max(0, run.Ticks) / Tune.RotateEvery.Ticks);
            var order = Enumerable.Range(0, 3).Select(k => (Toy)((first + k) % 3)).ToList();
            var toy = order.FirstOrDefault(t => !taken.Contains(t), order[0]);
            toys[id] = toy;
            taken.Add(toy);
        }
        return toys;
    }
}

/// The yarn ball on a strip, in strip DIP (X = left edge of its 48 DIP box). Rolls with friction and bounces off the ends.
/// Stepped in slices of at most Flight.Step, so how far it rolls does not depend on the frame rate.
public sealed class YarnBall(double x)
{
    double _rolled, _sinceFlip;

    public double X { get; private set; } = x;
    public double Vx { get; private set; }
    /// Moving at all: friction stops it dead below Tune.YarnStop, so it never creeps.
    public bool Rolling => Vx != 0;
    /// Roll frame (0/1): flips every Tune.YarnRollPx rolled, at most Tune.YarnFlipHz times a second.
    public int Frame { get; private set; }

    public void Push(double vx) => Vx = vx;

    public void Step(double dt, double lo, double hi)
    {
        X = Math.Clamp(X, lo, Math.Max(lo, hi));   // the strip shrank under a resting ball
        int n = Math.Max(1, (int)Math.Ceiling(dt / Flight.Step - 1e-9));
        double h = dt / n;
        for (int i = 0; i < n && Vx != 0; i++)
        {
            X += Vx * h;
            _rolled += Math.Abs(Vx * h);
            if (X < lo) { X = lo; Vx = -Vx * Tune.YarnBounce; }
            else if (X > hi) { X = hi; Vx = -Vx * Tune.YarnBounce; }
            double v = Math.Abs(Vx) - Tune.YarnFriction * h;
            Vx = v <= Tune.YarnStop ? 0 : Math.Sign(Vx) * v;
        }
        _sinceFlip += dt;
        if (_rolled >= Tune.YarnRollPx && _sinceFlip >= 1 / Tune.YarnFlipHz) { Frame ^= 1; _rolled = 0; _sinceFlip = 0; }
    }
}
