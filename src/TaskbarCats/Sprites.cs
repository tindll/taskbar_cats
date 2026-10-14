using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarCats;

public sealed record Palette(uint Fur, uint Dark, uint Light, uint Collar = 0);   // 0xAARRGGBB; Collar 0 = none ('R' pixels, Sprites.Collar)

public static partial class Sprites
{
    public const int Px = 16;

    public static readonly Palette[] Palettes =
    [
        new(0xFFE8913A, 0xFFB8601C, 0xFFFFE0B0),   // orange tabby
        new(0xFF8A8F98, 0xFF5A5E66, 0xFFD8DCE0),   // grey
        new(0xFF2B2B33, 0xFF15151A, 0xFF6A6A78),   // black
        new(0xFFF2EBDD, 0xFFC9BFA8, 0xFFFFFFFF),   // cream
        new(0xFF7A5230, 0xFF4E3218, 0xFFC9A27A),   // brown
    ];

    static readonly Dictionary<char, uint> Fixed = new()
    {
        ['.'] = 0x00000000, ['K'] = 0xFF1A1A1A, ['P'] = 0xFFF29BB0, ['E'] = 0xFF6BD86B,
        ['W'] = 0xFFFFFFFF, ['M'] = 0xFF6E7380, ['S'] = 0xFF5BC0FF, ['C'] = 0xFFD94A4A,
        ['G'] = 0xFF9A9A9A, ['A'] = 0xFFB8C8D8, ['B'] = 0xFF3D6FB6, ['Y'] = 0xFFE0B040,
        ['N'] = 0xFFC8955A, ['H'] = 0xFF8A5A2B, ['O'] = 0xFFF07818,   // cardboard, dark brown, pumpkin (v4)
        ['T'] = 0xFFFFD23C,   // gold: the collar medallion (never tinted by the collar colour)
    };

    public static Palette PaletteFor(string sessionId) => Palettes[sessionId.Sum(ch => ch) % Palettes.Length];

    /// The right-click menu's coats (CatLook.Coat stores the name). F = fur, D = stripes and far legs, L = chest and muzzle.
    public static readonly IReadOnlyList<(string Name, Palette P)> Coats =
    [
        ("Orange tabby", new(0xFFE8913A, 0xFFB8601C, 0xFFFFE0B0)),
        ("Grey", new(0xFF8A8F98, 0xFF5A5E66, 0xFFD8DCE0)),
        ("Black", new(0xFF2B2B33, 0xFF15151A, 0xFF6A6A78)),
        ("Cream", new(0xFFF2EBDD, 0xFFC9BFA8, 0xFFFFFFFF)),
        ("Brown", new(0xFF7A5230, 0xFF4E3218, 0xFFC9A27A)),
        ("White", new(0xFFFAFAF7, 0xFFD6D6DE, 0xFFFFFFFF)),
        ("Tuxedo", new(0xFF2B2B33, 0xFF15151A, 0xFFFFFFFF)),
        ("Siamese", new(0xFFEFE3CC, 0xFF5A4030, 0xFFFFF6E6)),
        ("Ginger & white", new(0xFFE8913A, 0xFFB8601C, 0xFFFFFFFF)),
        ("Calico", new(0xFFE8913A, 0xFF2B2B33, 0xFFFFFFFF)),
    ];

    /// The right-click menu's collar colours (CatLook.Collar stores the name; none = no collar).
    public static readonly IReadOnlyList<(string Name, uint Colour)> Collars =
    [
        ("Red", 0xFFD94A4A), ("Blue", 0xFF3D6FB6), ("Green", 0xFF4CAF50),
        ("Yellow", 0xFFE0B040), ("Pink", 0xFFF29BB0), ("Purple", 0xFF9B59B6),
    ];

    /// A project's palette: its coat (an unknown or missing name = `fallback`, the session's own palette), stripes
    /// painted over when Plain, and the collar colour (0 = none).
    public static Palette Dress(CatLook l, Palette fallback)
    {
        var p = Coats.FirstOrDefault(c => c.Name == l.Coat).P ?? fallback;
        if (l.Plain) p = p with { Dark = p.Fur };
        return p with { Collar = Collars.FirstOrDefault(c => c.Name == l.Collar).Colour };
    }

    /// Poses with no collar: the head is down in a bowl or box, or tucked under the tail, so there is no neck to ring.
    static readonly HashSet<Anim> NoNeck = [Anim.Box, Anim.Pumpkin, Anim.Sniff, Anim.Curl];

    /// `rows` with the collar drawn as 'R' (Palette.Collar) and the gold medallion as 'T'. The band is one horizontal row
    /// of fur directly under the head (the row after the nose 'P', or after the eyes 'E' when they sit lower, the
    /// run of fur at the front, at most four px wide, so it rings the neck and not the whole back); the medallion hangs from its front
    /// px on the row below. Rows under the nose with under two fur px are skipped. A pose with no neck comes back unchanged.
    public static string[] Collar(string[] rows, Anim a)
    {
        if (NoNeck.Contains(a)) return rows;
        int head = Math.Max(Array.FindIndex(rows, r => r.Contains('P')), Array.FindLastIndex(rows, r => r.Contains('E')));
        if (head < 0) return rows;
        if (a == Anim.LookUp) head++;   // nose and eyes are high in the lifted head; its chin is a row lower
        bool Fur(int y, int x) => y < rows.Length && x >= 0 && "FDL".Contains(rows[y][x]);
        foreach (int y in Enumerable.Range(head + 1, rows.Length - head - 1).Append(head))   // the head row itself is the last resort (a flat head with no row below)
        {
            int lim = y == head ? Math.Max(rows[y].IndexOf('P'), rows[y].IndexOf('E')) : Px, hi = lim <= 0 ? -1 : Array.FindLastIndex(rows[y][..lim].ToCharArray(), ch => "FDL".Contains(ch)), lo = hi;
            if (lim <= 0 || hi < 0) continue;
            while (lo > 0 && hi - lo < 3 && Fur(y, lo - 1)) lo--;
            if (hi - lo < 1) continue;
            var r = (string[])rows.Clone();
            void Put(int row, char ch) { for (int x = lo; x <= hi; x++) if (Fur(row, x)) r[row] = r[row][..x] + ch + r[row][(x + 1)..]; }
            Put(y, 'R');
            // no chest below the band (a low pose sits on the ground): the medallion takes the band's front px instead
            int mx = Enumerable.Range(lo, hi - lo + 1).LastOrDefault(x => Fur(y + 1, x), -1), my = mx < 0 ? y : y + 1;
            if (mx < 0) mx = hi;
            r[my] = r[my][..mx] + 'T' + r[my][(mx + 1)..];
            return r;
        }
        return rows;
    }
    public static uint[] ToPixels(string[] rows, Palette p)
    {
        var px = new uint[Px * Px];
        for (int y = 0; y < Px; y++)
            for (int x = 0; x < Px; x++)
                px[y * Px + x] = rows[y][x] switch { 'F' => p.Fur, 'D' => p.Dark, 'L' => p.Light, 'R' => p.Collar, var c => Fixed[c] };
        return px;
    }

    /// uint 0xAARRGGBB is BGRA in little-endian memory, so Bgra32 needs no byte shuffling.
    public static BitmapSource Render(string[] rows, Palette p)
    {
        var bmp = BitmapSource.Create(Px, Px, 96, 96, PixelFormats.Bgra32, null, ToPixels(rows, p), Px * 4);
        bmp.Freeze();
        return bmp;
    }

    static readonly string[][] WalkFrames =
    [
        [
            "................",
            "................",
            "................",
            "...K......K..K..",
            "..KFK....KFKKFK.",
            ".KFK.....KFFFFK.",
            ".KFK....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "..KDKKFKKKFDK...",
            "..KDKKFK.KFDK...",
            "..KDKKFK..KDK...",
            "...K..K....K....",
        ],
        [
            "................",
            "................",
            "...K......K..K..",
            "..KFK....KFKKFK.",
            ".KFK.....KFFFFK.",
            ".KFK....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "...KDFKKKKDFK...",
            "...KDFK..KDFK...",
            "...KDFK..KDFK...",
            "....KFK..KDK....",
            ".....K....K.....",
        ],
        [
            "................",
            "................",
            "................",
            ".K........K..K..",
            "KFK......KFKKFK.",
            "KFK......KFFFFK.",
            "KFK.....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "...KFDKKKDKKFK..",
            "...KFDK.KDKKFK..",
            "...KFK..KDKKFK..",
            "....K....K..K...",
        ],
        [
            "................",
            "................",
            "................",
            ".K........K..K..",
            "KFK......KFKKFK.",
            "KFK......KFFFFK.",
            "KFK.....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "..KFKKDKKKDFK...",
            "..KFKKDK.KDFK...",
            "..KFKKDK..KFK...",
            "...K..K....K....",
        ],
        [
            "................",
            "................",
            ".K........K..K..",
            "KFK......KFKKFK.",
            "KFK......KFFFFK.",
            ".KFK....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "...KFDKKKKFDK...",
            "...KFDK..KFDK...",
            "...KFDK..KFDK...",
            "....KDK..KFK....",
            ".....K....K.....",
        ],
        [
            "................",
            "................",
            "................",
            ".K........K..K..",
            "KFK......KFKKFK.",
            "KFK......KFFFFK.",
            ".KFK....KFFFEFFK",
            ".KFKKKKKKFFFFFPK",
            "..KFFFFFFFLLLKK.",
            "..KFDFFDFFFLLK..",
            "..KFFFFFFFFFFK..",
            "...KLLLLLLLLLK..",
            "...KDFKKKFKKDK..",
            "...KDFK.KFKKDK..",
            "...KDK..KFKKDK..",
            "....K....K..K...",
        ],
    ];

    public static readonly IReadOnlyDictionary<Anim, string[][]> Frames = new Dictionary<Anim, string[][]>
    {
        [Anim.Walk] = WalkFrames,
        [Anim.Run] = WalkFrames,   // same legs, faster frame rate (Cat.Timing)
        [Anim.Sit] = SitFrames, [Anim.Hop] = HopFrames, [Anim.Leap] = LeapFrames,   // profile head (SpriteArt.cs)
        [Anim.Sniff] = SniffFrames, [Anim.Bat] = BatFrames, [Anim.LookUp] = LookUpFrames,
        [Anim.Box] = BoxFrames, [Anim.Pumpkin] = PumpkinFrames, [Anim.Loaf] = LoafFrames,
        [Anim.Sneeze] = SneezeFrames, [Anim.Nuzzle] = NuzzleFrames, [Anim.Slip] = SlipFrames,
        [Anim.Curl] = CurlFrames, [Anim.Flat] = FlatFrames,   // v5 sleep poses (SpriteArt.cs)
        [Anim.Lick] =
        [
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFFPFFFK",
                ".........KFLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFFPFFFK",
                ".........KFLLPKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFKFFKFK",
                "........KFFPFFFK",
                ".........KFLLPKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFKFFKFK",
                "........KFFPFFFK",
                ".........KFLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
        ],
        [Anim.Wash] =
        [
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KLFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFFPFFFK",
                ".........KFLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KLLLFFFK",
                "........KFKFFEFK",
                "........KFFPFFFK",
                ".........KFLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFFPFFFK",
                ".........KLLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFLLLFFK",
                "........KFKFFEFK",
                "........KFFPFFFK",
                ".........KFLLFKK",
                "...K.....KFFFKFK",
                "..KDK...KFFFFFKK",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
        ],
        [Anim.Stretch] =
        [
            [
                "................",
                "................",
                "................",
                "..K.............",
                ".KFK............",
                ".KFK............",
                ".KFKKK..........",
                ".KFFFFK.........",
                ".KFFFFFK..K...K.",
                ".KFDFFFFKKFK.KFK",
                ".KFFDFFFFKFFKFFK",
                "..KFFFFFKFFFFFFK",
                "..KFFFFFKFEFFEFK",
                "..KFKKKKKFFPFFFK",
                "..KFK...KKFLLFK.",
                "..KKK....KKKKK..",
            ],
            [
                "................",
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                ".K......KFEFFEFK",
                "KFK.....KFFPFFFK",
                "KFKKKKKKKKFLLFK.",
                "KFFFFFFFFFFFFK..",
                "KFFDFFDFFDFFFK..",
                "KKFLLLLLLLLFKK..",
                ".KFKKKKKKKKKFK..",
                "KFK.........KFK.",
                "KKK.........KKK.",
            ],
            [
                "................",
                "................",
                ".K..............",
                ".K..KKKKK.K...K.",
                ".K.KFFFFFKFK.KFK",
                ".KKFFDFDFKFFKFFK",
                ".KFFFFFFKFFFFFFK",
                ".KFFFKKKKFEFFEFK",
                ".KFFK...KFFPFFFK",
                "KFFK.....KFLLFK.",
                "KFFK.....KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KKK......KKKKK..",
            ],
        ],
        [Anim.Yawn] =
        [
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFFPFFFK",
                ".........KFLLFK.",
                "...K.....KFFFK..",
                "..KDK...KFFFFFK.",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFEFFEFK",
                "........KFPPPFFK",
                ".........KFLLFK.",
                "...K.....KFFFK..",
                "..KDK...KFFFFFK.",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
            [
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "........KFFFFFFK",
                "........KFKFFKFK",
                "........KFFPFFFK",
                ".........KFLLFK.",
                "...K.....KFFFK..",
                "..KDK...KFFFFFK.",
                "..KFK..KFFLLFFK.",
                "...KFK.KFFLLFFK.",
                "....KFKFFFLLFFK.",
                ".....KFFFFFFFFK.",
                "......KKKKKKKK..",
            ],
        ],
        [Anim.Sleep] =
        [
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "..........K..K..",
                "....KKKKKKFKKFK.",
                "..KKFFFFFKFFFFFK",
                ".KFFDFFDFKFKFKFK",
                "KFFFFFFFFFFFPFFK",
                "KFLLLFFFFFFLLLK.",
                ".KKKKKKKKKKKKKK.",
            ],
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "....KKKK..K..K..",
                "...KFFFFKKFKKFK.",
                "..KFFFFFFKFFFFFK",
                ".KFFDFFDFKFKFKFK",
                "KFFFFFFFFFFFPFFK",
                "KFLLLFFFFFFLLLK.",
                ".KKKKKKKKKKKKKK.",
            ],
        ],
        [Anim.PawUp] =
        [
            [
                "................",
                "................",
                "........K...K.K.",
                ".......KFK.KFKFK",
                ".......KFFKFFKFK",
                "......KFFFFFFKFK",
                "......KFEFFEFKFK",
                "......KFFPFFFKFK",
                ".......KFLLFKKFK",
                ".K.....KFFFK.KFK",
                "KDK...KFFFFFKKK.",
                "KFK..KFFLLFFK...",
                ".KFK.KFFLLFFK...",
                "..KFKFFFLLFFK...",
                "...KFFFFFFFFK...",
                "....KKKKKKKK....",
            ],
            [
                "................",
                "..............K.",
                "........K...KKFK",
                ".......KFK.KFKFK",
                ".......KFFKFFKFK",
                "......KFFFFFFKFK",
                "......KFEFFEFKFK",
                "......KFFPFFFKFK",
                ".......KFLLFKKFK",
                ".K.....KFFFK.KFK",
                "KDK...KFFFFFKKK.",
                "KFK..KFFLLFFK...",
                ".KFK.KFFLLFFK...",
                "..KFKFFFLLFFK...",
                "...KFFFFFFFFK...",
                "....KKKKKKKK....",
            ],
        ],
        [Anim.Hiss] =
        [
            [
                "................",
                "................",
                ".K..K.K.K.......",
                ".K..KKKKK.K...K.",
                ".K.KFFFFFKFK.KFK",
                ".KKFFDFDFKFFKFFK",
                ".KFFFFFFKFFFFFFK",
                ".KFFFKKKKFEFFEFK",
                ".KFFK...KFFPFFFK",
                "KFFK.....KFLLFK.",
                "KFFK.....KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KKK......KKKKK..",
            ],
            [
                "................",
                "................",
                ".K..K.K.K.......",
                ".K..KKKKK.K...K.",
                ".K.KFFFFFKFK.KFK",
                ".KKFFDFDFKFFKFFK",
                ".KFFFFFFKFFFFFFK",
                ".KFFFKKKKFEFFEFK",
                ".KFFK...KFFPFFFK",
                "KFFK.....KPWPFK.",
                "KFFK.....KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KFK......KFFFK..",
                "KKK......KKKKK..",
            ],
        ],
        [Anim.Crouch] =
        [
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "..........K...K.",
                "K........KFK.KFK",
                ".K.......KFFKFFK",
                "..KKKKKKKFFFFFFK",
                ".KFFDFFDKFEFFEFK",
                "KFFFFFFFKFFPFFFK",
                "KFLLLLLFFKFLLFK.",
                ".KKKKKKKKKKKKKK.",
            ],
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "..........K...K.",
                ".........KFK.KFK",
                ".........KFFKFFK",
                "KKKKKKKKKFFFFFFK",
                ".KFFDFFDKFEFFEFK",
                "KFFFFFFFKFFPFFFK",
                "KFLLLLLFFKFLLFK.",
                ".KKKKKKKKKKKKKK.",
            ],
        ],
        [Anim.Dangle] =
        [
            [
                "................",
                "....K......K....",
                "...KFKKKKKKFK...",
                "...KFFFFFFFFK...",
                "...KFEFFFFEFK...",
                "....KFFPPFFK....",
                ".....KFLLFK.....",
                "....KFFLLFFK....",
                "...KFKFLLFKFK...",
                "...KFKFLLFKFK...",
                "...KLKFLLFKLK...",
                "....KKFFFFKK....",
                ".....KFKKFKK....",
                ".....KFKKFKFK...",
                ".....KLKKLFK....",
                "......K..KK.....",
            ],
            [
                "................",
                "....K......K....",
                "...KFKKKKKKFK...",
                "...KFFFFFFFFK...",
                "...KFEFFFFEFK...",
                "....KFFPPFFK....",
                ".....KFLLFK.....",
                "....KFFLLFFK....",
                "...KFKFLLFKFK...",
                "...KFKFLLFKFK...",
                "...KLKFLLFKLK...",
                "....KKFFFFKK....",
                ".....KFKKFK.....",
                ".....KFKKFKK....",
                ".....KLKKLFFK...",
                "......K..KKK....",
            ],
            [
                "................",
                "....K......K....",
                "...KFKKKKKKFK...",
                "...KFFFFFFFFK...",
                "...KFEFFFFEFK...",
                "...KFFPPFFKK....",
                "....KFLLFK......",
                "...KFFLLFFK.....",
                "..KFKFLLFKFK....",
                "..KFKFLLFKFK....",
                "..KLKFLLFKLK....",
                "...KKFFFFKK.....",
                "...KFKKFKK......",
                "...KFKKFKFK.....",
                "...KLKKLFK......",
                "....K..KK.......",
            ],
            [
                "................",
                "....K......K....",
                "...KFKKKKKKFK...",
                "...KFFFFFFFFK...",
                "...KFEFFFFEFK...",
                "....KKFFPPFFK...",
                "......KFLLFK....",
                ".....KFFLLFFK...",
                "....KFKFLLFKFK..",
                "....KFKFLLFKFK..",
                "....KLKFLLFKLK..",
                ".....KKFFFFKK...",
                "......KKFKKFK...",
                ".....KFKFKKFK...",
                "......KFLKKLK...",
                ".......KK..K....",
            ],
        ],
        [Anim.Fly] =
        [
            [
                "................",
                "................",
                "................",
                "..........K..K..",
                ".........KFKKFK.",
                ".K.......KFFFFK.",
                "KFK.....KFFFEFFK",
                ".KFKKKKKKFFFFFPK",
                "..KFFFFFFFLLLKK.",
                "..KFDFFDFFFLLK..",
                "..KFFFFFFFFFFK..",
                ".KDFLLLLLLLLLDK.",
                "KDKFKKKKKKKKFDK.",
                "KDKKFK......KFK.",
                ".K.KFK......KFK.",
                "....K........K..",
            ],
            [
                "................",
                "................",
                "..K.............",
                ".KFK......K..K..",
                ".KFK.....KFKKFK.",
                ".KFK.....KFFFFK.",
                "..KFK...KFFFEFFK",
                "..KFKKKKKFFFFFPK",
                "...KFFFFFFLLLKK.",
                "..KFDFFDFFFLLK..",
                "..KFFFFFFFFFFK..",
                "..KLLLLLLLLLLK..",
                "..KDKFKKKKDKFK..",
                "..KDKFK..KDKFK..",
                "..KLKLK..KLKLK..",
                "...K.K....K.K...",
            ],
        ],
        [Anim.Eat] =
        [
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                ".......K...K....",
                "......KFK.KFK...",
                "......KFFKFFK...",
                "KKKKKKFFFFFFK...",
                "KFDFFKFKFFKFK...",
                "KFFFFKFFPFFFKKKK",
                "KFLLLFKFLLFKAABK",
                ".KKKKKKKKKKKKKKK",
            ],
            [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                ".......K...K....",
                "......KFK.KFK...",
                "......KFFKFFK...",
                "KKKKKKFFFFFFK...",
                "KFDFFKFKFFKFK...",
                "KFFFFKFFPFFFKKK.",
                "KFLLLFKFPLFKABK.",
                ".KKKKKKKKKKKKKK.",
            ],
        ],
    };

    // Props: fixed colours only, so every palette renders them the same.
    static readonly string[] Mouse0 =
    [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "......KK........",
                ".....KPPK.......",
                "...KKKGGGKK.....",
                "KKKGGGGGKGGPK...",
                "...KKKKKKKKKK...",
    ];
    static readonly string[] Mouse1 =
    [
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "......KK........",
                ".....KPPK.......",
                "K..KKKGGGKK.....",
                ".KKGGGGGKGGPK...",
                "...KK.KKK.KKK...",
    ];
    public static readonly string[] Sardine =
    [
                "................",
                "................",
                "................",
                "................",
                "................",
                "...KKKKKKKK..K..",
                "..KBBBBBBBBKKBK.",
                ".KWKAAAAAAAAAAK.",
                "..KAAAAAAAAKKAK.",
                "...KKKKKKKK..K..",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
    ];

    /// The sleep bubble: big Z by the head, small Z farthest away, lit one after another so they float up.
    /// White with a dark drop shadow, so it reads on dark and light taskbars. Not a cat frame: no lint for ground or shape.
    public static readonly string[][] Zzz =
    [
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "WWWWWWK.........",
            "KKKKWKK.........",
            "...WKK..........",
            "..WKK...........",
            ".WKK............",
            "WWWWWWK.........",
            "KKKKKKK.........",
        ],
        [
            "................",
            "................",
            "................",
            "......WWWWWK....",
            "......KKKWKK....",
            "........WKK.....",
            ".......WKK......",
            "......WWWWWK....",
            "......KKKKKK....",
            "WWWWWWK.........",
            "KKKKWKK.........",
            "...WKK..........",
            "..WKK...........",
            ".WKK............",
            "WWWWWWK.........",
            "KKKKKKK.........",
        ],
        [
            "............WWWW",
            "............KKWK",
            ".............WKK",
            "......WWWWWKWWWW",
            "......KKKWKKKKKK",
            "........WKK.....",
            ".......WKK......",
            "......WWWWWK....",
            "......KKKKKK....",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
        [
            "............WWWW",
            "............KKWK",
            ".............WKK",
            "............WWWW",
            "............KKKK",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
    ];
    public const int ZzzMs = 350;

    /// Which Zzz frame shows at a clock time in ms (Environment.TickCount64): one step every ZzzMs, looping.
    public static int ZzzFrame(long ms) => (int)(ms / ZzzMs % Zzz.Length);

    public static readonly string[][] Mouse = [Mouse0, Mouse1];
}
