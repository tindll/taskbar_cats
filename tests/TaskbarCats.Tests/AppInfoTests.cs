using System.Xml.Linq;
using TaskbarCats;

namespace TaskbarCats.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_comes_from_csproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "TaskbarCats.slnx"))) dir = dir.Parent!;
        var csproj = XDocument.Load(Path.Combine(dir.FullName, "src", "TaskbarCats", "TaskbarCats.csproj"));
        string expected = csproj.Descendants("Version").Single().Value;

        Assert.Equal(expected, AppInfo.Version);
        Assert.NotEqual("1.0.0", AppInfo.Version);   // the SDK default when <Version> is missing
    }
}
