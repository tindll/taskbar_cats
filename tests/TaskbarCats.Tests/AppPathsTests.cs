using TaskbarCats;

namespace TaskbarCats.Tests;

public class AppPathsTests
{
    const string Root = @"C:\Users\me\AppData\Local\TaskbarCats.App";

    [Fact] public void Velopack_install_maps_versioned_dir_to_current()
    {
        Assert.Equal(Root + @"\current\TaskbarCats.exe", AppPaths.StableExe(Root + @"\app-5.0.1\TaskbarCats.exe", Root));
        Assert.Equal(Root + @"\current\TaskbarCats.exe", AppPaths.StableExe(Root + @"\current\TaskbarCats.exe", Root + @"\"));
    }

    [Fact] public void Portable_or_foreign_exe_keeps_its_own_path()
    {
        const string P = @"D:\tools\TaskbarCats.exe";
        Assert.Equal(P, AppPaths.StableExe(P, null));
        Assert.Equal(P, AppPaths.StableExe(P, Root));
        Assert.Equal(Root + @"X\a.exe", AppPaths.StableExe(Root + @"X\a.exe", Root));   // sibling folder, not under root
    }

    /// Velopack deletes %LOCALAPPDATA%\<packId> on uninstall; the hook events live in %LOCALAPPDATA%\TaskbarCats.
    [Fact] public void Pack_id_differs_from_the_events_folder()
    {
        Assert.NotEqual("taskbarcats", AppInfo.PackId.ToLowerInvariant());
        Assert.NotEqual(AppInfo.PackId.ToLowerInvariant(), new DirectoryInfo(Hook.DefaultDir).Parent!.Name.ToLowerInvariant());
    }

    [Fact] public void Pack_script_uses_the_same_pack_id()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "TaskbarCats.slnx"))) dir = dir.Parent!;
        Assert.Contains($"'{AppInfo.PackId}'", File.ReadAllText(Path.Combine(dir.FullName, "tools", "pack.ps1")));
    }
}
