namespace Hub.Tests.Helpers;

/// <summary>
/// The grid renders tile icons as a bare &lt;img&gt;, so a typo or a deleted file
/// shows up as a broken image in production rather than a build error.
/// </summary>
internal static class TileIconFiles
{
    /// <summary>True when an API icon path ("/images/icons/x.svg") or bare file name exists in wwwroot.</summary>
    internal static bool Exists(string icon)
    {
        var name = Uri.UnescapeDataString(icon.Replace("/images/icons/", string.Empty));
        return File.Exists(Path.Combine(RepoRoot(), "wwwroot", "images", "icons", name));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hub.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Hub.slnx not found above the test output");
    }
}
