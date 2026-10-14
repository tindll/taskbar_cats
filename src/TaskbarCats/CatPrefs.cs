using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarCats;

/// One project's look, chosen in the cat's right-click menu (CatMenu.cs). Null / false = the default.
public sealed record CatLook
{
    public string Folder { get; init; } = "";   // the project folder as first seen (shown in "Hidden cats")
    public string? Name { get; init; }          // name tag, CatLooks.CleanName
    public string? Coat { get; init; }          // a Sprites.Coats name; null = the session's palette
    public bool Plain { get; init; }            // no stripes: the dark colour is the fur colour
    public Look? Size { get; init; }            // null = by model (Tune.LookOf)
    public Trait? Trait { get; init; }          // null = by session hash (Tune.TraitOf)
    public string? Collar { get; init; }        // a Sprites.Collars name; null = no collar
    public Toy? Toy { get; init; }              // null = Pursuits takes turns
    public bool Quiet { get; init; }            // MEOW without blinking, no murmurs
    public bool Hidden { get; init; }           // the project's cats stay off the taskbar (Herd.Shown)
}

/// Per-project looks in %APPDATA%\TaskbarCats\cats.json, keyed by the normalised project folder, so every
/// conversation in one folder shares one look. Loaded once and saved on every change (a temp file, then replace).
/// A missing file means a fresh start. An unreadable one is kept as cats.json.bak and the store starts fresh.
public sealed class CatPrefs
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarCats", "cats.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly string _path;
    readonly Dictionary<string, CatLook> _looks;

    public CatPrefs(string path) { _path = path; _looks = Load(path); }

    /// Case-insensitive, either slash, no trailing separator: C:\Code\App\ and c:/code/app are one project.
    public static string Key(string cwd) => cwd.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    public CatLook Get(string cwd) => _looks.TryGetValue(Key(cwd), out var l) ? l : new CatLook { Folder = cwd };

    /// Stores `look` for cwd's project and saves. Setting the default look removes the entry. Returns what is stored.
    public CatLook Set(string cwd, CatLook look)
    {
        string key = Key(cwd);
        look = look with { Folder = _looks.TryGetValue(key, out var old) ? old.Folder : cwd };
        if (look with { Folder = "" } == new CatLook()) _looks.Remove(key); else _looks[key] = look;
        Save();
        return look;
    }

    public IReadOnlyList<CatLook> Hidden =>
        _looks.Values.Where(l => l.Hidden).OrderBy(l => l.Folder, StringComparer.OrdinalIgnoreCase).ToList();

    static Dictionary<string, CatLook> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var looks = new Dictionary<string, CatLook>();
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, CatLook?>>(File.ReadAllText(path), Json) ?? [])
                if (v is not null) looks[Key(k)] = v;
            return looks;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Copy(path, path + ".bak", overwrite: true); }
            catch (Exception c) when (c is IOException or UnauthorizedAccessException) { }
            return [];
        }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_looks, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        // ponytail: a failed save (disk full, file locked) keeps the look in memory for this run; the next change retries.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// App-wide switches from the tray menu, in %APPDATA%\TaskbarCats\settings.json. Missing or unreadable = the defaults.
public sealed record AppSettings
{
    public bool HoverCards { get; init; } = true;   // the hover card; off = the plain tooltip
    public bool Sounds { get; init; }               // a chime when a cat finishes or starts waiting

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarCats", "settings.json");

    public static AppSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        // ponytail: a failed save keeps the switch for this run; the next toggle retries.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// Small pure helpers for the menu and the tooltip.
public static class CatLooks
{
    /// The project folder's own name ("App" for C:\Code\App\); a drive root keeps its drive ("C:").
    public static string FolderName(string cwd)
    {
        string t = cwd.TrimEnd('\\', '/');
        string n = Path.GetFileName(t);
        return n.Length > 0 ? n : t;
    }

    /// "Bob · App · lazy", or "App · lazy" with no name tag.
    public static string Tip(string? name, string cwd, Trait t) =>
        (name is null ? "" : name + " · ") + $"{FolderName(cwd)} · {t.ToString().ToLowerInvariant()}";

    /// A surprise coat, maybe plain, maybe a collar (one in seven: none). Name, size, personality, toy, quiet stay.
    public static CatLook Randomize(CatLook l, Random r)
    {
        int collar = r.Next(Sprites.Collars.Count + 1);
        return l with
        {
            Coat = Sprites.Coats[r.Next(Sprites.Coats.Count)].Name,
            Plain = r.Next(4) == 0,
            Collar = collar < Sprites.Collars.Count ? Sprites.Collars[collar].Name : null,
        };
    }

    /// Control characters dropped, trimmed, at most Tune.NameMax characters; empty means no name tag.
    public static string? CleanName(string text)
    {
        string n = new string(text.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (n.Length > Tune.NameMax) n = n[..Tune.NameMax].TrimEnd();
        return n.Length == 0 ? null : n;
    }
}
