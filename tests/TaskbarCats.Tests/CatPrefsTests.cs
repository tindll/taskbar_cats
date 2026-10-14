using TaskbarCats;
using Xunit;

public class CatPrefsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "TaskbarCatsTests", Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "cats.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact] public void A_missing_file_starts_empty_and_is_not_created()
    {
        var p = new CatPrefs(FilePath);
        Assert.Equal(new CatLook { Folder = @"C:\Code\App" }, p.Get(@"C:\Code\App"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact] public void Set_then_a_new_store_reads_every_field_back()
    {
        // Kitten, Lazy and Mouse are enum value 0: they must survive WhenWritingDefault.
        var look = new CatLook { Name = "Bob", Coat = "Tuxedo", Plain = true, Size = Look.Kitten, Trait = Trait.Lazy,
                                 Collar = "Red", Toy = Toy.Mouse, Quiet = true };
        new CatPrefs(FilePath).Set(@"C:\Code\App", look);
        Assert.Equal(look with { Folder = @"C:\Code\App" }, new CatPrefs(FilePath).Get(@"C:\Code\App"));
    }

    [Fact] public void Folder_key_ignores_case_slashes_and_trailing_separators()
    {
        var p = new CatPrefs(FilePath);
        p.Set(@"C:\Code\App\", new CatLook { Coat = "Calico" });
        Assert.Equal("Calico", p.Get("c:/code/app").Coat);
        Assert.Equal("Calico", p.Get(@"C:\CODE\App").Coat);
        Assert.Equal(CatPrefs.Key("c:/code/app/"), CatPrefs.Key(@"C:\Code\App"));
    }

    [Fact] public void A_corrupt_file_is_kept_as_bak_and_the_store_starts_fresh()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        var p = new CatPrefs(FilePath);
        Assert.Null(p.Get(@"C:\Code\App").Coat);
        Assert.Equal("{ not json", File.ReadAllText(FilePath + ".bak"));
    }

    [Fact] public void The_default_look_removes_the_entry()
    {
        var p = new CatPrefs(FilePath);
        p.Set(@"C:\Code\App", new CatLook { Coat = "Grey" });
        p.Set(@"C:\Code\App", new CatLook());
        Assert.DoesNotContain("app", File.ReadAllText(FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Hidden_lists_hidden_projects_by_the_folder_first_seen()
    {
        var p = new CatPrefs(FilePath);
        p.Set(@"C:\Code\App", new CatLook { Coat = "Grey" });
        p.Set(@"c:\code\app\", p.Get(@"c:\code\app\") with { Hidden = true });
        p.Set(@"C:\Code\Other", new CatLook { Coat = "Black" });
        var h = Assert.Single(p.Hidden);
        Assert.Equal(@"C:\Code\App", h.Folder);
        Assert.Equal("Grey", h.Coat);
    }

    [Fact] public void Saving_leaves_no_temp_file()
    {
        new CatPrefs(FilePath).Set(@"C:\Code\App", new CatLook { Quiet = true });
        Assert.Equal(["cats.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact] public void Set_never_throws_when_the_file_cannot_be_written()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "blocker"), "");   // a file where the folder should be
        var p = new CatPrefs(Path.Combine(_dir, "blocker", "cats.json"));
        var stored = p.Set(@"C:\Code\App", new CatLook { Coat = "Grey" });
        Assert.Equal("Grey", stored.Coat);
        Assert.Equal("Grey", p.Get(@"C:\Code\App").Coat);   // kept in memory for this run
    }

    [Fact] public void Tip_shows_the_name_tag_first_when_there_is_one()
    {
        Assert.Equal("App · lazy", CatLooks.Tip(null, @"C:\Code\App\", Trait.Lazy));
        Assert.Equal("Bob · App · hyper", CatLooks.Tip("Bob", @"C:\Code\App", Trait.Hyper));
    }

    [Fact] public void CleanName_trims_strips_control_chars_and_caps()
    {
        Assert.Null(CatLooks.CleanName("   "));
        Assert.Equal("Bob", CatLooks.CleanName(" B\to\nb "));
        Assert.Equal(Tune.NameMax, CatLooks.CleanName(new string('x', 40))!.Length);
    }

    [Fact] public void FolderName_of_a_drive_root_is_the_drive()
    {
        Assert.Equal("C:", CatLooks.FolderName(@"C:\"));
        Assert.Equal("App", CatLooks.FolderName("c:/code/App/"));
    }
    [Fact] public void Randomize_changes_only_the_coat_plain_and_collar()
    {
        var l = new CatLook { Folder = @"C:\Code\App", Name = "Bob", Size = Look.Big, Trait = Trait.Shy, Toy = Toy.Yarn, Quiet = true };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = CatLooks.Randomize(l, new Random(seed));
            Assert.Contains(r.Coat, Sprites.Coats.Select(c => c.Name));
            Assert.True(r.Collar is null || Sprites.Collars.Any(c => c.Name == r.Collar));
            Assert.Equal(l, r with { Coat = null, Plain = false, Collar = null });
        }
    }
}
