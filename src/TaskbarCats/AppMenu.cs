using Velopack;
using Velopack.Sources;

namespace TaskbarCats;

/// App-level tray items (not about one cat): Start with Windows, Check for updates, About. WinForms types are fully
/// qualified like in TrayIcon. The tray menu is rebuilt on every open, so the check mark is always current.
public static class AppMenu
{
    internal static IEnumerable<System.Windows.Forms.ToolStripItem> Items()
    {
        bool on = false;
        try { using var run = Autostart.OpenRun(); on = Autostart.IsOn(run, Autostart.Exe); } catch { }
        var autostart = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { Checked = on, CheckOnClick = true };
        autostart.Click += (_, _) =>
        {
            try { using var run = Autostart.OpenRun(); Autostart.Set(run, autostart.Checked, Autostart.Exe); }
            catch (Exception e) { System.Windows.MessageBox.Show("Couldn't change Start with Windows: " + e.Message, "taskbar-cats"); }
        };
        return
        [
            autostart,
            new System.Windows.Forms.ToolStripMenuItem("Check for updates", null, async (_, _) => await CheckForUpdates()),
            new System.Windows.Forms.ToolStripMenuItem($"About taskbar-cats v{AppInfo.Version}", null, (_, _) =>
                System.Windows.MessageBox.Show(
                    $"taskbar-cats v{AppInfo.Version}\n{AppInfo.RepoUrl}\n\n" +
                    "Pixel cats for your AI coding sessions. Everything is read locally; " +
                    "nothing is sent anywhere unless you click Check for updates.",
                    "taskbar-cats")),
        ];
    }

    /// The app's only network access, and only when clicked: GitHub Releases via Velopack.
    static async Task CheckForUpdates()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(AppInfo.RepoUrl, null, false));
            if (!mgr.IsInstalled)
            {
                System.Windows.MessageBox.Show("Updates work in the installed app (TaskbarCats-win-Setup.exe), not in this copy.", "taskbar-cats");
                return;
            }
            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                System.Windows.MessageBox.Show($"You have the latest version (v{AppInfo.Version}).", "taskbar-cats");
                return;
            }
            if (System.Windows.MessageBox.Show($"taskbar-cats v{info.TargetFullRelease.Version} is available. Download it and restart now?",
                    "taskbar-cats", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes) return;
            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);   // exits; Update.exe swaps files and starts the new version
        }
        catch (Exception e)
        {
            System.Windows.MessageBox.Show("Couldn't check for updates: " + e.Message, "taskbar-cats");
        }
    }
}
