using PickUper.Core.Configuration;
using Xunit;

namespace PickUper.Core.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Parses_comments_and_trailing_commas()
    {
        var result = ConfigLoader.Parse("""
            {
              // a comment
              "defaultBrowser": "edge",
              "browsers": { "edge": { "path": "msedge.exe" }, },
              "rules": [],
            }
            """);

        Assert.Null(result.Error);
        Assert.Equal("edge", result.Config!.DefaultBrowser);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Reports_broken_json_without_throwing()
    {
        var result = ConfigLoader.Parse("{ not json");

        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Contains("invalid JSON", result.Error);
    }

    [Fact]
    public void Warns_about_dangling_references_and_bad_patterns()
    {
        var result = ConfigLoader.Parse("""
            {
              "defaultBrowser": "ghost",
              "browsers": { "edge": { "path": "msedge.exe" } },
              "rules": [
                { "match": "*.github.com", "browser": "nope" },
                { "match": "regex:[oops", "browser": "edge" },
                { "browser": "edge" }
              ]
            }
            """);

        Assert.NotNull(result.Config);
        Assert.Contains(result.Warnings, w => w.Contains("'ghost'"));
        Assert.Contains(result.Warnings, w => w.Contains("'nope'"));
        Assert.Contains(result.Warnings, w => w.Contains("invalid regex"));
        Assert.Contains(result.Warnings, w => w.Contains("no 'match'"));
    }

    [Fact]
    public void Bundled_template_parses_cleanly()
    {
        var result = ConfigLoader.Parse(DefaultConfig.Template);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(4, result.Config!.Rules.Count);
    }

    [Fact]
    public void Loads_the_first_candidate_that_exists()
    {
        var home = Directory.CreateTempSubdirectory("pick-uper-test").FullName;
        try
        {
            var second = ConfigLoader.CandidatePaths(home)[1];
            File.WriteAllText(second, """{ "defaultBrowser": "edge", "browsers": { "edge": { "path": "msedge.exe" } } }""");

            var result = ConfigLoader.Load(home);
            Assert.Equal(second, result.SourcePath);
            Assert.Equal("edge", result.Config!.DefaultBrowser);

            var first = ConfigLoader.CandidatePaths(home)[0];
            File.WriteAllText(first, """{ "defaultBrowser": "chrome", "browsers": { "chrome": { "path": "chrome.exe" } } }""");

            Assert.Equal(first, ConfigLoader.Load(home).SourcePath);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Missing_config_is_reported_with_the_paths_it_looked_at()
    {
        var home = Directory.CreateTempSubdirectory("pick-uper-empty").FullName;
        try
        {
            var result = ConfigLoader.Load(home);

            Assert.Null(result.Config);
            Assert.Contains(".pick-uper.json", result.Error);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
