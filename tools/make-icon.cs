#:project ../src/TaskbarCats/TaskbarCats.csproj
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishTrimmed=false
#:property PublishAot=false

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarCats;

// Writes src/TaskbarCats/TaskbarCats.ico: the orange sitting cat, nearest-neighbour scaled, one PNG per size.
// Run from the repo root: dotnet run tools/make-icon.cs
uint[] src = Sprites.ToPixels(Sprites.Frames[Anim.Sit][0], Sprites.Palettes[0]);
int[] sizes = [16, 32, 48, 256];
var pngs = sizes.Select(size =>
{
    int k = size / Sprites.Px;
    var px = new uint[size * size];
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = src[(y / k) * Sprites.Px + x / k];
    var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}).ToArray();

using var f = File.Create(Path.Combine("src", "TaskbarCats", "TaskbarCats.ico"));
using var w = new BinaryWriter(f);
w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);   // ICONDIR
int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)                                    // ICONDIRENTRY
{
    w.Write((byte)(sizes[i] % 256)); w.Write((byte)(sizes[i] % 256));     // 256 is stored as 0
    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
    w.Write(pngs[i].Length); w.Write(offset);
    offset += pngs[i].Length;
}
foreach (var png in pngs) w.Write(png);
Console.WriteLine($"Wrote TaskbarCats.ico ({f.Length} bytes)");
