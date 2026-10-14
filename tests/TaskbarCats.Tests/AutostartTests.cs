using Microsoft.Win32;
using TaskbarCats;

namespace TaskbarCats.Tests;

/// Uses a scratch key under HKCU\Software\TaskbarCats.Tests, never the real Run key.
public sealed class AutostartTests : IDisposable
{
    readonly string _path = $@"Software\TaskbarCats.Tests\{Guid.NewGuid():N}";
    readonly RegistryKey _run;
    const string Exe = @"C:\Users\me\AppData\Local\TaskbarCats.App\current\TaskbarCats.exe";

    public AutostartTests() => _run = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _run.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Set_on_writes_the_quoted_exe_and_reads_back_on()
    {
        Autostart.Set(_run, true, Exe);
        Assert.Equal($"\"{Exe}\"", _run.GetValue(Autostart.ValueName));
        Assert.True(Autostart.IsOn(_run, Exe));
    }

    [Fact]
    public void Set_off_removes_the_value_and_is_safe_when_already_off()
    {
        Autostart.Set(_run, true, Exe);
        Autostart.Set(_run, false, Exe);
        Autostart.Set(_run, false, Exe);
        Assert.Null(_run.GetValue(Autostart.ValueName));
        Assert.False(Autostart.IsOn(_run, Exe));
    }

    [Fact]
    public void IsOn_false_when_value_points_to_another_exe()
    {
        _run.SetValue(Autostart.ValueName, @"""C:\dev\taskbar-cats\publish\v4.4\TaskbarCats.exe""");
        Assert.False(Autostart.IsOn(_run, Exe));
    }

    [Fact]
    public void IsOn_ignores_case_and_missing_quotes()
    {
        _run.SetValue(Autostart.ValueName, Exe.ToUpperInvariant());
        Assert.True(Autostart.IsOn(_run, Exe));
    }
}
