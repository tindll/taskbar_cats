using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskbarCats;

/// What one hover card says. Value equality: the window redraws only when this changes. Lines are '\n'-joined.
public sealed record CardInfo(string Title, uint? Dot, string Lines, string? Warning);

/// The hover card's content and placement: pure, no WPF (the window is CardWindow, below). Reads only privacy-safe
/// fields: tool names, timestamps, counts, the model id, status flags, the API error code and the folder name.
public static class HoverCard
{
    public const string Footer = "click: open · right-click: menu";

    /// run = Mood.Run; subagents = Transcripts.RunningSubagents. Hook-reported cats leave edits, errors, context empty.
    public static CardInfo For(SessionInfo s, Signals? sig, string? name, TimeSpan run, int subagents, DateTimeOffset now)
    {
        bool claude = s.Tool == AgentTool.Claude;
        string title = (name is null ? "" : name + " · ") + CatLooks.FolderName(s.Cwd) + (claude ? "" : " · " + Tune.ToolLabel(s.Tool));
        var lines = new List<string>();
        if (sig?.Model is { } model) lines.Add(Clip(model, 32));
        lines.Add(Status(s, sig, claude, now));
        var busy = new List<string>();
        if (run > TimeSpan.Zero) busy.Add("Busy " + Mins(run));
        if (claude && sig is not null)
        {
            if (sig.Edits > 0) busy.Add(Count(sig.Edits, "edit"));
            if (sig.Errors > 0) busy.Add(Count(sig.Errors, "error"));
        }
        if (busy.Count > 0) lines.Add(string.Join(" · ", busy));
        if (subagents > 0) lines.Add(Count(subagents, "sub-agent") + " running");
        if (sig is { MaxContext: > 0 }) lines.Add(ContextBar(sig.Context, sig.MaxContext));
        string? warning = sig?.ApiError switch
        {
            null => null,
            "rate_limit" => "! Rate limited: waiting to retry",
            var e => "! API error: " + Clip(e, 24),
        };
        return new CardInfo(title, Tune.ToolColor(s.Tool), string.Join("\n", lines), warning);
    }

    static string Status(SessionInfo s, Signals? sig, bool claude, DateTimeOffset now)
    {
        switch (s.Status)
        {
            case SessionStatus.Waiting: return "Waiting for you: " + Mins(now - s.UpdatedAt);
            case SessionStatus.Working:
                if (sig is { Tool: { } tool, ToolAt: { } at })
                {
                    // Hooks report a tool after it ran (PostToolUse); Claude's transcript knows which call is still open.
                    if (!claude) return $"Working · last: {Clip(tool, 24)} {Clock(now - at)} ago";
                    if (sig.Open > 0) return $"Working: {Clip(tool, 24)} {Clock(now - at)} on it";
                }
                return claude && sig is not null ? "Working: thinking" : "Working";
            default:
                return (sig?.LastHuman ?? s.LastHumanAt) is { } typed ? $"Idle: you last typed {Mins(now - typed)} ago" : "Idle";
        }
    }

    static string ContextBar(long context, long max)
    {
        int pct = (int)Math.Clamp(Math.Round(100.0 * context / Tune.ContextWindow(context, max)), 0, 100);
        int full = (pct + 5) / 10;
        // ASCII on purpose: block glyphs fall back to another font in the card and make that line taller
        return $"Context [{new string('#', full)}{new string('-', 10 - full)}] {pct}%";
    }

    /// "<1 min", "12 min", "2 h 5 min". Negative (clock skew) counts as zero.
    public static string Mins(TimeSpan t)
    {
        int m = (int)Math.Max(0, t.TotalMinutes);
        return m < 1 ? "<1 min" : m < 60 ? $"{m} min" : $"{m / 60} h {m % 60} min";
    }

    /// "1:05", "1:00:00". Negative counts as zero.
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// Switched on, rested CardDelay on a cat, no teaser wand, no cat menu open. (Press, carry and sardine already
    /// clear the hovered cat in Herd.Hover.)
    public static bool Shows(bool enabled, double restedFor, bool playing, bool menuOpen) =>
        enabled && !playing && !menuOpen && restedFor >= Tune.CardDelay - 1e-9;

    /// Physical px: card w×h centred on anchorX with its bottom at anchorY, kept inside the monitor.
    public static (int Left, int Top) Place(int anchorX, int anchorY, int w, int h, Native.RECT monitor) =>
        (Math.Clamp(anchorX - w / 2, monitor.Left, Math.Max(monitor.Left, monitor.Right - w)),
         Math.Max(monitor.Top, anchorY - h));

    static string Count(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

/// The hover card: a dark pixel-style box in its own topmost window, like DragWindow. Click-through and never
/// activated (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE via Native.MakeClickThrough), so it can never take focus or a click
/// meant for the desktop. Placed in physical px (the strips do the same).
public sealed class CardWindow : Window
{
    static readonly Brush Back = Frozen(0xF0202028), Edge = Frozen(0xFF9A9A9A), Fg = Frozen(0xFFEDEDED),
        Dim = Frozen(0xFF8A8A8A), Warn = Frozen(0xFFFFC857);
    static readonly FontFamily Mono = new("Consolas");
    readonly StackPanel _panel = new();
    readonly Border _box;
    CardInfo? _info;
    IntPtr _hwnd;

    public CardWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false; Focusable = false;
        Width = 1; Height = 1;
        _box = new Border { Background = Back, BorderBrush = Edge, BorderThickness = new Thickness(2), Padding = new Thickness(6, 4, 6, 4),
            Child = _panel, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        TextOptions.SetTextRenderingMode(_box, TextRenderingMode.Aliased);   // crisp, pixel-ish
        Content = _box;
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; Native.MakeClickThrough(_hwnd); };
    }

    /// anchorX/anchorY: physical px where the card's bottom centre goes; scale and monitor: the cat's screen.
    public void ShowFor(CardInfo c, double anchorX, double anchorY, double scale, Native.RECT monitor)
    {
        if (c != _info) { _info = c; Fill(_panel, c); }
        if (!IsVisible) Show();
        _box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int w = (int)Math.Ceiling(_box.DesiredSize.Width * scale), h = (int)Math.Ceiling(_box.DesiredSize.Height * scale);
        var (left, top) = HoverCard.Place((int)Math.Round(anchorX), (int)Math.Round(anchorY), w, h, monitor);
        Native.PlaceTopmost(_hwnd, new Native.RECT { Left = left, Top = top, Right = left + w, Bottom = top + h });
    }

    public void Off() { if (IsVisible) Hide(); }

    internal static void Fill(Panel p, CardInfo c)
    {
        p.Children.Clear();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        if (c.Dot is uint d)
            title.Children.Add(new Border { Width = 6, Height = 6, Background = Frozen(d), BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Text(c.Title, Fg, bold: true));
        p.Children.Add(title);
        p.Children.Add(Text(c.Lines, Fg));
        if (c.Warning is { } w) p.Children.Add(Text(w, Warn));
        p.Children.Add(Text(HoverCard.Footer, Dim));
    }

    static TextBlock Text(string s, Brush b, bool bold = false) =>
        new() { Text = s, Foreground = b, FontFamily = Mono, FontSize = 12, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };

    static Brush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
