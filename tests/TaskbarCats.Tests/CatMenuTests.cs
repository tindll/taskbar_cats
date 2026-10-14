using System.Windows;
using System.Windows.Controls;
using TaskbarCats;
using Xunit;

public class CatMenuTests
{
    static void Sta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw new Exception("failed on the STA thread", err);
    }

    static MenuItem Item(ItemCollection items, string header) =>
        items.OfType<MenuItem>().Single(i => i.Header as string == header);

    static void Click(MenuItem i) => i.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    static readonly CatMenu.Hooks None = new((_, _) => { }, () => [], () => { }, () => { }, () => false, () => { }, () => new AppSettings(), _ => { });

    [Fact] public void Every_option_is_in_the_right_click_menu() => Sta(() =>
    {
        var m = new ContextMenu();
        CatMenu.Fill(m.Items, @"C:\Code\App", new CatLook(), false, Trait.Lazy, () => { }, () => { }, None);
        var heads = m.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        foreach (var h in new[] { "Give sardine", "Teaser wand", "Send away (until next activity)", "Stay here", "Customize App", "Coat", "Plain coat (no stripes)", "Size", "Personality", "Collar",
                                  "Favourite toy", "Quiet (no blinking MEOW)", "Randomize look", "Reset to default", "Hide this project's cat", "Hidden cats", "Exit" })
            Assert.Contains(h, heads);
        Assert.Contains("Name tag...", heads);
        Assert.Equal("Default (lazy)", Item(Item(m.Items, "Personality").Items, "Default (lazy)").Header);
        Assert.Equal(1 + Sprites.Coats.Count, Item(m.Items, "Coat").Items.Count);
    });

    [Fact] public void Picking_a_coat_saves_it_for_the_folder_and_the_current_coat_is_checked() => Sta(() =>
    {
        var look = new CatLook { Coat = "Tuxedo" };
        (string Cwd, CatLook Look)? got = null;
        var m = new ContextMenu();
        CatMenu.Fill(m.Items, @"C:\Code\App", look, false, Trait.Lazy, () => { }, () => { }, new((cwd, f) => got = (cwd, f(look)), () => [], () => { }, () => { }, () => false, () => { }, () => new AppSettings(), _ => { }));
        var coat = Item(m.Items, "Coat");
        Assert.True(Item(coat.Items, "Tuxedo").IsChecked);
        Assert.False(Item(coat.Items, "Default").IsChecked);
        Click(Item(coat.Items, "Calico"));
        Assert.Equal(@"C:\Code\App", got!.Value.Cwd);
        Assert.Equal("Calico", got.Value.Look.Coat);
    });

    [Fact] public void Sardine_wand_and_send_away_run_their_hooks_and_the_wand_shows_the_play_state() => Sta(() =>
    {
        int sardines = 0, toggles = 0, away = 0; bool playing = true;
        var m = new ContextMenu();
        CatMenu.Fill(m.Items, @"C:\Code\App", new CatLook(), false, Trait.Lazy, () => { }, () => away++,
            new((_, _) => { }, () => [], () => { }, () => sardines++, () => playing, () => toggles++, () => new AppSettings(), _ => { }));
        Assert.True(Item(m.Items, "Teaser wand").IsChecked);
        Click(Item(m.Items, "Give sardine")); Click(Item(m.Items, "Teaser wand")); Click(Item(m.Items, "Send away (until next activity)"));
        Assert.Equal((1, 1, 1), (sardines, toggles, away));
        playing = false;
        CatMenu.Fill(m.Items, @"C:\Code\App", new CatLook(), false, Trait.Lazy, () => { }, () => { },
            new((_, _) => { }, () => [], () => { }, () => { }, () => playing, () => { }, () => new AppSettings(), _ => { }));
        Assert.False(Item(m.Items, "Teaser wand").IsChecked);
    });

    [Fact] public void Hidden_cats_are_listed_in_the_cat_menu_and_a_click_shows_one_again() => Sta(() =>
    {
        var hidden = new CatLook { Folder = @"C:\Code\Old", Hidden = true };
        string? at = null; CatLook? got = null;
        var m = new ContextMenu();
        CatMenu.Fill(m.Items, @"C:\Code\App", new CatLook(), false, Trait.Lazy, () => { }, () => { },
            new((cwd, f) => { at = cwd; got = f(hidden); }, () => [hidden], () => { }, () => { }, () => false, () => { }, () => new AppSettings(), _ => { }));
        var list = Item(m.Items, "Hidden cats");
        Assert.True(list.IsEnabled);
        Click((MenuItem)list.Items[0]!);
        Assert.Equal(@"C:\Code\Old", at);
        Assert.False(got!.Hidden);
    });
}
