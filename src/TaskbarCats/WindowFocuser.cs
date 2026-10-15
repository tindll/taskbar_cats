using System.Diagnostics;
using System.IO;

namespace TaskbarCats;

public static class WindowFocuser
{
    /// The only text this app ever types: a fixed prompt. /haiku is the user's own skill that answers on haiku.
    public const string Praise = "/haiku good job, that's really good";
    const int FocusTimeoutMs = 2000, PollMs = 100, SettleMs = 300;
    /// "<active tab> - <folder> - <app>" or "<folder> - <app>": VS Code (verified) and Cursor (same Electron title rule, unverified).
    public static bool TitleMatches(string title, string cwd, string app = "Visual Studio Code")
    {
        var parts = title.Split(" - ");
        var folder = Path.GetFileName(cwd.TrimEnd('\\', '/'));
        return parts.Length >= 2 && parts[^1] == app
            && string.Equals(parts[^2], folder, StringComparison.OrdinalIgnoreCase);
    }

    /// Fallback for terminals whose window the process tree did not lead to: the title mentions the folder.
    public static bool TerminalTitleMatches(string title, string cwd)
    {
        var folder = Path.GetFileName(cwd.TrimEnd('\\', '/'));
        return folder.Length > 0 && title.Contains(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// Only a GUID ever reaches the shell: the session file is outside our control.
    public static string? SessionUri(string sessionId) =>
        Guid.TryParse(sessionId, out var g) ? $"vscode://anthropic.claude-code/open?session={g}" : null;

    /// True when a window title says this conversation's own tab is the active one in the VS Code window of cwd:
    /// "<tab title> - <folder> - Visual Studio Code", where VS Code cuts a long tab title with "…".
    public static bool ShowsConversation(string windowTitle, string cwd, string? conversationTitle)
    {
        if (string.IsNullOrEmpty(conversationTitle) || !TitleMatches(windowTitle, cwd)) return false;
        var parts = windowTitle.Split(" - ");
        if (parts.Length < 3) return false;                       // "<folder> - Visual Studio Code": no tab open
        var tab = string.Join(" - ", parts[..^2]);               // a conversation title may itself contain " - "
        bool cut = tab.EndsWith('…');
        tab = tab.TrimEnd('…');
        return tab.Length > 0 && (cut ? conversationTitle.StartsWith(tab, StringComparison.Ordinal) : conversationTitle == tab);
    }

    /// Never type into a conversation that is waiting on a permission prompt: Enter could answer it.
    public static bool MayType(SessionStatus status) => status != SessionStatus.Waiting;

    /// Opens the conversation, waits until VS Code's foreground window shows that conversation's tab, then types
    /// Praise and Enter. Types nothing (returns false) when the window is not found, or does not show the
    /// conversation within FocusTimeoutMs, or loses it during the SettleMs pause before typing.
    public static async Task<bool> SendPraiseAsync(SessionInfo s, string? conversationTitle)
    {
        var h = await FocusAsync(s);
        if (h == IntPtr.Zero || string.IsNullOrEmpty(conversationTitle)) return false;
        bool Ready() => Native.ForegroundWindow() == h && ShowsConversation(Native.WindowText(h), s.Cwd, conversationTitle);
        for (int waited = 0; waited < FocusTimeoutMs; waited += PollMs)
        {
            await Task.Delay(PollMs);
            if (!Ready()) continue;
            await Task.Delay(SettleMs);                           // the webview moves focus into its prompt box
            return Ready() && Native.TypeLine(Praise);           // checked again right before the first key
        }
        return false;
    }

    /// Brings the conversation's VS Code window forward and fires the open-session link. Returns that window, or
    /// IntPtr.Zero when no window title matched (the link still opens the session wherever VS Code picks).
    public static async Task<IntPtr> FocusAsync(SessionInfo s)
    {
        var h = WindowFor(s);
        if (h != IntPtr.Zero)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            if (!Native.SetForegroundWindow(h)) { Native.TapAlt(); Native.SetForegroundWindow(h); }
            await Task.Delay(150);   // VS Code routes URIs to the last-focused window; let it register first
        }
        // ponytail: if no window matched (custom window.title), the URI still opens the session in whichever window VS Code picks.
        if (s.Tool == AgentTool.Claude && s.Host != "terminal" && SessionUri(s.SessionId) is { } uri)
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return h;
    }

    /// The conversation's window is the foreground window right now.
    public static bool IsFocused(SessionInfo s) => WindowFor(s) is var h && h != IntPtr.Zero && h == Native.ForegroundWindow();

    static IntPtr WindowFor(SessionInfo s) => s.Host switch
    {
        "cursor" => FindWindow(t => TitleMatches(t, s.Cwd, "Cursor")),
        "terminal" => TerminalWindow(s),
        _ => FindWindow(t => TitleMatches(t, s.Cwd)),
    };

    /// The agent process or the nearest ancestor that owns a window (conhost / Windows Terminal), else a title match.
    // ponytail: Windows Terminal tabs share one window, so this raises the window, not the tab.
    static IntPtr TerminalWindow(SessionInfo s)
    {
        if (s.Pid is int pid)
            foreach (var p in Native.Ancestors(pid).Select(a => a.Pid).Prepend(pid))
                if (Native.TopWindowOf(p) is var h && h != IntPtr.Zero) return h;
        return FindWindow(t => TerminalTitleMatches(t, s.Cwd));
    }

    static IntPtr FindWindow(Func<string, bool> match)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h) && match(Native.WindowText(h))) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
