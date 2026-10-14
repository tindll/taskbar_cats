using TaskbarCats;
using Xunit;

public class WindowFocuserTests
{
    const string Demo = @"c:\Users\me\Downloads\DemoGame\DemoGame";

    [Theory]
    [InlineData("Handoff movement - DemoGame - Visual Studio Code", true)]
    [InlineData("DemoGame - Visual Studio Code", true)]                              // no editor open
    [InlineData("● Program.cs - demogame - Visual Studio Code", true)]               // dirty marker, case
    [InlineData("Left navbar refinements - MonacoLogistique - Visual Studio Code", false)]
    [InlineData("notes - DemoGame - Notepad", false)]
    [InlineData("a - DemoGame - Other - Visual Studio Code", false)]                 // folder must be the segment before the app name
    [InlineData("", false)]
    public void Title_matching(string title, bool expected) =>
        Assert.Equal(expected, WindowFocuser.TitleMatches(title, Demo));

    [Fact] public void Trailing_slash_in_cwd_ok() =>
        Assert.True(WindowFocuser.TitleMatches("x - MyApp - Visual Studio Code", @"c:\Users\me\MyApp\"));

    [Fact] public void Session_uri_only_for_guids()
    {
        Assert.Equal("vscode://anthropic.claude-code/open?session=494d4841-0e3a-4fe4-92ab-24b6811922d0",
            WindowFocuser.SessionUri("494d4841-0e3a-4fe4-92ab-24b6811922d0"));
        Assert.Null(WindowFocuser.SessionUri("x&prompt=rm -rf"));
    }
    [Fact] public void Praise_is_the_fixed_haiku_prompt() =>
        Assert.Equal("/haiku good job, that's really good", WindowFocuser.Praise);

    const string App = @"c:\Users\me\MyApp";

    [Theory]
    [InlineData("Taskbar cat walker proje… - MyApp - Visual Studio Code", "Taskbar cat walker project", true)]   // real title, cut by VS Code
    [InlineData("Fix it - MyApp - Visual Studio Code", "Fix it", true)]
    [InlineData("Plan - v3 - MyApp - Visual Studio Code", "Plan - v3", true)]                // " - " inside the title
    [InlineData("Fix - MyApp - Visual Studio Code", "Fix it", false)]                         // not cut, so must be equal
    [InlineData("Herd.cs - MyApp - Visual Studio Code", "Taskbar cat walker project", false)] // a code tab is active
    [InlineData("● Herd.cs - MyApp - Visual Studio Code", "Taskbar cat walker project", false)]
    [InlineData("MyApp - Visual Studio Code", "Taskbar cat walker project", false)]          // no tab at all
    [InlineData("… - MyApp - Visual Studio Code", "Taskbar cat walker project", false)]
    [InlineData("Taskbar cat walker proje… - OtherApp - Visual Studio Code", "Taskbar cat walker project", false)]
    [InlineData("Taskbar cat walker proje… - MyApp - Visual Studio Code", null, false)]       // title unknown: never type
    public void Shows_conversation_only_when_its_own_tab_is_active(string window, string? conversation, bool expected) =>
        Assert.Equal(expected, WindowFocuser.ShowsConversation(window, App, conversation));

    [Theory]
    [InlineData(SessionStatus.Idle, true)]
    [InlineData(SessionStatus.Working, true)]      // queued after the current turn
    [InlineData(SessionStatus.Waiting, false)]     // Enter could answer the permission prompt
    public void Never_types_into_a_permission_prompt(SessionStatus s, bool expected) =>
        Assert.Equal(expected, WindowFocuser.MayType(s));

    [Fact] public void Typing_is_unicode_keys_then_enter()
    {
        var inputs = Native.TypingInputs("/h'");
        Assert.Equal(8, inputs.Length);
        Assert.All(inputs, i => Assert.Equal(Native.INPUT_KEYBOARD, i.type));
        Assert.Equal(('/', Native.KEYEVENTF_UNICODE), ((char)inputs[0].u.ki.wScan, inputs[0].u.ki.dwFlags));
        Assert.Equal(Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP, inputs[1].u.ki.dwFlags);
        Assert.Equal('\'', (char)inputs[4].u.ki.wScan);
        Assert.Equal((Native.VK_RETURN, 0u), (inputs[6].u.ki.wVk, inputs[6].u.ki.dwFlags));
        Assert.Equal((Native.VK_RETURN, Native.KEYEVENTF_KEYUP), (inputs[7].u.ki.wVk, inputs[7].u.ki.dwFlags));
    }

    [Fact] public void Input_struct_matches_the_win32_size() =>
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());

    [Theory]
    [InlineData("Program.cs - Proj - Cursor", true)]
    [InlineData("Proj - Cursor", true)]
    [InlineData("Program.cs - Proj - Visual Studio Code", false)]
    [InlineData("Program.cs - Other - Cursor", false)]
    public void Cursor_title(string title, bool ok) => Assert.Equal(ok, WindowFocuser.TitleMatches(title, @"c:\x\Proj", "Cursor"));

    [Theory]
    [InlineData(@"PS C:\x\Proj>", true)]
    [InlineData("codex - Proj", true)]
    [InlineData("Windows PowerShell", false)]
    [InlineData("", false)]
    public void Terminal_title(string title, bool ok) => Assert.Equal(ok, WindowFocuser.TerminalTitleMatches(title, @"c:\x\Proj"));
}
