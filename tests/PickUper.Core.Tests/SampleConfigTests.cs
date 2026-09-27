using PickUper.Core.Configuration;
using Xunit;

namespace PickUper.Core.Tests;

public class SampleConfigTests
{
    /// <summary>
    /// pick-uper.sample.json in the repo root is what the README points at, while
    /// --init-config writes DefaultConfig.Template. Keep them identical.
    /// </summary>
    [Fact]
    public void Sample_file_matches_the_embedded_template()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var samplePath = Path.Combine(repoRoot, "pick-uper.sample.json");

        Assert.True(File.Exists(samplePath), $"missing {samplePath}");

        Assert.Equal(
            Normalize(DefaultConfig.Template),
            Normalize(File.ReadAllText(samplePath)));
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").TrimEnd();
}
