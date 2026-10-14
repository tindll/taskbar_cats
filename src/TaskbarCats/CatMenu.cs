using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskbarCats;

/// The cat's right-click menu. Every per-project option lives here; there is no settings window (v5). Herd rebuilds
/// it each time it opens, so the checks always show the saved look. The tray icon has its own short menu (TrayIcon).
public static class CatMenu
{
    /// Change(cwd, f): save f(look) for cwd's project and restyle its cats. Hidden(): hidden projects. Exit(): quit.
    /// GiveSardine(): the sardine goes on the cursor. Playing() / TogglePlay(): the teaser wand (play mode), app-wide.
    /// Settings() / SetSettings(s): the tray's app-wide switches (hover cards, sounds), saved on every change.
    public sealed record Hooks(Action<string, Func<CatLook, CatLook>> Change, Func<IReadOnlyList<CatLook>> Hidden, Action Exit,
        Action GiveSardine, Func<bool> Playing, Action TogglePlay, Func<AppSettings> Settings, Action<AppSettings> SetSettings);

    /// trait = the session-hash personality, shown as the default.
    public static void Fill(ItemCollection items, string cwd, CatLook look, bool pinned, Trait trait, Action togglePin, Action sendAway, Hooks h)
    {
        void Set(Func<CatLook, CatLook> f) => h.Change(cwd, f);
        items.Clear();
        items.Add(Action("Give sardine", h.GiveSardine));
        items.Add(Check("Teaser wand", h.Playing(), _ => h.TogglePlay()));
        items.Add(Action("Send away (until next activity)", sendAway));
        items.Add(new Separator());
        var stay = new MenuItem { Header = pinned ? "Free" : "Stay here" };
        stay.Click += (_, _) => togglePin();
        items.Add(stay);
        items.Add(new Separator());
        items.Add(new MenuItem { Header = $"Customize {CatLooks.FolderName(cwd)}", IsEnabled = false });
        items.Add(NameBox(look, Set));
        items.Add(Choice("Coat", look.Coat, Sprites.Coats.Select(c => (c.Name, (object)c.Name, (UIElement?)Swatch(c.P.Fur))),
            v => Set(l => l with { Coat = (string?)v })));
        items.Add(Check("Plain coat (no stripes)", look.Plain, b => Set(l => l with { Plain = b })));
        items.Add(Choice("Size", look.Size, [("Small", Look.Kitten, null), ("Normal", Look.Cat, null), ("Big", Look.Big, null)],
            v => Set(l => l with { Size = (Look?)v }), "Default (by model)"));
        items.Add(Choice("Personality", look.Trait, Enum.GetValues<Trait>().Select(t => (t.ToString(), (object)t, (UIElement?)null)),
            v => Set(l => l with { Trait = (Trait?)v }), $"Default ({trait.ToString().ToLowerInvariant()})"));
        items.Add(Choice("Collar", look.Collar, Sprites.Collars.Select(c => (c.Name, (object)c.Name, (UIElement?)Swatch(c.Colour))),
            v => Set(l => l with { Collar = (string?)v }), "None"));
        items.Add(Choice("Favourite toy", look.Toy, Enum.GetValues<Toy>().Select(t => (t.ToString(), (object)t, (UIElement?)null)),
            v => Set(l => l with { Toy = (Toy?)v }), "Default (takes turns)"));
        items.Add(Check("Quiet (no blinking MEOW)", look.Quiet, b => Set(l => l with { Quiet = b })));
        items.Add(Action("Randomize look", () => Set(l => CatLooks.Randomize(l, Random.Shared))));
        items.Add(Action("Reset to default", () => Set(_ => new CatLook())));
        items.Add(new Separator());
        items.Add(Action("Hide this project's cat", () => Set(l => l with { Hidden = true })));
        items.Add(HiddenMenu(h));
        items.Add(new Separator());
        items.Add(Action("Exit", h.Exit));
    }

    static MenuItem Action(string header, Action act)
    {
        var i = new MenuItem { Header = header };
        i.Click += (_, _) => act();
        return i;
    }

    static MenuItem Check(string header, bool on, Action<bool> set)
    {
        var i = new MenuItem { Header = header, IsChecked = on };
        i.Click += (_, _) => set(!on);
        return i;
    }

    /// A submenu: the default first (checked when nothing is chosen), then each option, the current one checked.
    static MenuItem Choice(string header, object? current, IEnumerable<(string Label, object Value, UIElement? Icon)> options,
        Action<object?> pick, string def = "Default")
    {
        var menu = new MenuItem { Header = header };
        var d = new MenuItem { Header = def, IsChecked = current is null };
        d.Click += (_, _) => pick(null);
        menu.Items.Add(d);
        foreach (var (label, value, icon) in options)
        {
            var i = new MenuItem { Header = label, Icon = icon, IsChecked = Equals(current, value) };
            i.Click += (_, _) => pick(value);
            menu.Items.Add(i);
        }
        return menu;
    }

    static Rectangle Swatch(uint argb) => new()
    {
        Width = 12, Height = 12, Stroke = Brushes.Black, StrokeThickness = 1,
        Fill = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)),
    };

    /// The name tag. A menu cannot hold a text box (clicking it makes the popup take focus and WPF closes the menu),
    /// so this opens a small window. Enter or OK saves; an empty box removes the tag (CatLooks.CleanName).
    static MenuItem NameBox(CatLook look, Action<Func<CatLook, CatLook>> set)
    {
        var i = new MenuItem { Header = look.Name is { Length: > 0 } n ? $"Name tag: {n}..." : "Name tag..." };
        i.Click += (_, _) =>
        {
            var box = new TextBox { Text = look.Name ?? "", MaxLength = Tune.NameMax, MinWidth = 200, Margin = new Thickness(0, 0, 0, 8) };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 70, HorizontalAlignment = HorizontalAlignment.Right };
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(box);
            panel.Children.Add(ok);
            var w = new Window
            {
                Title = "Name tag", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                Topmost = true, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            ok.Click += (_, _) =>
            {
                var name = CatLooks.CleanName(box.Text);
                set(l => l with { Name = name });
                w.Close();
            };
            w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
            w.Show();
            w.Activate();
        };
        return i;
    }

    static MenuItem HiddenMenu(Hooks h)
    {
        var hidden = h.Hidden();
        var menu = new MenuItem { Header = "Hidden cats", IsEnabled = hidden.Count > 0 };
        foreach (var l in hidden)
        {
            var i = new MenuItem { Header = $"Show {CatLooks.FolderName(l.Folder)}", ToolTip = l.Folder };
            i.Click += (_, _) => h.Change(l.Folder, x => x with { Hidden = false });
            menu.Items.Add(i);
        }
        return menu;
    }
}
