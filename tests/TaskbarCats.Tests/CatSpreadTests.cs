using TaskbarCats;
using Xunit;

public class CatSpreadTests
{
    const double Dt = 1.0 / 30, Width = 1000, Edge = Width - Cat.Size - Tune.EdgeMargin;

    [Fact] public void Cats_stacked_at_the_strip_end_spread_out_and_stay_off_the_end()
    {
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var cats = Enumerable.Range(0, 5).Select(i => new Cat($"c{i}", Width - Cat.Size - i, new Random(seed * 10 + i)) { Status = SessionStatus.Idle }).ToList();
            var w = new World(Width, null, cats);
            double t = 0; int crowded = 0, samples = 0;
            for (; t < 1; t += Dt) foreach (var c in cats) c.Tick(Dt, w);
            foreach (var c in cats) c.Click();   // acknowledged: awake and wandering (an un-clicked cat dozes off after 2 min)
            for (; t < 300; t += Dt) foreach (var c in cats) c.Tick(Dt, w);
            for (; t < 600; t += Dt)
            {
                foreach (var c in cats) c.Tick(Dt, w);
                Assert.All(cats, c => Assert.True(c.X <= Edge + 1, $"seed {seed}: cat at {c.X} parked in the end margin"));
                samples++;
                if (cats.Any(a => a.Anim != Anim.Walk && cats.Any(b => b.Anim != Anim.Walk && a != b && Math.Abs(a.X - b.X) < Cat.Size - 1))) crowded++;
            }
            Assert.True(crowded < samples * 0.15, $"seed {seed}: cats within a cat width in {crowded}/{samples} samples: {string.Join(",", cats.Select(c => $"{c.X:F0}{c.Anim}"))}");
        }
    }

    [Fact] public void New_cats_appear_spaced_from_the_others_and_short_of_the_end()
    {
        var rng = new Random(7);
        var w = new World(Width, null, []);
        var xs = new List<double>();
        for (int i = 0; i < 6; i++) xs.Add(Cat.SpawnX(w, xs, rng));
        Assert.All(xs, x => Assert.InRange(x, 0, Edge));
        var sorted = xs.Order().ToList();
        for (int i = 1; i < sorted.Count; i++) Assert.True(sorted[i] - sorted[i - 1] >= Cat.Size * 0.5, $"{sorted[i - 1]} / {sorted[i]}");
    }
}
