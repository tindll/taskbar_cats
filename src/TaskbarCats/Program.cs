using Velopack;

namespace TaskbarCats;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Hook calls come from the coding tools many times a minute: answer before any mutex or WPF work.
        if (args.Length > 0 && args[0] == Hook.Verb) return Hook.Run(args);
        if (args.Length > 0 && args[0] == "hooks") return HookInstaller.Cli(args);
        // Velopack next (its arguments never collide with the verbs above): on install/update/uninstall Setup/Update.exe
        // start this exe with hook arguments; Run() handles them and exits before the mutex or a window.
        // On a normal start it returns at once.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => SetAutostart(true))        // on by default; the tray menu turns it off
            .OnAfterUpdateFastCallback(_ => Quietly(() => HookInstaller.Refresh(UserHome(), Env, HookInstaller.ExeArg(AppPaths.Exe, Native.ShortPath))))   // new path, same tools; never a fresh install
            .OnBeforeUninstallFastCallback(_ =>
            {
                SetAutostart(false);
                Quietly(() => HookInstaller.Uninstall(UserHome(), Env));
            })
            .Run();
        using var mutex = new Mutex(true, @"Local\TaskbarCats", out bool first);
        if (!first) return 0;
        // One strip window per monitor, opened and closed as monitors come and go: only "Exit" ends the app.
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        TrayIcon? tray = null;
        app.Startup += (_, _) => { var herd = new Herd(); herd.Start(); tray = new TrayIcon(herd.Hooks()); };
        app.Exit += (_, _) => tray?.Dispose();
        app.Run();
        return 0;
    }

    static string UserHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string? Env(string k) => Environment.GetEnvironmentVariable(k);

    // Velopack hooks must never throw: a failed hook aborts the install/update/uninstall.
    static void Quietly(Action a) { try { a(); } catch { } }

    static void SetAutostart(bool on) => Quietly(() => { using var run = Autostart.OpenRun(); Autostart.Set(run, on, Autostart.Exe); });
}
