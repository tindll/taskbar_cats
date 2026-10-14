using TaskbarCats;
using Xunit;

public class AppSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "TaskbarCatsTests", Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact] public void Missing_file_gives_cards_on_sounds_off_and_creates_nothing()
    {
        var s = AppSettings.Load(FilePath);
        Assert.True(s.HoverCards);
        Assert.False(s.Sounds);
        Assert.False(File.Exists(FilePath));
    }

    [Fact] public void Save_then_load_round_trips()
    {
        new AppSettings { HoverCards = false, Sounds = true }.Save(FilePath);
        Assert.Equal(new AppSettings { HoverCards = false, Sounds = true }, AppSettings.Load(FilePath));
    }

    [Fact] public void A_missing_key_keeps_its_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "Sounds": true }""");
        Assert.Equal(new AppSettings { HoverCards = true, Sounds = true }, AppSettings.Load(FilePath));
    }

    [Theory]
    [InlineData("{ not json")] [InlineData("null")] [InlineData("[]")] [InlineData("")]
    public void A_bad_file_gives_the_defaults(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, text);
        Assert.Equal(new AppSettings(), AppSettings.Load(FilePath));
    }

    [Fact] public void Save_into_a_missing_folder_creates_it()
    {
        new AppSettings { Sounds = true }.Save(FilePath);
        Assert.True(AppSettings.Load(FilePath).Sounds);
    }
}
