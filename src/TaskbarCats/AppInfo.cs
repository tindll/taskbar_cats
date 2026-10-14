namespace TaskbarCats;

/// The one place the app learns its own version (from csproj <Version>), install id and home page.
public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version!.ToString(3);

    /// Velopack installs to %LOCALAPPDATA%\PackId and deletes that folder on uninstall: must never be "TaskbarCats",
    /// where the hook events live. tools/pack.ps1 passes the same id.
    public const string PackId = "TaskbarCats.App";

    /// Home page, and the GitHub Releases feed Check for updates reads.
    public const string RepoUrl = "https://github.com/tindll/taskbar_cats";
}
