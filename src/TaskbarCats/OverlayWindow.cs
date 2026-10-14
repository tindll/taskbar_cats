using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarCats;

/// The transparent strip above one monitor's taskbar. Draws the cats that live on it. Herd owns the logic.
public sealed class OverlayWindow : Window
{
    public const double BandHeight = 120, TaskbarBand = 48;
    static readonly Transform FlipX = new ScaleTransform(-1, 1);
    static readonly BitmapSource[] MouseFrames = Sprites.Mouse.Select(f => Sprites.Render(f, Sprites.Palettes[0])).ToArray();
    static readonly BitmapSource[] ZzzFrames = Sprites.Zzz.Select(f => Sprites.Render(f, Sprites.Palettes[0])).ToArray();
    // v4 toys and props: fixed colours, so one render serves every cat.
    static readonly BitmapSource[][] YarnFrames = Sprites.Yarn.Select(Props).ToArray();
    static readonly BitmapSource[] BirdFrames = Props(Sprites.Bird), BirdFlyFrames = Props(Sprites.BirdFly), BatFlyFrames = Props(Sprites.BatFly),
        ButterflyFrames = Props(Sprites.Butterfly);
    static readonly BitmapSource CheeseSprite = Sprites.Render(Sprites.Cheese, Sprites.Palettes[0]), FishSprite = Sprites.Render(Sprites.Fish, Sprites.Palettes[0]);
    static readonly Dictionary<(double, bool), Transform> BodyTransforms = new();

    static BitmapSource[] Props(string[][] frames) => frames.Select(f => Sprites.Render(f, Sprites.Palettes[0])).ToArray();

    readonly Canvas _canvas = new() { ClipToBounds = true, UseLayoutRounding = true };
    IntPtr _hwnd;

    public OverlayWindow(Screen screen)
    {
        Screen = screen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
        Width = 1; Height = 1; Content = _canvas;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);
            Place(Screen);
        };
        Loaded += (_, _) => Place(Screen);
        // Moving between monitors makes WPF resize us to its own idea of the new size: put the strip back.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => Place(Screen));
    }

    public Screen Screen { get; private set; }

    /// The strip's width in DIP (the monitor's work-area width): cats and the yarn have all of it.
    double WidthDip => (Screen.Work.Right - Screen.Work.Left) / Screen.Scale;

    /// Physical-pixel placement: WPF's Left/Top are DIPs of whatever monitor the window is on right now.
    public void Place(Screen s)
    {
        Screen = s;
        if (_hwnd != IntPtr.Zero) Native.PlaceTopmost(_hwnd, Screens.Strip(s, BandHeight));
    }

    /// Cats walk the strip's full width.
    public World WorldFor(IReadOnlyList<Cat> cats, double? cursorX) => new(Math.Max(WidthDip, Cat.Size), cursorX, cats);

    /// Screen pixels to this strip's DIPs (DPI-correct, works for points outside the strip too).
    public Point ToDip(Native.POINT p) =>
        PresentationSource.FromVisual(this) is null ? new Point(double.NaN, double.NaN) : PointFromScreen(new Point(p.X, p.Y));

    /// Cursor x when it is over this strip or its taskbar (the pounce band), else null.
    public double? PounceX(Native.POINT p)
    {
        if (!IsVisible) return null;
        var d = ToDip(p);
        return d.X >= 0 && d.X <= ActualWidth && d.Y >= 0 && d.Y <= BandHeight + TaskbarBand ? d.X : null;
    }

    static UIElement[] Parts(CatView v) => [v.Mouse, v.Toy, v.YarnHit, v.Prop, v.Present, .. v.Kits, v.Hit, v.Badge, v.Bubble, v.Murmur, v.Zs];
    static UIElement[] Extras(CatView v) => [v.Toy, v.YarnHit, v.Prop, v.Present, .. v.Kits, v.Murmur];

    public void Attach(CatView v)
    {
        foreach (var e in Parts(v)) _canvas.Children.Add(e);
        Panel.SetZIndex(v.Hit, 1);
        Panel.SetZIndex(v.Bubble, 3);   // bubbles above every cat and prop, so nothing hides one
        Panel.SetZIndex(v.Murmur, 3);
        Panel.SetZIndex(v.Zs, 3);
        Panel.SetZIndex(v.Badge, 3);
    }

    /// A loose element (a yarn ball parked here by a cat from another monitor) on this strip's canvas.
    public void AddLoose(UIElement e) { if (!_canvas.Children.Contains(e)) _canvas.Children.Add(e); }
    public void RemoveLoose(UIElement e) => _canvas.Children.Remove(e);

    public void Detach(CatView v)
    {
        foreach (var e in Parts(v)) _canvas.Children.Remove(e);
    }

    public void Draw(CatView v)
    {
        var c = v.Cat;
        if (c.IsHeld || c.IsBinned)
        {
            v.Hit.Visibility = v.Bubble.Visibility = v.Zs.Visibility = v.Badge.Visibility = Visibility.Collapsed;
            foreach (var e in Extras(v)) e.Visibility = Visibility.Collapsed;
            // A held busy cat's mouse waits where it was (frozen), so the user can help it get away.
            v.Mouse.Visibility = c.IsHeld && c.HasMouse ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        v.Hit.Visibility = Visibility.Visible;
        v.Badge.Visibility = Tune.ToolColor(v.Session.Tool) is null ? Visibility.Collapsed : Visibility.Visible;
        bool halloween = Tune.IsHalloween(DateTime.Now);
        var anim = halloween && c.Anim == Anim.Box ? Anim.Pumpkin : c.Anim;
        v.Body.Source = v.Sprite(anim, c.Frame, Tune.BellyAnims.Contains(anim) ? v.Mood.Belly : 0);
        double k = Tune.LookScale(v.Look.Size ?? v.Mood.Look);   // the menu's Size beats the model's look
        v.Body.RenderTransform = BodyTransform(k, !c.FacingRight);
        // Whole DIPs: nearest-neighbour pixels shimmer when a sprite sits on a fractional position.
        double left = Math.Round(c.X), top = Math.Round(BandHeight - Cat.Size - c.Lift);
        double rise = Math.Round((k - 1) * Cat.Size);   // a bigger cat's head is higher (the hit box stays 48)
        DrawToys(v, c, halloween);
        Canvas.SetLeft(v.Hit, left - Tune.CatGrabPad); Canvas.SetTop(v.Hit, top - Tune.CatGrabPad);
        Canvas.SetLeft(v.Badge, left + Cat.Size - 9); Canvas.SetTop(v.Badge, top + 3);   // v5: tool collar dot by the ear

        v.Mouse.Visibility = c.HasMouse ? Visibility.Visible : Visibility.Collapsed;
        if (c.HasMouse)
        {
            v.Mouse.Source = MouseFrames[c.MouseFrame];
            v.Mouse.RenderTransform = c.MouseFacingRight ? Transform.Identity : FlipX;
            Canvas.SetLeft(v.Mouse, Math.Round(c.MouseX)); Canvas.SetTop(v.Mouse, BandHeight - Cat.Size);
        }

        // Asleep: drawn Z glyphs float up from the head instead of a speech bubble.
        bool zzz = c.Bubble == Bubble.Zzz;
        v.Zs.Visibility = zzz ? Visibility.Visible : Visibility.Collapsed;
        if (zzz)
        {
            v.Zs.Source = ZzzFrames[Sprites.ZzzFrame(Environment.TickCount64)];
            // The big Z (sprite cols 0-6, rows 9-15) sits just above the sleeping head: every Tune.SleepPoses head is cols 10-15
            // facing right, 0-5 facing left, with its ears on row 9. Kept inside the strip.
            double zx = left + Cat.Size / 2 + (c.FacingRight ? 2 : -26) * k;
            Canvas.SetLeft(v.Zs, Math.Round(Math.Clamp(zx, 0, Math.Max(0, ActualWidth - Cat.Size))));
            Canvas.SetTop(v.Zs, Math.Max(0, top - 24 - rise));
        }
        v.Text.Text = c.Bubble switch
        {
            Bubble.Meow => c.Quiet ? "meow" : "MEOW!", Bubble.Bang => "!",
            Bubble.GoodJob => "good job !", _ => "",
        };
        // Centred over the head (sprite cols 8-15 facing right, 0-7 facing left), kept inside the strip.
        double head = left + Cat.Size / 2 + (c.FacingRight ? 12 : -12) * k;
        bool talk = c.Bubble is not (Bubble.None or Bubble.Zzz);
        v.Bubble.Visibility = talk ? Visibility.Visible : Visibility.Collapsed;
        if (talk) Over(v.Bubble, head, top - 24 - rise);
        // A murmur only when the cat has nothing more important to say.
        bool murmur = !c.Quiet && c.Bubble == Bubble.None && c.Murmur != Murmur.None;   // a quiet cat keeps its murmurs to itself
        v.Murmur.Visibility = murmur ? Visibility.Visible : Visibility.Collapsed;
        if (!murmur) return;
        v.MurmurText.Text = Tune.MurmurText(c.Murmur);
        Over(v.Murmur, head, top - 20 - rise);
    }

    void Over(Border b, double head, double top)
    {
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = b.DesiredSize.Width;
        Canvas.SetLeft(b, Math.Round(Math.Clamp(head - w / 2, 0, Math.Max(0, ActualWidth - w))));
        Canvas.SetTop(b, Math.Max(0, top));
    }

    void DrawToys(CatView v, Cat c, bool halloween)
    {
        BitmapSource? toy = null;
        double tx = 0, tLift = 0;
        bool tFlip = false;
        if (c.Yarn is { } y) { toy = YarnFrames[Math.Clamp(v.Mood.Yarn, 0, YarnFrames.Length - 1)][y.Frame]; tx = y.X; }
        else if (c.Bird is { } b) { toy = (b.Flying ? BirdFlyFrames : BirdFrames)[b.Frame]; tx = b.X; tLift = b.Lift; tFlip = !b.FacingRight; }
        Put(v.Toy, toy, tx, tLift, tFlip);
        v.Toy.IsHitTestVisible = c.Yarn is null && c.Bird is not null;   // the bird is scared off by a click on it
        v.YarnHit.Visibility = c.Yarn is null ? Visibility.Collapsed : Visibility.Visible;   // the ball: its bigger grab box
        if (c.Yarn is { } ball)
        {
            var box = Herd.YarnGrabBox(Math.Round(ball.X));
            Canvas.SetLeft(v.YarnHit, box.Left); Canvas.SetTop(v.YarnHit, box.Top);
        }

        var prop = c.Prop switch
        {
            Prop.Cheese => CheeseSprite,
            Prop.Flyer => (halloween ? BatFlyFrames : BirdFlyFrames)[c.PropFrame % 2],
            Prop.Butterfly => ButterflyFrames[c.PropFrame % ButterflyFrames.Length],
            _ => null,
        };
        Put(v.Prop, prop, c.PropX, c.PropLift, !c.PropFacingRight);

        Put(v.Present, c.HasPresent ? FishSprite : null, c.PresentX, 0, false);

        for (int i = 0; i < v.Kits.Length; i++)
        {
            var kit = v.Kits[i];
            kit.Visibility = i < c.Kittens ? Visibility.Visible : Visibility.Collapsed;
            if (i >= c.Kittens) continue;
            var (n, ms) = Cat.Timing[Anim.Walk];
            kit.Source = c.KittensMoving ? v.Sprite(Anim.Walk, (int)(Environment.TickCount64 / ms + i) % n) : v.Sprite(Anim.Sit, 0);
            kit.RenderTransform = c.FacingRight ? Transform.Identity : FlipX;
            Canvas.SetLeft(kit, Math.Round(c.KittenX(i)));
            Canvas.SetTop(kit, Math.Round(BandHeight - Cat.Size / 2 - c.KittenLift(i)));
        }
    }

    static void Put(Image img, BitmapSource? src, double x, double lift, bool flip)
    {
        img.Visibility = src is null ? Visibility.Collapsed : Visibility.Visible;
        if (src is null) return;
        img.Source = src;
        img.RenderTransform = flip ? FlipX : Transform.Identity;
        Canvas.SetLeft(img, Math.Round(x));
        Canvas.SetTop(img, Math.Round(BandHeight - Cat.Size - lift));
    }

    /// Look scale k about the bottom centre (the paws stay on the floor), mirrored when facing left.
    static Transform BodyTransform(double k, bool flip)
    {
        if (k == 1) return flip ? FlipX : Transform.Identity;
        if (!BodyTransforms.TryGetValue((k, flip), out var t))
        {
            t = new ScaleTransform(flip ? -k : k, k, 0, Cat.Size / 2);
            t.Freeze();
            BodyTransforms[(k, flip)] = t;
        }
        return t;
    }

    public static Image Pixelated(double size = Cat.Size)
    {
        var i = new Image { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.NearestNeighbor);
        return i;
    }
}
