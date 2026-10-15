using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TaskbarCats;

/// Everything on screen for one conversation.
public sealed class CatView
{
    internal static readonly Brush HitBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));   // alpha 1: clickable, invisible

    public required Cat Cat;
    public required SessionInfo Session;
    public required Palette Palette;
    public CatLook Look = new();              // the project's right-click choices (Herd.Restyle)
    public required OverlayWindow Strip;
    public required Mood Mood;
    public SessionStatus LastStatus;          // status at the previous poll (send-away wake-up rule)
    public DateTimeOffset BinnedAt;
    public Signals? Signals;                  // the last poll's signals (transcript or hook), for the hover card
    public int Subagents;                     // the last poll's running sub-agents, unclamped (Mood.Kittens is clamped)
    internal DragWindow? Air;                 // draws this cat while it is carried or flying, on any monitor
    internal Flight? Flight;                  // set from the moment it is let go until it lands
    internal DragWindow? YarnAir;             // draws the yarn ball while it is carried or flying
    internal Flight? YarnFlight;              // the thrown yarn ball, until it lands
    internal (OverlayWindow To, double BallX)? YarnAcross;   // it landed on another monitor's strip: it lies there (Parked, clickable) while the cat runs over
    internal double YarnAcrossSince;          // clock time it was parked (Herd.YarnOrphaned)
    public readonly Image Parked = OverlayWindow.Pixelated();   // the parked ball, on that other strip
    // Invisible grab boxes (Herd.YarnGrabBox): the strips are layered windows, so the ball's transparent pixels let
    // clicks through; these alpha-1 borders make the whole box around the ball grabbable.
    public readonly Border YarnHit = new() { Width = Tune.YarnGrab, Height = Tune.YarnGrab, Background = HitBrush, Cursor = Cursors.Hand };
    public readonly Border ParkedHit = new() { Width = Tune.YarnGrab, Height = Tune.YarnGrab, Background = HitBrush, Cursor = Cursors.Hand };
    public readonly Image Body = OverlayWindow.Pixelated(), Mouse = OverlayWindow.Pixelated(), Zs = OverlayWindow.Pixelated();
    public readonly Border Hit, Bubble;
    public readonly TextBlock Text = new() { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 14 };
    // v4: the toy (yarn or bird), a prop (cheese, flyer, butterfly), the fish present, kittens, a murmur.
    public readonly Image Toy = OverlayWindow.Pixelated(), Prop = OverlayWindow.Pixelated(), Present = OverlayWindow.Pixelated();
    public readonly Image[] Kits = Enumerable.Range(0, Tune.MaxKittens).Select(_ => OverlayWindow.Pixelated(Cat.Size / 2)).ToArray();
    public readonly Border Murmur;
    public readonly Border Badge = new() { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.Black,
        BorderThickness = new Thickness(1), IsHitTestVisible = false, Visibility = Visibility.Collapsed };   // v5: which coding tool
    public readonly TextBlock MurmurText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    readonly Dictionary<(Anim, int, int), BitmapSource> _cache = new();

    public CatView()
    {
        // A padded hit box (left, right, top) so a moving cat is easy to catch; the sprite sits in the padding.
        Hit = new Border
        {
            Width = Cat.Size + 2 * Tune.CatGrabPad, Height = Cat.Size + Tune.CatGrabPad,
            Padding = new Thickness(Tune.CatGrabPad, Tune.CatGrabPad, Tune.CatGrabPad, 0),
            Background = HitBrush, Cursor = Cursors.Hand, Child = Body,
        };
        Bubble = new Border
        {
            Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(2),
            Padding = new Thickness(4, 0, 4, 0), Child = Text, IsHitTestVisible = false,
        };
        Murmur = new Border
        {
            Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 0, 3, 0), Child = MurmurText, IsHitTestVisible = false,
        };
        Mouse.IsHitTestVisible = false;
        Zs.IsHitTestVisible = false;
        Prop.IsHitTestVisible = false;
        Parked.IsHitTestVisible = false;   // ParkedHit takes the clicks
        Toy.Cursor = Cursors.Hand;   // the bird can be scared off (OverlayWindow); the ball is grabbed through YarnHit
        foreach (var k in Kits) k.IsHitTestVisible = false;
        Present.Cursor = Cursors.Hand;
    }

    /// A new coat or collar: drop the cached sprites so the next frame draws it.
    public void Dress(Palette p)
    {
        if (p == Palette) return;
        Palette = p;
        _cache.Clear();
    }

    /// belly = Tune.BellyStage (only for Tune.BellyAnims).
    public BitmapSource Sprite(Anim a, int frame, int belly = 0)
    {
        if (!_cache.TryGetValue((a, frame, belly), out var bmp))
        {
            var rows = Sprites.Frames[a][frame];
            if (belly > 0) rows = Sprites.Chubby(rows, belly);
            if (Palette.Collar != 0) rows = Sprites.Collar(rows, a);
            _cache[(a, frame, belly)] = bmp = Sprites.Render(rows, Palette);
        }
        return bmp;
    }
}

/// Owns the cats, one strip per monitor, the 2 s registry poll and the 30 fps frame loop,
/// plus everything the pointer does: click, drag across monitors, sardine.
public sealed class Herd
{
    internal const double SardineTimeout = 15;
    static readonly BitmapSource SardineSprite = Sprites.Render(Sprites.Sardine, Sprites.Palettes[0]);
    static readonly BitmapSource[] WandMouseSprites = Sprites.WandMouse.Select(f => Sprites.Render(f, Sprites.Palettes[0])).ToArray();
    static readonly BitmapSource[][] YarnSprites = Sprites.Yarn.Select(s => s.Select(f => Sprites.Render(f, Sprites.Palettes[0])).ToArray()).ToArray();

    readonly Dictionary<string, OverlayWindow> _strips = new();
    readonly Dictionary<string, CatView> _views = new();
    readonly Roster _roster = new();
    readonly Transcripts _transcripts = new(Transcripts.DefaultDir);
    readonly CatPrefs _prefs = new(CatPrefs.DefaultPath);   // per-project looks, %APPDATA%\TaskbarCats\cats.json
    readonly Random _rng = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DragWindow _drag = new();                 // the carried sardine
    readonly DragWindow _toy = new();                  // the pink mouse that follows the cursor in play mode
    readonly CardWindow _card = new();                 // the hover card (HoverCard.cs)
    readonly Chimes _chimes = new();
    AppSettings _settings = AppSettings.Load(AppSettings.DefaultPath);   // tray switches: hover cards, sounds
    bool _play;                                        // play mode (the teaser wand): one switch for the whole app
    double _playSince;
    string? _fullscreen;                               // device name of the monitor a fullscreen window covers
    Carry? _carry;                                     // the carried cat or ball: follow lag and throw speed
    List<Screen> _screens = [];
    double _last;

    // Pointer state. A press on a cat becomes a carry after Tune.DragPx px or Tune.HoldToLift s. "Give sardine" (cat menu) carries a sardine on the cursor.
    CatView? _pressed;
    Native.POINT _pressAt;
    bool _dragging, _sardine;
    double _sardineSince;
    OverlayWindow? _captor;
    CatView? _hovered;
    OverlayWindow? _hoverStrip;   // the strip _hovered was on when the hover began (a roaming cat changes strip)
    CatView? _yarnOf;   // pressed on (then carrying) this cat's yarn ball
    double _hoverSince;
    double _pressSince;   // clock time of the press on a cat or ball (hold to lift)

    OverlayWindow PrimaryStrip => _strips[Screens.Primary(_screens).Key];

    public void Start()
    {
        SyncScreens();
        Poll();
        new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Frame(), Dispatcher.CurrentDispatcher).Start();
        new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher.CurrentDispatcher).Start();
    }

    /// Wake a binned cat when something starts happening (a new non-idle status) or the user typed after binning it.
    internal static bool ShouldUnbin(SessionStatus now, SessionStatus previous, DateTimeOffset? lastHuman, DateTimeOffset binnedAt) =>
        (now != SessionStatus.Idle && now != previous) || lastHuman > binnedAt;

    /// Done = Windows' Asterisk, waiting = Exclamation: the user's own sound scheme, no assets.
    static void Play(Chime c)
    {
        try { (c == Chime.Done ? SystemSounds.Asterisk : SystemSounds.Exclamation).Play(); }
        catch (Exception) { }   // ponytail: a sound is never worth a crash
    }

    /// The typed praise is a Claude Code prompt (/haiku skill): other tools only get their window focused.
    internal static bool MayPraise(SessionInfo s) => s.Tool == AgentTool.Claude && s.Host != "terminal" && WindowFocuser.MayType(s.Status);

    /// Tooltip: the project's name (if given), folder, personality; other tools lead with their name.
    internal static string TipFor(SessionInfo s, Trait t, string? name = null)
    {
        var tip = CatLooks.Tip(name, s.Cwd, t);
        return s.Tool == AgentTool.Claude ? tip : $"{Tune.ToolLabel(s.Tool)} · {tip}";
    }

    /// Where a cat that came down at strip x (DIP, sprite centre) lands: centred there, kept between the walls.
    internal static double LandX(double dipX, World w) =>
        Math.Clamp(dipX - Cat.Size / 2, w.MinX, Math.Max(w.MinX, w.Width - Cat.Size));

    /// Which way a cat on strip `from` runs to reach strip `to`: +1 right, -1 left (side by side; stacked: by centre, ties right).
    internal static int CrossDir(Screen from, Screen to) =>
        to.Bounds.Left + to.Bounds.Right >= from.Bounds.Left + from.Bounds.Right ? 1 : -1;

    /// The monitor next to `from` on side dir (+1 right, -1 left): the nearest by centre x, or null at the end of the row.
    /// ponytail: stacked monitors (same centre x) are never "beside": a cat does not roam up or down. Add when someone asks.
    internal static Screen? Beside(IReadOnlyList<Screen> all, Screen from, int dir)
    {
        static double Mid(Screen s) => (s.Bounds.Left + s.Bounds.Right) / 2.0;
        return all.Where(s => s.Key != from.Key && dir * (Mid(s) - Mid(from)) > 0).MinBy(s => Math.Abs(Mid(s) - Mid(from)));
    }

    /// Which way a cat may wander off its strip (+1/-1, 0 = nowhere) and its chance per idle pick: towards the neighbour
    /// monitor with the fewest cats of its project, likelier when that spreads them out, rarer when it piles them up.
    internal static (int Dir, double Chance) RoamOffer(string id, IReadOnlyList<Screen> screens,
        IReadOnlyList<(string Id, string Strip, string Cwd)> cats, string? fullscreen)
    {
        var me = cats.First(c => c.Id == id);
        if (screens.FirstOrDefault(s => s.Key == me.Strip) is not { } here) return (0, 0);
        string project = CatPrefs.Key(me.Cwd);
        int Mates(string key) => cats.Count(c => c.Strip == key && c.Id != id && project != "" && CatPrefs.Key(c.Cwd) == project);
        (int Dir, double Chance) best = (0, 0);
        foreach (int dir in (int[])[1, -1])
        {
            if (Beside(screens, here, dir) is not { } s || s.Key == fullscreen) continue;
            int lean = Mates(here.Key) - Mates(s.Key);
            double chance = Tune.RoamChance * (lean > 0 ? Tune.RoamSpread : lean < 0 ? Tune.RoamPile : 1);
            if (chance > best.Chance) best = (dir, chance);
        }
        return best;
    }

    /// A ball parked on another strip that nobody has crossed for in Tune.YarnOrphan seconds is given up on.
    internal static bool YarnOrphaned(double since, double now) => now - since >= Tune.YarnOrphan;

    /// A press on a cat or its ball becomes a carry once the pointer has moved Tune.DragPx physical px, or has been
    /// held Tune.HoldToLift seconds. A quick click does neither, so it still focuses the conversation.
    internal static bool ShouldLift(double heldFor, int dx, int dy) =>
        Math.Abs(dx) >= Tune.DragPx || Math.Abs(dy) >= Tune.DragPx || heldFor >= Tune.HoldToLift - 1e-9;

    /// The yarn ball's grab box on a strip (DIP): a Tune.YarnGrab square centred on the ball's 48 DIP box (left edge
    /// ballX), its bottom on the strip floor so it never covers the taskbar.
    internal static Rect YarnGrabBox(double ballX) =>
        new(ballX + Cat.Size / 2 - Tune.YarnGrab / 2, OverlayWindow.BandHeight - Tune.YarnGrab, Tune.YarnGrab, Tune.YarnGrab);

    /// A press where a cat's and a ball's grab boxes overlap: the ball wins only when the press is nearer its centre
    /// (catX, ballX = left edges of their 48 DIP boxes). A tie goes to the cat.
    internal static bool BallWins(double pressX, double catX, double ballX) =>
        Math.Abs(pressX - (ballX + Cat.Size / 2)) < Math.Abs(pressX - (catX + Cat.Size / 2));

    /// The ball (on its own cat's strip, or parked here) whose grab box holds d, if it beats the cat at catX.
    (CatView Owner, bool Parked)? BallNear(OverlayWindow strip, Point d, double catX)
    {
        foreach (var o in _views.Values)
        {
            double? x = o.Strip == strip && o.Cat.Yarn is { } y ? y.X
                : o.YarnAcross is { } a && a.To == strip ? a.BallX : null;
            if (x is double bx && YarnGrabBox(Math.Round(bx)).Contains(d) && BallWins(d.X, catX, bx)) return (o, o.YarnAcross is not null);
        }
        return null;
    }

    /// A cat's friend: another cat on the same strip whose conversation runs in the same folder (the lowest id), or null.
    /// An unknown (empty) folder has no friends.
    internal static string? FriendOf(string id, IEnumerable<(string Id, string Strip, string Cwd)> cats)
    {
        var all = cats.ToList();
        var me = all.FirstOrDefault(c => c.Id == id);
        if (me.Id is null || string.IsNullOrEmpty(me.Cwd)) return null;
        string folder = me.Cwd.TrimEnd('\\', '/');
        return all.Where(c => c.Id != id && c.Strip == me.Strip && string.Equals(c.Cwd.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    /// The sessions whose project folder is not hidden (right-click: Hide this project's cat).
    internal static List<SessionInfo> Shown(IEnumerable<SessionInfo> all, CatPrefs prefs) =>
        all.Where(s => !prefs.Get(s.Cwd).Hidden).ToList();

    void SyncScreens()
    {
        var screens = Screens.All();
        if (screens.Count == 0) return;   // mid display change: keep the current strips until the next poll
        _screens = screens;
        foreach (var s in screens)
        {
            if (!_strips.TryGetValue(s.Key, out var strip))
            {
                var created = strip = new OverlayWindow(s);
                created.MouseMove += (_, _) => PointerMoved();
                created.MouseLeftButtonUp += (_, _) => PointerUp();
                created.LostMouseCapture += (_, _) => { if (_captor == created) PointerUp(); };   // Alt+Tab mid-drag
                _strips[s.Key] = created;
                created.Show();
            }
            strip.Place(s);
        }
        string primary = Screens.Primary(screens).Key;
        foreach (var (key, strip) in _strips.ToList())
        {
            if (screens.Any(s => s.Key == key)) continue;
            if (_captor == strip) EndPointer();
            foreach (var v in _views.Values.Where(v => v.Strip == strip).ToList()) Move(v, _strips[primary]);   // unplugged
            _strips.Remove(key);
            strip.Close();
        }
    }

    void Poll()
    {
        SyncScreens();
        if (_strips.Count == 0) return;   // no monitor reported yet
        var fullscreen = _fullscreen = Native.FullscreenMonitor();
        foreach (var (key, strip) in _strips) strip.Visibility = key == fullscreen ? Visibility.Hidden : Visibility.Visible;

        var sessions = Shown(SessionReader.ReadActive(SessionReader.DefaultDir, DateTimeOffset.Now, SessionReader.IsAlive, _transcripts.LastHumanAt)
            .Concat(EventReader.ReadActive(Hook.DefaultDir, DateTimeOffset.Now, SessionReader.IsAlive)), _prefs);
        var (added, removed) = _roster.Update(sessions);
        foreach (var s in added) Add(s);
        var now = DateTimeOffset.Now;
        var cues = new Dictionary<CatView, IReadOnlyList<Cue>>();
        var finished = new List<CatView>();
        foreach (var s in sessions)
        {
            if (!_views.TryGetValue(s.SessionId, out var v)) continue;
            if (v.Cat.IsBinned && ShouldUnbin(s.Status, v.LastStatus, s.LastHumanAt, v.BinnedAt)) Unbin(v);
            if (v.LastStatus == SessionStatus.Working && s.Status == SessionStatus.Idle) finished.Add(v);
            // Status changes only: murmurs and cues (tag's "mrrp") never chime. A Quiet project never does.
            if (_settings.Sounds && !v.Look.Quiet && _chimes.Decide(s.SessionId, v.LastStatus, s.Status, _clock.Elapsed.TotalSeconds) is { } chime)
                Play(chime);
            v.Session = s; v.Cat.Status = s.Status; v.LastStatus = s.Status;
            if (s.Status == SessionStatus.Idle && WindowFocuser.IsFocused(s)) v.Cat.Acknowledge();   // already looking at it: no MEOW
            v.Signals = s.Tool == AgentTool.Claude ? _transcripts.Signals(s.SessionId) : s.Live;
            v.Subagents = _transcripts.RunningSubagents(s.SessionId, now);
            cues[v] = v.Mood.Update(v.Signals, s.Status, s.UpdatedAt, v.Subagents, now);
        }
        // All moods first (a toy depends on every busy cat's run), the cues last (a catch depends on the toy).
        var picked = _views.Values.Where(v => v.Look.Toy is not null).ToDictionary(v => v.Cat.Id, v => v.Look.Toy!.Value);
        var toys = Pursuits.Assign(_views.Values.Where(v => v.Cat.Status == SessionStatus.Working).Select(v => (v.Cat.Id, v.Mood.Run)), picked);
        var where = _views.Values.Select(v => (v.Cat.Id, v.Strip.Screen.Key, v.Session.Cwd)).ToList();
        foreach (var v in _views.Values)
        {
            var c = v.Cat;
            c.Activity = v.Mood.Activity; c.Speed = Tune.Speed(v.Mood.Trait); c.Kittens = v.Mood.Kittens;
            if (toys.TryGetValue(c.Id, out var toy)) c.Toy = toy;
            c.Friend = FriendOf(c.Id, where) is { } f ? _views[f].Cat : null;
            (c.RoamDir, c.RoamChance) = RoamOffer(c.Id, _screens, where, fullscreen);
            if (cues.TryGetValue(v, out var list)) foreach (var cue in list) c.React(cue);
        }
        foreach (var v in finished) v.Cat.Friend?.Greet(v.Cat);
        foreach (var id in removed)
            if (_views.TryGetValue(id, out var v)) v.Cat.Despawn();
    }

    void Add(SessionInfo s)
    {
        if (_views.Remove(s.SessionId, out var old)) old.Strip.Detach(old);   // re-appeared while hopping off
        var strip = PrimaryStrip;
        var w = strip.WorldFor([], null);
        var v = new CatView
        {
            Cat = new Cat(s.SessionId, Cat.SpawnX(w, _views.Values.Where(o => o.Strip == strip && o.Cat.IsLive).Select(o => o.Cat.X), _rng), _rng) { Status = s.Status },
            Session = s, Palette = Sprites.PaletteFor(s.SessionId), Strip = strip, LastStatus = s.Status, Mood = new Mood(s.SessionId, _rng),
        };
        Restyle(v);
        v.Present.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            if (v.Cat.TakePresent()) await WindowFocuser.FocusAsync(v.Session);   // the fish: a long job is done, go and see
        };
        v.Toy.MouseLeftButtonDown += (_, e) =>
        {
            if (v.Cat.Bird is null) return;
            e.Handled = true; v.Cat.ScareBird();   // not a cat click: no VS Code focus
        };
        v.YarnHit.MouseLeftButtonDown += (_, e) =>
        {
            if (v.Cat.Yarn is null) return;
            e.Handled = true;
            YarnDown(v);
        };
        v.ParkedHit.MouseLeftButtonDown += (_, e) =>
        {
            if (v.YarnAcross is not { } a) return;
            e.Handled = true;
            YarnDown(v, a.To);   // the ball lies on another strip: that strip takes the mouse
        };
        v.Hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Native.GetCursorPos(out var p);
            // Feeding wins; otherwise a ball under the press wins when the press is nearer the ball (BallWins).
            if (!_sardine && BallNear(v.Strip, v.Strip.ToDip(p), v.Cat.X) is { } b) { YarnDown(b.Owner, b.Parked ? v.Strip : null); return; }
            CatDown(v);
        };
        var menu = new ContextMenu();
        void Fill() => CatMenu.Fill(menu.Items, v.Session.Cwd, v.Look, v.Cat.Pinned, Tune.TraitOf(v.Session.SessionId),
            () => v.Cat.Pinned = !v.Cat.Pinned, () => SendAway(v), Hooks());
        Fill();
        v.Hit.ContextMenuOpening += (_, _) => Fill();   // rebuilt on every open: the checks show the saved look
        v.Hit.ContextMenu = menu;
        strip.Attach(v);
        _views[s.SessionId] = v;
    }

    /// The project's saved look on this cat: coat and collar, personality, quiet, tooltip (size is read in Draw, the
    /// toy in Poll). With nothing chosen, the coat is the session's own palette (Sprites.PaletteFor). This is the one
    /// place a cat's default look is decided, including the per-tool badge.
    void Restyle(CatView v)
    {
        var l = v.Look = _prefs.Get(v.Session.Cwd);
        v.Dress(Sprites.Dress(l, Sprites.PaletteFor(v.Session.SessionId)));
        v.Mood.Trait = l.Trait ?? Tune.TraitOf(v.Session.SessionId);
        v.Cat.Quiet = l.Quiet;
        v.Hit.ToolTip = _settings.HoverCards ? null : TipFor(v.Session, v.Mood.Trait, l.Name);   // the card replaces the tooltip
        if (Tune.ToolColor(v.Session.Tool) is uint c)
        {
            v.Badge.Background = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));
            v.Badge.Visibility = Visibility.Visible;
        }
    }

    /// A right-click choice for one project folder: saved, then every cat in that folder shows it at once. Hidden:
    /// they hop off now and stay off (Shown). Unhidden: they hop back on at the next poll (Roster.Drop forgot them).
    void Change(string cwd, Func<CatLook, CatLook> f)
    {
        var look = _prefs.Set(cwd, f(_prefs.Get(cwd)));
        string key = CatPrefs.Key(cwd);
        foreach (var o in _views.Values.Where(o => CatPrefs.Key(o.Session.Cwd) == key).ToList())
        {
            if (!look.Hidden) { Restyle(o); continue; }
            _roster.Drop(o.Cat.Id);
            o.Cat.Despawn();
        }
    }

    internal CatMenu.Hooks Hooks() => new(Change, () => _prefs.Hidden, () => Application.Current.Shutdown(),
        GiveSardine, () => _play, () => SetPlay(!_play), () => _settings, SetSettings);

    /// A tray switch: saved, the tooltip/card swap applied to every cat now. Called from the WinForms tray menu, which
    /// runs on the WPF UI thread (like TogglePlay).
    void SetSettings(AppSettings s)
    {
        _settings = s;
        s.Save(AppSettings.DefaultPath);
        foreach (var v in _views.Values) Restyle(v);
        if (!s.HoverCards) _card.Off();
    }

    void Move(CatView v, OverlayWindow to)
    {
        if (v.Strip == to) return;
        v.Strip.Detach(v);
        to.Attach(v);
        v.Strip = to;
        foreach (var o in _views.Values) { o.Cat.Forget(v.Cat); v.Cat.Forget(o.Cat); }   // nobody greets or chases across monitors
    }

    void Unbin(CatView v)
    {
        var strip = PrimaryStrip;
        Move(v, strip);
        var w = strip.WorldFor([], null);
        v.Cat.Unbin(Cat.SpawnX(w, _views.Values.Where(o => o.Strip == strip && o != v && o.Cat.IsLive).Select(o => o.Cat.X), _rng));   // hops out away from the others, not at the tray
    }

    void Frame()
    {
        double t = _clock.Elapsed.TotalSeconds, dt = Math.Min(t - _last, 0.1);
        _last = t;
        Native.GetCursorPos(out var cursor);
        if (_play && Teaser.Expired(_playSince, t)) SetPlay(false);
        var under = Screens.At(_screens, cursor.X, cursor.Y);
        foreach (var v in _views.Values.Where(v => v.Flight is not null).ToList()) Fly(v, dt);
        foreach (var v in _views.Values.Where(v => v.YarnFlight is not null).ToList()) FlyYarn(v, dt);
        foreach (var strip in _strips.Values)
        {
            var mine = _views.Values.Where(v => v.Strip == strip).ToList();
            var world = strip.WorldFor(mine.Select(v => v.Cat).ToList(), strip.PounceX(cursor));
            Lure? lure = null;
            if (_play) { var d = strip.ToDip(cursor); lure = Teaser.For(under?.Key == strip.Screen.Key, d.X, d.Y, OverlayWindow.BandHeight); }
            foreach (var v in mine)
            {
                v.Cat.Lure = lure;
                v.Cat.Tick(dt, world);
                if (v.Cat.IsGone)
                {
                    strip.Detach(v); _views.Remove(v.Cat.Id);
                    v.Air?.Close();
                    v.YarnAir?.Close();
                    Unpark(v);
                    if (_pressed == v || _yarnOf == v) EndPointer();
                    continue;
                }
                strip.Draw(v);
            }
        }
        foreach (var v in _views.Values.Where(v => v.YarnAcross is not null).ToList()) CrossForYarn(v);
        foreach (var v in _views.Values.Where(v => v.Cat.RoamingTo != 0).ToList()) Roam(v);
        Hover(t, cursor);
        TickCarry(t, dt, cursor);
        TickToy(cursor, under);
    }

    /// The teaser wand (cat or tray menu): play mode on or off, for every monitor at once. On, the cats chase the cursor
    /// (Cat.Lure, set each frame) until it is switched off, or Teaser.Expired.
    void SetPlay(bool on)
    {
        _play = on;
        _playSince = _clock.Elapsed.TotalSeconds;
        if (!on) _toy.Hide();
    }

    /// The pink mouse follows the cursor, in its own click-through window, so the user sees what the cats chase.
    void TickToy(Native.POINT cursor, Screen? under)
    {
        if (!_play || under is null || under.Key == _fullscreen) { _toy.Hide(); return; }
        _toy.ShowAt(cursor, WandMouseSprites[(int)(Environment.TickCount64 / Tune.WandSwingMs % 2)], under.Scale);
    }

    /// The cat chasing a ball that fell on another monitor: when it reaches the edge it goes over (the same Move as a
    /// thrown cat landing there) and carries on at the near edge of that strip, the ball ahead. Gone or cancelled: drop it.
    void CrossForYarn(CatView v)
    {
        var (to, ballX) = v.YarnAcross!.Value;
        bool gone = v.Cat.FetchDir == 0 || !_strips.ContainsValue(to) || YarnOrphaned(v.YarnAcrossSince, _clock.Elapsed.TotalSeconds);
        int dir = gone ? 0 : v.Cat.CrossingTo;
        if (!gone && dir == 0) return;   // still running to the edge
        Unpark(v);
        if (gone) { v.Cat.CancelFetch(); return; }
        Move(v, to);
        var w = to.WorldFor([], null);
        v.Cat.Cross(dir > 0 ? w.MinX : Math.Max(w.MinX, w.Width - Cat.Size), ballX);
    }

    /// A wandering cat stands at the end of its strip: over to the next monitor's strip (the same Move as the yarn chase),
    /// in at its near end. The way is shut (that monitor went, it is fullscreen, the cat is pressed or its menu is open):
    /// it turns back. A strip that vanished mid-walk is handled by SyncScreens (its cats go to the primary strip).
    void Roam(CatView v)
    {
        int dir = v.Cat.RoamingTo;
        if (Beside(_screens, v.Strip.Screen, dir) is not { } s || s.Key == _fullscreen || !_strips.TryGetValue(s.Key, out var to)
            || _pressed == v || v.Hit.ContextMenu?.IsOpen == true)
        {
            v.Cat.CancelRoam();
            return;
        }
        Move(v, to);
        var w = to.WorldFor([], null);
        v.Cat.Arrive(dir > 0 ? w.MinX : Math.Max(w.MinX, w.Width - Cat.Size));
    }

    /// Resting the pointer on a cat pets it (Cat.Pet has its own cool-down) and, after Tune.CardDelay, shows its hover
    /// card, refreshed every frame (the text only redraws when it changes). Not while pressing, carrying or feeding.
    /// Runs after Roam/CrossForYarn in Frame, so v.Strip is the strip the cat is on now; a cat that changed strip
    /// starts over (the card closes, then reopens on the new monitor if the pointer is still on it).
    void Hover(double t, Native.POINT cursor)
    {
        var v = _pressed is null && _yarnOf is null && !_sardine ? CatAt(cursor) : null;
        if (v != _hovered || v?.Strip != _hoverStrip) { _hovered = v; _hoverStrip = v?.Strip; _hoverSince = t; }
        else if (v is not null && t - _hoverSince >= Tune.PetHover) v.Cat.Pet();
        if (v is null || !HoverCard.Shows(_settings.HoverCards, t - _hoverSince, _play, _views.Values.Any(o => o.Hit.ContextMenu is { IsOpen: true })))
        {
            _card.Off();
            return;
        }
        double k = Tune.LookScale(v.Look.Size ?? v.Mood.Look);   // a bigger cat's head is higher (OverlayWindow.Draw)
        // CatAt only returns cats on visible strips, so the strip has a PresentationSource: PointToScreen is safe (ToDip's inverse).
        var at = v.Strip.PointToScreen(new Point(v.Cat.X + Cat.Size / 2, OverlayWindow.BandHeight - Cat.Size * k - v.Cat.Lift - Tune.CardLift));
        _card.ShowFor(HoverCard.For(v.Session, v.Signals, v.Look.Name, v.Mood.Run, v.Subagents, DateTimeOffset.Now),
            at.X, at.Y, v.Strip.Screen.Scale, v.Strip.Screen.Bounds);
    }

    /// One frame of a cat in the air: drawn in its own window until it lands, then handed to the strip below it.
    void Fly(CatView v, double dt)
    {
        var f = v.Flight!;
        f.Advance(dt, _screens);
        var centre = new Native.POINT { X = (int)Math.Round(f.X), Y = (int)Math.Round(f.Y) };
        if (!f.Landed)
        {
            v.Air?.ShowAt(centre, v.Sprite(Anim.Fly, f.FlyFrame), f.Screen.Scale, flip: !f.FacingRight);
            return;
        }
        v.Flight = null;
        var strip = _strips.TryGetValue(f.Screen.Key, out var under) ? under : PrimaryStrip;
        bool sameStrip = strip == v.Strip;       // the frozen mouse lives on the strip the cat was picked up from
        Move(v, strip);
        v.Cat.Land(LandX(strip.ToDip(centre).X, strip.WorldFor([], null)), f.FacingRight, keepMouse: sameStrip, hard: f.LandVy / f.Screen.Scale >= Tune.HardLanding);
        v.Air?.Hide();
    }

    void TickCarry(double t, double dt, Native.POINT cursor)
    {
        // A press held still lifts too (PointerMoved only fires on movement).
        if (!_dragging && !_sardine && (_pressed ?? _yarnOf) is not null
            && ShouldLift(t - _pressSince, cursor.X - _pressAt.X, cursor.Y - _pressAt.Y))
            StartCarry(t, cursor);
        if (_dragging && _carry is { } k && (_pressed ?? _yarnOf) is { } v)
        {
            k.Sample(t, cursor.X, cursor.Y);
            k.Follow(dt);
            var at = new Native.POINT { X = (int)Math.Round(k.X), Y = (int)Math.Round(k.Y) };
            double s = Screens.At(_screens, at.X, at.Y)?.Scale ?? 1;   // the monitor under the cat, not the cursor
            if (_pressed is not null) (v.Air ??= new DragWindow()).ShowAt(at, v.Sprite(Anim.Dangle, Flight.SwingFrame(k.Vx / s, t)), s, Flight.ScruffY);
            else (v.YarnAir ??= new DragWindow()).ShowAt(at, YarnSprite(v), s);
            return;
        }
        if (!_sardine) return;
        double scale = Screens.At(_screens, cursor.X, cursor.Y)?.Scale ?? 1;
        _drag.ShowAt(cursor, SardineSprite, scale);
        // A click anywhere but a cat, Esc, a right-click or waiting puts the sardine back.
        if (SardineDone(Native.LeftButtonDown() && CatAt(cursor) is null, Native.EscOrRightDown(), t - _sardineSince)) EndPointer();
    }

    /// The press becomes a carry: the cat is picked up (or its ball taken) and the Carry starts at the press point.
    void StartCarry(double t, Native.POINT p)
    {
        if ((_pressed ?? _yarnOf) is not { } v) return;
        if (_yarnOf is not null ? !v.Cat.TakeYarn() : !v.Cat.Grab()) { EndPointer(); return; }
        if (_yarnOf is not null) Unpark(v);   // picked up off another strip: no longer parked there
        _carry = new Carry(t, _pressAt.X, _pressAt.Y);
        _carry.Sample(t, p.X, p.Y);
        _dragging = true;
    }

    void CatDown(CatView v)
    {
        if (_sardine) { Feed(v); return; }
        EndPointer();
        _pressed = v;
        _pressSince = _clock.Elapsed.TotalSeconds;
        Native.GetCursorPos(out _pressAt);
        Capture(v.Strip);
    }

    /// Cat menu: the sardine goes on the cursor until a click on any cat feeds it (Feed), or a click away, Esc, a right-click
    /// or SardineTimeout puts it back.
    void GiveSardine()
    {
        EndPointer();
        _sardine = true;
        _sardineSince = _clock.Elapsed.TotalSeconds;
    }

    /// Cat menu: off the strip until the conversation shows new activity (ShouldUnbin).
    void SendAway(CatView v)
    {
        if (!v.Cat.Grab()) return;
        v.Cat.Bin();
        v.BinnedAt = DateTimeOffset.Now;
    }

    /// The carried sardine is done: clicked away from every cat, Esc or the right button pressed, or held too long.
    internal static bool SardineDone(bool clickedAway, bool cancelKey, double heldFor) =>
        clickedAway || cancelKey || heldFor > SardineTimeout;

    void YarnDown(CatView v, OverlayWindow? at = null)
    {
        if (_sardine) return;
        EndPointer();
        _yarnOf = v;
        _pressSince = _clock.Elapsed.TotalSeconds;
        Native.GetCursorPos(out _pressAt);
        Capture(at ?? v.Strip);
    }

    /// The thrown ball lies on strip `to` at ballX until its cat gets there: drawn there as a clickable ball, so it
    /// can be picked up again, not in the click-through flight window.
    void Park(CatView v, OverlayWindow to, double ballX)
    {
        v.YarnAir?.Hide();
        v.YarnAcross = (to, ballX);
        v.YarnAcrossSince = _clock.Elapsed.TotalSeconds;
        to.AddLoose(v.Parked);
        to.AddLoose(v.ParkedHit);
        v.Parked.Source = YarnSprite(v);
        Canvas.SetLeft(v.Parked, Math.Round(ballX)); Canvas.SetTop(v.Parked, OverlayWindow.BandHeight - Cat.Size);
        var box = YarnGrabBox(Math.Round(ballX));
        Canvas.SetLeft(v.ParkedHit, box.Left); Canvas.SetTop(v.ParkedHit, box.Top);
        v.Parked.Visibility = v.ParkedHit.Visibility = Visibility.Visible;
    }

    void Unpark(CatView v)
    {
        if (v.YarnAcross is { } a) { a.To.RemoveLoose(v.Parked); a.To.RemoveLoose(v.ParkedHit); }
        v.YarnAcross = null;
        v.YarnAir?.Hide();
    }

    static BitmapSource YarnSprite(CatView v, int frame = 0) => YarnSprites[Math.Clamp(v.Mood.Yarn, 0, YarnSprites.Length - 1)][frame];

    /// Let go of the yarn ball: it flies from where it is drawn with the pointer's recent speed, like a thrown cat,
    /// and bounces on the taskbar.
    void ThrowYarn(CatView v, Carry k, Native.POINT p)
    {
        k.Sample(_clock.Elapsed.TotalSeconds, p.X, p.Y);
        var (vx, vy) = k.Throw();
        var screen = Screens.At(_screens, (int)Math.Round(k.X), (int)Math.Round(k.Y)) ?? Screens.Primary(_screens);
        v.YarnFlight = new Flight(k.X, k.Y, vx, vy, screen, facingRight: true, floorBounce: Tune.YarnFloorBounce);
    }

    /// One frame of the thrown ball. Down on its cat's own strip: it rolls on from there. Down on another monitor's
    /// strip: it lies where it fell and the cat runs over to it (CrossForYarn). A cat that will not go ("Stay here")
    /// gets it back at the nearest end of its own strip (Cat.DropYarn clamps).
    void FlyYarn(CatView v, double dt)
    {
        var f = v.YarnFlight!;
        f.Advance(dt, _screens);
        var centre = new Native.POINT { X = (int)Math.Round(f.X), Y = (int)Math.Round(f.Y) };
        if (!f.Landed) { v.YarnAir?.ShowAt(centre, YarnSprite(v, (int)(f.Aloft * Tune.YarnFlipHz) % 2), f.Screen.Scale); return; }
        v.YarnFlight = null;
        if (_strips.TryGetValue(f.Screen.Key, out var to) && to != v.Strip && !double.IsNaN(to.ToDip(centre).X))
        {
            var tw = to.WorldFor([], null);
            double ballX = LandX(to.ToDip(centre).X, tw);   // the strip's ends clamp it, so it never lands outside them
            int dir = CrossDir(v.Strip.Screen, to.Screen);
            double beyond = dir > 0 ? ballX - tw.MinX : Math.Max(tw.MinX, tw.Width - Cat.Size) - ballX;   // the run on that strip
            if (v.Cat.DropYarnAcross(dir, beyond)) { Park(v, to, ballX); return; }
        }
        v.YarnAir?.Hide();
        double x = v.Strip.ToDip(centre).X;
        v.Cat.DropYarn(double.IsNaN(x) ? v.Cat.X : x - Cat.Size / 2, f.LandVx / f.Screen.Scale);
    }

    void PointerMoved()
    {
        Native.GetCursorPos(out var p);
        double t = _clock.Elapsed.TotalSeconds;
        if (_dragging) { _carry?.Sample(t, p.X, p.Y); return; }   // every move counts towards the throw speed
        if ((_pressed ?? _yarnOf) is null || !ShouldLift(t - _pressSince, p.X - _pressAt.X, p.Y - _pressAt.Y)) return;
        StartCarry(t, p);
    }

    async void PointerUp()
    {
        Native.GetCursorPos(out var p);
        if (_yarnOf is { } ball)
        {
            var thrown = _dragging ? _carry : null;
            _dragging = false;
            EndPointer();
            if (thrown is not null) ThrowYarn(ball, thrown, p);   // a plain click on the ball does nothing
            return;
        }
        if (_pressed is not { } v) return;
        var carried = _dragging ? _carry : null;
        _dragging = false;
        EndPointer();
        if (carried is not null) { DropCat(v, carried, p); return; }
        if (v.Cat.Click() == ClickResult.Focus) await WindowFocuser.FocusAsync(v.Session);
    }

    /// Let go: thrown from where the cat hangs with the pointer's recent speed
    /// (a still pointer = a straight fall).
    void DropCat(CatView v, Carry k, Native.POINT p)
    {
        k.Sample(_clock.Elapsed.TotalSeconds, p.X, p.Y);
        var (vx, vy) = k.Throw();
        var screen = Screens.At(_screens, (int)Math.Round(k.X), (int)Math.Round(k.Y)) ?? Screens.Primary(_screens);
        int size = (int)Math.Round(Cat.Size * screen.Scale);
        // Same centre as the carried sprite (DragWindow.ShowAt, scruff anchor at the Carry point), so nothing jumps.
        v.Flight = new Flight(k.X, Math.Round(k.Y) - (int)Math.Round(size * Flight.ScruffY) + size / 2, vx, vy, screen, v.Cat.FacingRight);
    }

    /// The cat eats and says "good job !", then its own conversation gets the fixed praise prompt
    /// (WindowFocuser.Praise), typed only once VS Code shows that conversation. Waiting on a permission: only opened.
    async void Feed(CatView v)
    {
        EndPointer();
        if (!v.Cat.IsLive) return;
        v.Cat.Feed();
        if (MayPraise(v.Session))
            await WindowFocuser.SendPraiseAsync(v.Session, _transcripts.Title(v.Session.SessionId));
        else
            await WindowFocuser.FocusAsync(v.Session);
    }

    CatView? CatAt(Native.POINT p)
    {
        foreach (var v in _views.Values)
        {
            if (!v.Cat.IsLive || !v.Strip.IsVisible) continue;
            var d = v.Strip.ToDip(p);
            double top = OverlayWindow.BandHeight - Cat.Size - v.Cat.Lift, pad = Tune.CatGrabPad;   // the same box as Hit
            if (d.X >= v.Cat.X - pad && d.X <= v.Cat.X + Cat.Size + pad && d.Y >= top - pad && d.Y <= top + Cat.Size) return v;
        }
        return null;
    }

    void Capture(OverlayWindow strip) { _captor = strip; strip.CaptureMouse(); }

    void Release()
    {
        var c = _captor;
        _captor = null;   // first, so the LostMouseCapture this raises is ignored
        c?.ReleaseMouseCapture();
    }

    void EndPointer()
    {
        if (_dragging && _pressed is { Cat.IsHeld: true } held && _carry is { } k1)
        {
            _dragging = false;
            Native.GetCursorPos(out var p);
            DropCat(held, k1, p);   // never leave a cat stuck in the air
        }
        if (_dragging && _yarnOf is { } carried && _carry is { } k2)
        {
            _dragging = false;
            Native.GetCursorPos(out var p);
            ThrowYarn(carried, k2, p);   // never leave the ball in the air
        }
        _pressed = null; _yarnOf = null; _dragging = false; _carry = null; _sardine = false;
        Release();
        _drag.Hide();
    }
}

/// A carried or flying cat, or the carried sardine, drawn at a screen point on any monitor. Clicks pass straight through it.
sealed class DragWindow : Window
{
    static readonly Transform FlipX = new ScaleTransform(-1, 1);
    readonly Image _img = OverlayWindow.Pixelated();
    IntPtr _hwnd;

    public DragWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
        Width = Cat.Size; Height = Cat.Size; Content = _img;
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; Native.MakeClickThrough(_hwnd); };
    }

    /// p = the screen pixel that sits at the sprite's horizontal centre and at anchorY of its height
    /// (0.5 = centre; Flight.ScruffY = the scruff, for a carried cat). flip = face left.
    public void ShowAt(Native.POINT p, BitmapSource sprite, double scale, double anchorY = 0.5, bool flip = false)
    {
        _img.Source = sprite;
        _img.RenderTransform = flip ? FlipX : Transform.Identity;
        if (!IsVisible) Show();
        int size = (int)Math.Round(Cat.Size * scale), left = p.X - size / 2, top = p.Y - (int)Math.Round(size * anchorY);
        Native.PlaceTopmost(_hwnd, new Native.RECT { Left = left, Top = top, Right = left + size, Bottom = top + size });
    }
}
