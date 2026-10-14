using TaskbarCats;
using Xunit;

public class TrayIconTests
{
    static void Sta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw new Exception("failed on the STA thread", err);
    }

    static System.Windows.Forms.ToolStripMenuItem Item(System.Windows.Forms.ToolStripItemCollection items, string text) =>
        items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Text == text);

    // Only the switches, not BuildMenu: the whole menu would also read the HKCU Run key and the tools' hook configs.
    [Fact] public void Hover_cards_and_sounds_show_the_saved_switches_and_toggle_them() => Sta(() =>
    {
        var saved = new AppSettings { HoverCards = true, Sounds = false };
        AppSettings? set = null;
        var h = new CatMenu.Hooks((_, _) => { }, () => [], () => { }, () => { }, () => false, () => { }, () => saved, s => set = s);
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.AddRange(TrayIcon.SettingsItems(h));
        Assert.True(Item(menu.Items, "Hover cards").Checked);
        Assert.False(Item(menu.Items, "Sounds").Checked);
        Item(menu.Items, "Sounds").PerformClick();
        Assert.Equal(new AppSettings { HoverCards = true, Sounds = true }, set);
        Item(menu.Items, "Hover cards").PerformClick();
        Assert.Equal(new AppSettings { HoverCards = false, Sounds = false }, set);
    });
}
