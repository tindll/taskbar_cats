using TaskbarCats;
using Xunit;

public class HerdFriendTests
{
    [Fact] public void Cats_on_one_strip_in_the_same_folder_are_friends()
    {
        var cats = new[] { ("b", "S1", @"C:\code\app"), ("a", "S1", @"c:\Code\App\"), ("c", "S1", @"C:\code\other") };
        Assert.Equal("a", Herd.FriendOf("b", cats));
        Assert.Equal("b", Herd.FriendOf("a", cats));
        Assert.Null(Herd.FriendOf("c", cats));
    }

    [Fact] public void Another_strip_or_an_unknown_folder_means_no_friend()
    {
        Assert.Null(Herd.FriendOf("a", [("a", "S1", @"C:\app"), ("b", "S2", @"C:\app")]));
        Assert.Null(Herd.FriendOf("a", [("a", "S1", ""), ("b", "S1", "")]));
        Assert.Null(Herd.FriendOf("x", [("a", "S1", @"C:\app")]));
    }

    [Fact] public void With_several_friends_the_lowest_id_is_picked()
    {
        var cats = new[] { ("c", "S1", @"C:\app"), ("b", "S1", @"C:\app"), ("a", "S1", @"C:\app") };
        Assert.Equal("a", Herd.FriendOf("c", cats));
        Assert.Equal("b", Herd.FriendOf("a", cats));
    }
}
