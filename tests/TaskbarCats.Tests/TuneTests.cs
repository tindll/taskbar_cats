using TaskbarCats;
using Xunit;

public class TuneTests
{
    [Fact] public void Trait_is_stable_per_session_and_all_four_occur()
    {
        const string id = "494d4841-0e3a-4fe4-92ab-24b6811922d0";
        Assert.Equal(Tune.TraitOf(id), Tune.TraitOf(id));
        var seen = Enumerable.Range(0, 400).Select(i => Tune.TraitOf(new Guid(i, 7, 7, new byte[8]).ToString())).Distinct().Count();
        Assert.Equal(4, seen);
    }

    [Fact] public void Hash_is_fnv1a() => Assert.Equal(0xE40C292Cu, Tune.Hash("a"));

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", Look.Kitten)]
    [InlineData("claude-sonnet-5-5", Look.Cat)]
    [InlineData("claude-opus-5-5", Look.Big)]
    [InlineData("claude-fable-5", Look.Big)]
    [InlineData(null, Look.Cat)]
    [InlineData("something-new", Look.Cat)]
    public void Look_follows_the_model(string? model, Look look) => Assert.Equal(look, Tune.LookOf(model));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100_000, 100_000, 0)]       // 37% of 267k
    [InlineData(120_000, 120_000, 1)]       // 45%
    [InlineData(200_000, 200_000, 2)]       // 75%
    [InlineData(400_000, 400_000, 1)]       // past 300k: the 1M window, 40%
    [InlineData(50_000, 400_000, 0)]        // compacted back down, still the 1M window
    [InlineData(800_000, 800_000, 2)]
    public void Belly_stage_is_coarse(long context, long max, int stage) => Assert.Equal(stage, Tune.BellyStage(context, max));

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(3, 1)] [InlineData(7, 1)] [InlineData(8, 2)] [InlineData(50, 2)]
    public void Yarn_grows_with_edits(int edits, int stage) => Assert.Equal(stage, Tune.YarnStage(edits));

    [Theory]
    [InlineData("Read", Activity.Sniff)] [InlineData("Grep", Activity.Sniff)] [InlineData("Glob", Activity.Sniff)]
    [InlineData("Edit", Activity.Bat)] [InlineData("Write", Activity.Bat)] [InlineData("MultiEdit", Activity.Bat)]
    [InlineData("WebFetch", Activity.Watch)] [InlineData("WebSearch", Activity.Watch)]
    public void Tools_map_to_poses(string tool, Activity pose)
    {
        Assert.Equal(pose, Tune.PoseFor(tool));
        Assert.True(Tune.IsPose(pose));
    }

    [Fact] public void Other_tools_have_no_pose()
    {
        Assert.Null(Tune.PoseFor("Task"));
        Assert.Null(Tune.PoseFor("TodoWrite"));
        Assert.Null(Tune.PoseFor(null));
        Assert.False(Tune.IsPose(Activity.Toy));
        Assert.False(Tune.IsPose(Activity.Box));
    }

    [Fact] public void Shell_commands_play_with_the_toy()
    {
        Assert.Null(Tune.PoseFor("Bash"));
        Assert.Null(Tune.PoseFor("PowerShell"));
        Assert.Null(Tune.PoseFor("BashOutput"));
    }

    [Fact] public void Halloween_is_the_last_two_weeks_of_october()
    {
        Assert.False(Tune.IsHalloween(new DateTime(2026, 10, 16)));
        Assert.True(Tune.IsHalloween(new DateTime(2026, 10, 17)));
        Assert.True(Tune.IsHalloween(new DateTime(2026, 10, 31)));
        Assert.False(Tune.IsHalloween(new DateTime(2026, 11, 1)));
    }

    [Fact] public void Favourite_toy_follows_the_trait()
    {
        foreach (var id in Enumerable.Range(0, 200).Select(i => new Guid(i, 3, 3, new byte[8]).ToString()))
        {
            var toy = Tune.FavouriteToy(id);
            switch (Tune.TraitOf(id))
            {
                case Trait.Hyper: Assert.Equal(Toy.Mouse, toy); break;
                case Trait.Curious: Assert.Equal(Toy.Bird, toy); break;
                case Trait.Lazy: Assert.Equal(Toy.Yarn, toy); break;
            }
        }
    }

    [Theory]
    [InlineData(100_000, 100_000, Tune.Window)]
    [InlineData(100_000, 400_000, Tune.BigWindow)]   // the peak decides: a 1M-context model stays on the big window after compaction
    [InlineData(0, 0, Tune.Window)]
    public void Context_window_follows_the_peak(long ctx, long max, long window) =>
        Assert.Equal(window, Tune.ContextWindow(ctx, max));
}
