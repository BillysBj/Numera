using System.Text.RegularExpressions;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// GoBD/compliance wording guard (Phase 9, success criterion 4): a fast, DB-free unit
/// test that fails the build if a forbidden certification claim ("zertifiziert" /
/// "certified") is ever introduced into user-facing copy. Numera is designed to be
/// <em>GoBD-konform</em>; it must NEVER claim to be certified — a Verfahrensdokumentation
/// is a self-assessment, not a certification.
/// </summary>
/// <remarks>
/// The guard reads real files from the repository (the i18n locale JSON that drives all
/// user-facing text, plus the Verfahrensdokumentation draft) rather than any compiled
/// artefact, so a copy change is caught even without a rebuild of the web app. It walks up
/// from the test assembly directory to the repository root (the folder containing
/// <c>Numera.sln</c>). No database or container is required.
/// </remarks>
public sealed class WordingComplianceTests
{
    // Case-insensitive certification claim. "Zertifizierung" (the noun, used honestly to
    // negate a claim in the docs) does NOT match this participle, only the forbidden
    // "zertifiziert"/"certified" adjectives do.
    private static readonly Regex ForbiddenClaim =
        new("zertifiziert|certified", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<object[]> FilesToScan()
    {
        var root = RepoRoot();

        var localesDir = Path.Combine(root, "web", "src", "i18n", "locales");
        if (Directory.Exists(localesDir))
        {
            foreach (var json in Directory.EnumerateFiles(localesDir, "*.json", SearchOption.AllDirectories))
            {
                yield return new object[] { json };
            }
        }

        var doc = Path.Combine(root, "docs", "gobd-verfahrensdokumentation.md");
        if (File.Exists(doc))
        {
            yield return new object[] { doc };
        }
    }

    [Theory]
    [MemberData(nameof(FilesToScan))]
    public async Task File_contains_no_certification_claim(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        var match = ForbiddenClaim.Match(text);

        Assert.False(
            match.Success,
            $"Forbidden certification claim '{match.Value}' found in {path}. " +
            "Numera is 'GoBD-konform', never 'zertifiziert'/'certified' — a " +
            "Verfahrensdokumentation is a self-assessment, not a certification.");
    }

    [Fact]
    public void At_least_one_file_is_scanned()
    {
        // Guards against the walk-up silently finding nothing (which would make the
        // Theory vacuously pass and let a claim slip through unnoticed).
        Assert.NotEmpty(FilesToScan());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Numera.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate the repository root (Numera.sln) from the test assembly directory.");
        return dir!.FullName;
    }
}
