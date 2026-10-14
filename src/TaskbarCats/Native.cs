using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarCats;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    internal delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr m, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    // SendInput's INPUT: a type tag plus a union sized by its largest member (MOUSEINPUT), 40 bytes on x64.
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion u; }
    internal const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    internal const ushort VK_RETURN = 0x0D;

    public const int SW_RESTORE = 9;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    /// Key events that type text as Unicode characters (the same on any keyboard layout, no clipboard), then Enter.
    internal static INPUT[] TypingInputs(string text)
    {
        static INPUT Key(ushort vk, ushort scan, uint flags) =>
            new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };
        var list = new List<INPUT>();
        foreach (char ch in text)
        {
            list.Add(Key(0, ch, KEYEVENTF_UNICODE));
            list.Add(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        list.Add(Key(VK_RETURN, 0, 0));
        list.Add(Key(VK_RETURN, 0, KEYEVENTF_KEYUP));
        return [.. list];
    }

    /// Types text then Enter into whatever window has keyboard focus. True when Windows took every event
    /// (false when another app's input, or a higher-integrity window such as an admin prompt, blocked it).
    public static bool TypeLine(string text)
    {
        var inputs = TypingInputs(text);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    public static string WindowText(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, s.Capacity); return s.ToString(); }
    static string ClassName(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, s.Capacity); return s.ToString(); }

    static void AddExStyle(IntPtr h, long bits) =>
        SetWindowLongPtr(h, GWL_EXSTYLE, (IntPtr)((long)GetWindowLongPtr(h, GWL_EXSTYLE) | bits));

    /// Hide from Alt+Tab.
    public static void MakeToolWindow(IntPtr h) => AddExStyle(h, WS_EX_TOOLWINDOW);

    /// Mouse clicks go straight through to whatever is underneath, and the window never takes focus.
    public static void MakeClickThrough(IntPtr h) => AddExStyle(h, WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

    /// Place a window in physical pixels, topmost, without activating it. WPF's Left/Top are DIPs of the
    /// window's current monitor, which is wrong for a window that has not moved to its monitor yet.
    public static void PlaceTopmost(IntPtr h, RECT r)
    {
        const uint SWP_NOACTIVATE = 0x10;
        SetWindowPos(h, new IntPtr(-1) /*HWND_TOPMOST*/, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOACTIVATE);
    }

    /// The physical left button (VK_LBUTTON), whether or not any window has captured the mouse.
    // ponytail: ignores the "swap mouse buttons" setting; fine for a right-handed setup.
    public static bool LeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    /// Windows only lets the foreground process steal focus; a synthetic Alt tap is the documented-in-practice unlock.
    public static void TapAlt()
    {
        const byte VK_MENU = 0x12; const uint KEYUP = 2;
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYUP, UIntPtr.Zero);
    }

    /// Esc or the right mouse button is down (cancels a carried sardine).
    public static bool EscOrRightDown() => (GetAsyncKeyState(0x1B) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;

    public static bool IsFullscreen(RECT win, RECT mon) =>
        win.Left <= mon.Left && win.Top <= mon.Top && win.Right >= mon.Right && win.Bottom >= mon.Bottom;

    /// Device name (e.g. \\.\DISPLAY2) of the monitor a fullscreen foreground window covers, else null.
    public static string? FullscreenMonitor()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || ClassName(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        if (!GetWindowRect(h, out var r)) return null;
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(MonitorFromWindow(h, 2 /*NEAREST*/), ref mi) && IsFullscreen(r, mi.rcMonitor) ? mi.szDevice : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetShortPathName(string l, StringBuilder s, uint n);
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    /// Parent, grandparent, ... of pid (nearest first, at most max), from one Toolhelp snapshot.
    // ponytail: a dead parent's pid can be reused by an unrelated process; start-time check if that ever bites.
    public static List<(int Pid, string Exe)> Ancestors(int pid, int max = 12)
    {
        var all = new Dictionary<int, (int Parent, string Exe)>();
        var snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return [];
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                all[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile);
        }
        finally { CloseHandle(snap); }
        var chain = new List<(int, string)>();
        var seen = new HashSet<int> { pid };
        int cur = pid;
        while (chain.Count < max && all.TryGetValue(cur, out var me) && seen.Add(me.Parent) && all.TryGetValue(me.Parent, out var up))
        {
            chain.Add((me.Parent, up.Exe));
            cur = me.Parent;
        }
        return chain;
    }

    /// The first visible, titled top-level window owned by pid, or IntPtr.Zero.
    public static IntPtr TopWindowOf(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindowThreadProcessId(h, out var p) != 0 && p == pid && WindowText(h).Length > 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string? ShortPath(string path)
    {
        var sb = new StringBuilder(1024);
        return GetShortPathName(path, sb, (uint)sb.Capacity) is > 0 and < 1024 ? sb.ToString() : null;
    }

    /// Lets the WinExe print to the console it was started from (hooks CLI verb).
    public static bool AttachParentConsole() => AttachConsole(-1);
}
