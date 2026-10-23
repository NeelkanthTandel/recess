namespace Recess.Core.Tests;

public class DataFolderTests
{
    [Fact]
    public void DefaultsToRecessInAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Recess");
        Assert.Equal(expected, DataFolder.Resolve(null));
        Assert.Equal(expected, DataFolder.Resolve("  "));
    }

#if DEBUG
    [Fact]
    public void DebugBuildsHonourTheOverride()
    {
        Assert.Equal(Path.GetFullPath("devdata"), DataFolder.Resolve("devdata"));
    }
#else
    [Fact]
    public void ReleaseBuildsIgnoreTheOverride()
    {
        Assert.NotEqual(Path.GetFullPath("devdata"), DataFolder.Resolve("devdata"));
    }
#endif
}
