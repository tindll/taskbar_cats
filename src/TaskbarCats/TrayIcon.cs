namespace TaskbarCats;

/// The notification-area icon: the app stays controllable when every cat is hidden or none is active. Its tooltip is
/// "taskbar-cats"; right-click opens the menu BuildMenu fills. WinForms types are fully qualified on purpose (they clash
/// with WPF's), and Dispose removes the icon so no ghost is left behind.
public sealed class TrayIcon : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly System.Drawing.Icon _art = SitIcon();

    public TrayIcon(CatMenu.Hooks h)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Opening += (_, _) => BuildMenu(menu.Items, h);   // rebuilt on every open: the checks and the hidden list are current
        _icon = new System.Windows.Forms.NotifyIcon { Icon = _art, Text = "taskbar-cats", ContextMenuStrip = menu, Visible = true };
    }

    /// The one place the tray menu is built: later plans add "Start with Windows", "Hooks", "Check for updates" and
    /// "About" here.
    internal static void BuildMenu(System.Windows.Forms.ToolStripItemCollection items, CatMenu.Hooks h)
    {
        items.Clear();
        items.Add(new System.Windows.Forms.ToolStripMenuItem("Teaser wand", null, (_, _) => h.TogglePlay()) { Checked = h.Playing() });
        items.AddRange(SettingsItems(h));
        var hidden = h.Hidden();
        var list = new System.Windows.Forms.ToolStripMenuItem("Hidden cats") { Enabled = hidden.Count > 0 };
        foreach (var l in hidden)
            list.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem($"Show {CatLooks.FolderName(l.Folder)}", null,
                (_, _) => h.Change(l.Folder, x => x with { Hidden = false })) { ToolTipText = l.Folder });
        items.Add(list);
        items.Add(ToolsMenu());
        items.Add(new System.Windows.Forms.ToolStripSeparator());
        foreach (var i in AppMenu.Items()) items.Add(i);
        items.Add(new System.Windows.Forms.ToolStripSeparator());
        items.Add(new System.Windows.Forms.ToolStripMenuItem("Exit", null, (_, _) => h.Exit()));
    }

    /// The app-wide switches (AppSettings). s is read at open time; the menu is rebuilt on every open, so it is current.
    internal static System.Windows.Forms.ToolStripItem[] SettingsItems(CatMenu.Hooks h)
    {
        var s = h.Settings();
        return
        [
            new System.Windows.Forms.ToolStripMenuItem("Hover cards", null, (_, _) => h.SetSettings(s with { HoverCards = !s.HoverCards })) { Checked = s.HoverCards },
            new System.Windows.Forms.ToolStripMenuItem("Sounds", null, (_, _) => h.SetSettings(s with { Sounds = !s.Sounds })) { Checked = s.Sounds },
        ];
    }

    /// Other coding tools get cats through hooks: a status line (rebuilt on every open), install, remove.
    static System.Windows.Forms.ToolStripMenuItem ToolsMenu()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Func<string, string?> env = Environment.GetEnvironmentVariable;
        var tools = new System.Windows.Forms.ToolStripMenuItem("Coding tools");
        tools.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(HookInstaller.Status(home, env)) { Enabled = false });
        tools.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Install hooks", null, (_, _) =>
            System.Windows.MessageBox.Show(string.Join("\n", HookInstaller.Install(home, env, HookInstaller.ExeArg(AppPaths.Exe, Native.ShortPath))), "Taskbar Cats")));
        tools.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Remove hooks", null, (_, _) =>
            System.Windows.MessageBox.Show(string.Join("\n", HookInstaller.Uninstall(home, env)), "Taskbar Cats")));
        return tools;
    }

    /// A sitting cat (profile), drawn from the sprite art at 2x nearest-neighbour: 32 px, crisp; Windows scales it down.
    static System.Drawing.Icon SitIcon()
    {
        var src = Sprites.Render(Sprites.Frames[Anim.Sit][0], Sprites.Palettes[0]);
        int px = src.PixelWidth, stride = px * 4;
        var data = new byte[stride * src.PixelHeight];
        src.CopyPixels(data, stride, 0);
        using var bmp = new System.Drawing.Bitmap(px * 2, px * 2);
        for (int y = 0; y < px * 2; y++)
            for (int x = 0; x < px * 2; x++)
            {
                int i = y / 2 * stride + x / 2 * 4;
                bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(data[i + 3], data[i + 2], data[i + 1], data[i]));
            }
        var hicon = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hicon).Clone(); }
        finally { Native.DestroyIcon(hicon); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _art.Dispose();
    }
}
