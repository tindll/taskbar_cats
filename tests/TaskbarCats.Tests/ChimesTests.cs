using TaskbarCats;
using Xunit;
using St = TaskbarCats.SessionStatus;

public class ChimesTests
{
    [Theory]
    [InlineData(St.Working, St.Idle, Chime.Done)]
    [InlineData(St.Idle, St.Waiting, Chime.Waiting)]
    [InlineData(St.Working, St.Waiting, Chime.Waiting)]
    [InlineData(St.Waiting, St.Waiting, null)]
    [InlineData(St.Idle, St.Working, null)]
    [InlineData(St.Waiting, St.Idle, null)]
    [InlineData(St.Waiting, St.Working, null)]
    [InlineData(St.Idle, St.Idle, null)]
    public void Which_changes_chime(St was, St now, Chime? chime) =>
        Assert.Equal(chime, new Chimes().Decide("a", was, now, 100));

    [Fact] public void One_chime_per_cat_per_ten_seconds()
    {
        var c = new Chimes();
        Assert.Equal(Chime.Done, c.Decide("a", St.Working, St.Idle, 0));
        Assert.Null(c.Decide("a", St.Idle, St.Waiting, 9.9));
        Assert.Equal(Chime.Waiting, c.Decide("a", St.Idle, St.Waiting, 10));
    }

    [Fact] public void One_chime_from_all_cats_per_two_seconds()
    {
        var c = new Chimes();
        Assert.NotNull(c.Decide("a", St.Working, St.Idle, 0));
        Assert.Null(c.Decide("b", St.Working, St.Idle, 1.9));
        Assert.NotNull(c.Decide("b", St.Working, St.Idle, 2));
    }

    [Fact] public void A_refused_chime_does_not_restart_the_clocks()
    {
        var c = new Chimes();
        c.Decide("a", St.Working, St.Idle, 0);
        Assert.Null(c.Decide("b", St.Working, St.Idle, 1));      // global gap
        Assert.NotNull(c.Decide("b", St.Working, St.Idle, 2));   // measured from 0, not 1
    }

    [Fact] public void No_status_change_never_touches_the_clocks()
    {
        var c = new Chimes();
        Assert.Null(c.Decide("a", St.Working, St.Working, 0));
        Assert.NotNull(c.Decide("b", St.Working, St.Idle, 0.5));
    }
}
