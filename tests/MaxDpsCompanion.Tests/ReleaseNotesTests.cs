using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 4.5.0 "Voidlight" release-notes gate. The in-app changelog is the release
/// contract for every future version (AGENTS.md, "Release notes"):
///
///  * <see cref="ReleaseNotes.Current"/> must be the version and codename the
///    assembly actually reports, so the Details panel can never advertise an
///    older release;
///  * the history must be newest-first with unique versions;
///  * docs/CHANGELOG.md must carry a heading for every in-app release (and the
///    current one in particular), so the long form and the short form are
///    bumped in the same commit.
///
/// These tests only read files and static data - no XAML runtime is loaded.
/// </summary>
public class ReleaseNotesTests
{
    [Fact]
    public void Current_MatchesAssemblyVersion()
        => Assert.Equal(Native.AppVersion, ReleaseNotes.Current.Version);

    [Fact]
    public void Current_MatchesAssemblyCodename()
        => Assert.Equal(Native.Codename, ReleaseNotes.Current.Codename);

    [Fact]
    public void Current_HasHighlights()
    {
        Assert.NotEmpty(ReleaseNotes.Current.Headline);
        Assert.NotEmpty(ReleaseNotes.Current.Highlights);
        Assert.All(ReleaseNotes.Current.Highlights, line => Assert.False(string.IsNullOrWhiteSpace(line)));
    }

    [Fact]
    public void History_IsNewestFirst_WithUniqueVersions()
    {
        Assert.NotEmpty(ReleaseNotes.All);

        var versions = ReleaseNotes.All.Select(r => r.Version).ToArray();
        Assert.Equal(versions.Length, versions.Distinct().Count());

        var parsed = versions.Select(Parse).ToArray();
        for (var i = 1; i < parsed.Length; i++)
        {
            Assert.True(parsed[i - 1] > parsed[i],
                $"{versions[i - 1]} must sort above {versions[i]} (newest first)");
        }
    }

    [Fact]
    public void ChangelogDocument_HasASectionForEveryInAppRelease()
    {
        var doc = ReadChangelog();
        foreach (var release in ReleaseNotes.All)
        {
            Assert.Contains($"## {release.Version} ", doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChangelogDocument_LeadsWithTheCurrentRelease()
    {
        var doc = ReadChangelog();
        var current = doc.IndexOf($"## {ReleaseNotes.Current.Version} ", StringComparison.Ordinal);
        Assert.True(current >= 0, $"docs/CHANGELOG.md has no section for {ReleaseNotes.Current.Version}");

        foreach (var other in ReleaseNotes.All.Skip(1))
        {
            var index = doc.IndexOf($"## {other.Version} ", StringComparison.Ordinal);
            Assert.True(index > current,
                $"{other.Version} must appear below {ReleaseNotes.Current.Version} in docs/CHANGELOG.md");
        }
    }

    /// <summary>Walks up from the test output directory to the checkout root.</summary>
    private static string ReadChangelog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "CHANGELOG.md");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "docs/CHANGELOG.md not found above " + AppContext.BaseDirectory);
    }

    private static Version Parse(string version)
        => Version.TryParse(version, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"release version '{version}' is not numeric");
}
