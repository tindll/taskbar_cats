using Microsoft.Win32;

namespace TaskbarCats;

/// Start with Windows = one value in HKCU\...\Run. Callers pass the key so tests can use a scratch key.
public static class Autostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "TaskbarCats";

    public static string Exe => AppPaths.Exe;

    public static RegistryKey OpenRun() => Registry.CurrentUser.CreateSubKey(RunKeyPath);

    public static bool IsOn(RegistryKey run, string exe) =>
        run.GetValue(ValueName) is string v && string.Equals(v.Trim().Trim('"'), exe, StringComparison.OrdinalIgnoreCase);

    public static void Set(RegistryKey run, bool on, string exe)
    {
        if (on) run.SetValue(ValueName, $"\"{exe}\"");
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
