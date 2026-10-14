namespace TaskbarCats;

/// The exe path other things remember (the Run value, tool hook commands). Under Velopack every update lives in a new
/// folder, but %LOCALAPPDATA%\<packId>\current\ is swapped in place and survives updates, so that is what gets stored.
/// A portable exe keeps its own path.
public static class AppPaths
{
    public static string StableExe(string processPath, string? veloRoot) =>
        veloRoot is { Length: > 0 } r && processPath.StartsWith(r.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.Combine(r, "current", System.IO.Path.GetFileName(processPath))
            : processPath;

    public static string Exe
    {
        get
        {
            string p = Environment.ProcessPath!;
            try { return StableExe(p, Velopack.Locators.VelopackLocator.IsCurrentSet ? Velopack.Locators.VelopackLocator.Current.RootAppDir : null); }
            catch { return p; }
        }
    }
}
