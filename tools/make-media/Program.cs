// Regenerates docs/media/*.png from the app's own sprites and card/menu code, offscreen (no window is ever shown,
// no settings, registry or tool config is read or written; all names below are made up).
// Run from the repo root: dotnet run --project tools/make-media
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using TaskbarCats;

var root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "TaskbarCats.slnx"))) root = Path.GetDirectoryName(root.TrimEnd('\\'));
var outDir = args.Length > 0 ? args[0] : Path.Combine(root ?? ".", "docs", "media");
Directory.CreateDirectory(outDir);
string Out(string n) => Path.Combine(outDir, n);

var t = new Thread(() =>
{
Media.Cats(Out("cats.png"));
Media.Poses(Out("poses.png"));
Media.HoverCards(Out("hovercard.png"));
Media.Menu(Out("menu.png"));
Media.Strip(Out("strip.png"));
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
Console.WriteLine("wrote " + outDir);

static class Media
{
    static Brush B(uint argb) => new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    static readonly Brush Panel = B(0xFF2B2B2B), Text = B(0xFFE4E4E4), Dim = B(0xFF9A9A9A);
    static readonly FontFamily Ui = new("Segoe UI"), Mono = new("Consolas");

    static TextBlock T(string s, double size, Brush fg, bool bold = false, FontFamily? f = null) => new()
    {
        Text = s, FontSize = size, Foreground = fg, FontFamily = f ?? Ui, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
    };

    static string[] Rows(Anim a, int f, Palette p)
    {
        var rows = Sprites.Frames[a][Math.Min(f, Sprites.Frames[a].Length - 1)];
        return p.Collar != 0 ? Sprites.Collar(rows, a) : rows;
    }

    static Image Spr(string[] rows, Palette p, double size, bool flip = false)
    {
        var i = new Image { Source = Sprites.Render(rows, p), Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.NearestNeighbor);
        if (flip) i.RenderTransform = new ScaleTransform(-1, 1);
        return i;
    }

    static Border Rounded(UIElement child, Brush bg, double pad = 22) =>
        new() { Child = child, Background = bg, CornerRadius = new CornerRadius(14), Padding = new Thickness(pad) };

    static void Save(FrameworkElement el, string path, double dpi = 96)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(el.DesiredSize));
        el.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.DesiredSize.Width * dpi / 96), (int)Math.Ceiling(el.DesiredSize.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        Console.WriteLine($"{Path.GetFileName(path)} {el.DesiredSize.Width:0}x{el.DesiredSize.Height:0} dip");
    }

    static UIElement Cell(UIElement art, string name, string? sub = null, double w = 150)
    {
        var s = new StackPanel { Width = w, Margin = new Thickness(0, 4, 0, 8) };
        s.Children.Add(art);
        s.Children.Add(T(name, 14, Text, true));
        if (sub is not null) s.Children.Add(T(sub, 11, Dim));
        return s;
    }

    // 8x nearest-neighbour: 16 px sprite -> 128 px.
    public static void Cats(string path)
    {
        string?[] collars = ["Red", null, "Yellow", null, "Green", "Blue", "Pink", null, "Purple", "Red"];
        var grid = new UniformGrid { Columns = 5 };
        for (int i = 0; i < Sprites.Coats.Count; i++)
        {
            var look = new CatLook { Coat = Sprites.Coats[i].Name, Collar = collars[i] };
            var p = Sprites.Dress(look, Sprites.Palettes[0]);
            var img = Spr(Rows(Anim.Sit, 0, p), p, 128);
            grid.Children.Add(Cell(img, look.Coat!, collars[i] is { } c ? c.ToLowerInvariant() + " collar" : " "));
        }
        Save(Rounded(grid, Panel), path);
    }

    static readonly (Anim A, string Label, int Frame)[] PoseList =
    [
        (Anim.Walk, "Walk", 1), (Anim.Run, "Run", 2), (Anim.Sit, "Sit", 0), (Anim.Loaf, "Loaf", 0), (Anim.Sleep, "Sleep", 0),
        (Anim.Curl, "Curl", 0), (Anim.Flat, "Flat", 0), (Anim.Stretch, "Stretch", 1), (Anim.Yawn, "Yawn", 1), (Anim.Lick, "Lick", 1),
        (Anim.Wash, "Wash", 1), (Anim.Hop, "Hop", 1), (Anim.Leap, "Leap", 0), (Anim.PawUp, "PawUp", 0), (Anim.Bat, "Bat", 1),
        (Anim.Sniff, "Sniff", 1), (Anim.Box, "Box", 0), (Anim.Sneeze, "Sneeze", 1), (Anim.Nuzzle, "Nuzzle", 0), (Anim.Crouch, "Crouch", 0),
        (Anim.Hiss, "Hiss", 0), (Anim.Eat, "Eat", 0), (Anim.Slip, "Slip", 0), (Anim.LookUp, "LookUp", 0), (Anim.Dangle, "Carried", 0),
        (Anim.Fly, "Thrown", 0), (Anim.Pumpkin, "Pumpkin", 0),
    ];

    public static void Poses(string path)
    {
        var p = Sprites.Palettes[0];
        var grid = new UniformGrid { Columns = 7 };
        foreach (var (a, label, f) in PoseList)
            grid.Children.Add(Cell(Spr(Rows(a, f, p), p, 128), label, null, 136));
        Save(Rounded(grid, Panel), path);
    }

    // The real card body (CardWindow.Fill) in the same box style as the live window, on a neutral panel.
    public static void HoverCards(string path)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var cards = new[]
        {
            HoverCard.For(new SessionInfo("a", @"C:\code\my-app", "my-app", SessionStatus.Working, now.AddMinutes(-18)),
                new Signals(Tool: "Bash", ToolAt: now.AddSeconds(-42), Open: 1, Edits: 7, Context: 98_000, MaxContext: 98_000, Model: "example-model-4"),
                "Mochi", TimeSpan.FromMinutes(18), 0, now),
            HoverCard.For(new SessionInfo("b", @"C:\code\notes-api", "notes-api", SessionStatus.Waiting, now.AddMinutes(-3), Tool: AgentTool.Codex),
                null, null, TimeSpan.FromMinutes(9), 0, now),
            HoverCard.For(new SessionInfo("c", @"C:\code\demo-site", "demo-site", SessionStatus.Working, now.AddMinutes(-35)),
                new Signals(Model: "example-model-4", Context: 180_000, MaxContext: 180_000, ApiError: "rate_limit", ApiErrors: 1),
                null, TimeSpan.FromMinutes(35), 2, now),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in cards)
        {
            var body = new StackPanel();
            CardWindow.Fill(body, c);
            var box = new Border { Background = B(0xF0202028), BorderBrush = B(0xFF9A9A9A), BorderThickness = new Thickness(2),
                Padding = new Thickness(6, 4, 6, 4), Child = body, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 18, 0) };
            TextOptions.SetTextRenderingMode(box, TextRenderingMode.Aliased);
            row.Children.Add(box);
        }
        ((FrameworkElement)row.Children[^1]).Margin = new Thickness(0);
        Save(Rounded(row, B(0xFF3A3A44), 22), path, 192);
    }

    // The real items from CatMenu.Fill (headers, checks, swatches, which are disabled or have submenus), drawn as a
    // light Windows-style menu: WPF cannot show a MenuItem popup offscreen.
    public static void Menu(string path)
    {
        var hooks = new CatMenu.Hooks((_, _) => { }, () => [new CatLook { Folder = @"C:\code\old-demo" }], () => { }, () => { }, () => false, () => { },
            () => new AppSettings(), _ => { });
        var look = new CatLook { Folder = @"C:\code\my-app", Name = "Mochi", Coat = "Calico", Collar = "Red" };
        var host = new ItemsControl();
        CatMenu.Fill(host.Items, @"C:\code\my-app", look, false, Trait.Curious, () => { }, () => { }, hooks);
        var items = host.Items.Cast<Control>().ToList();
        var coat = items.OfType<MenuItem>().First(m => m.Header as string == "Coat");

        UIElement Row(MenuItem m, bool hover)
        {
            var g = new Grid { Height = 28 };
            foreach (var w in new[] { 26.0, 22.0, 0.0, 22.0 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var fg = m.IsEnabled ? B(0xFF1B1B1B) : B(0xFF8A8A8A);
            void At(UIElement e, int col) { Grid.SetColumn(e, col); g.Children.Add(e); }
            if (m.IsChecked) At(new TextBlock { Text = "\u2713", FontSize = 13, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 0);
            if (m.Icon is UIElement ic) { ((FrameworkElement)ic).VerticalAlignment = VerticalAlignment.Center; At(ic, 1); }
            At(new TextBlock { Text = m.Header as string, FontFamily = Ui, FontSize = 13, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 14, 0) }, 2);
            if (m.Items.Count > 0) At(new TextBlock { Text = "\u203A", FontSize = 16, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 3);
            return new Border { Child = g, CornerRadius = new CornerRadius(4), Background = hover ? B(0xFFE3E3E3) : Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
        }

        Border Build(IEnumerable<Control> source, double width, MenuItem? hovered, Dictionary<MenuItem, UIElement> rows)
        {
            var stack = new StackPanel { Width = width };
            foreach (var c in source)
            {
                if (c is Separator) stack.Children.Add(new Rectangle { Height = 1, Fill = B(0xFFDCDCDC), Margin = new Thickness(6, 5, 6, 5) });
                else if (c is MenuItem m) { var r = Row(m, m == hovered); rows[m] = r; stack.Children.Add(r); }
            }
            return new Border { Child = stack, Background = B(0xFFFCFCFC), BorderBrush = B(0xFFCFCFCF), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(4) };
        }

        var mainRows = new Dictionary<MenuItem, UIElement>();
        var main = Build(items, 270, coat, mainRows);
        var sub = Build(coat.Items.Cast<Control>(), 190, null, new());
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        main.Measure(inf); main.Arrange(new Rect(main.DesiredSize));
        sub.Measure(inf);
        double coatY = mainRows[coat].TranslatePoint(new Point(), main).Y - 6;
        var canvas = new Canvas { Width = main.DesiredSize.Width - 6 + sub.DesiredSize.Width, Height = Math.Max(main.DesiredSize.Height, coatY + sub.DesiredSize.Height) };
        canvas.Children.Add(main);
        Canvas.SetLeft(sub, main.DesiredSize.Width - 6); Canvas.SetTop(sub, coatY);
        canvas.Children.Add(sub);
        Save(Rounded(canvas, Panel, 24), path, 192);
    }

    // A made-up taskbar with a few cats on it: sprites, bubbles and the Zzz are the app's own.
    public static void Strip(string path)
    {
        const double W = 640, H = 170, Floor = 122;   // feet sit on the taskbar's top edge
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var c = new Canvas { Width = W, Height = H, ClipToBounds = true };
        void Add(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }
        Add(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(Color.FromRgb(0x2F, 0x3E, 0x66), Color.FromRgb(0x7A, 0x5C, 0x93), 90) }, 0, 0);
        Add(new Rectangle { Width = W, Height = H - Floor, Fill = B(0xF01F1F25) }, 0, Floor);
        Add(new Rectangle { Width = W, Height = 1, Fill = B(0xFF3A3A44) }, 0, Floor);
        for (int i = 0; i < 6; i++)   // generic app tiles, centred
            Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = B(i == 2 ? 0xFF5B7BD5 : 0xFF4A4A55) }, 232 + i * 32, Floor + 12);
        Add(T("12:00", 11, B(0xFFD0D0D0)), 590, Floor + 10);
        Add(T("1/1/2026", 11, B(0xFFD0D0D0)), 580, Floor + 24);
        for (int i = 0; i < 3; i++) Add(new Ellipse { Width = 8, Height = 8, Fill = B(0xFF8A8A95) }, 520 + i * 16, Floor + 20);

        Palette Coat(string name, string? collar = null) => Sprites.Dress(new CatLook { Coat = name, Collar = collar }, Sprites.Palettes[0]);
        void Cat(Anim a, int f, double x, Palette p, bool flip = false) => Add(Spr(Rows(a, f, p), p, 48, flip), x, Floor - 48);
        Border Bubble(string s, bool murmur = false)
        {
            var b = murmur
                ? new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock { Text = s, FontFamily = Mono, FontSize = 12 } }
                : new Border { Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(2), Padding = new Thickness(4, 0, 4, 0),
                    Child = new TextBlock { Text = s, FontFamily = Mono, FontWeight = FontWeights.Bold, FontSize = 14 } };
            b.Measure(inf);
            return b;
        }
        void Say(string s, double catX, double dx = 12, bool murmur = false)
        {
            var b = Bubble(s, murmur);
            Add(b, Math.Round(catX + 24 + dx - b.DesiredSize.Width / 2), Floor - 48 - (murmur ? 20 : 24));
        }

        Cat(Anim.Sit, 0, 18, Coat("Orange tabby", "Red"));
        Cat(Anim.Walk, 1, 90, Coat("Grey"), flip: true); Say("mrrp", 90, -12, murmur: true);
        Cat(Anim.Sit, 0, 170, Coat("Calico", "Blue")); Say("MEOW!", 170);
        Cat(Anim.Sleep, 0, 250, Coat("Black")); Add(Spr(Sprites.Zzz[0], Sprites.Palettes[0], 48), 250 + 24 + 2, Floor - 48 - 24);
        Cat(Anim.Run, 2, 345, Coat("Tuxedo", "Yellow"));
        Add(Spr(Sprites.Mouse[0], Sprites.Palettes[0], 48), 430, Floor - 48);
        Cat(Anim.Sit, 0, 505, Coat("Cream", "Green")); Say("!", 505);
        Save(c, path, 192);
    }
}
