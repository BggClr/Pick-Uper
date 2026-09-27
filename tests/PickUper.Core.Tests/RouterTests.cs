using PickUper.Core.Configuration;
using PickUper.Core.Matching;
using PickUper.Core.Routing;
using Xunit;

namespace PickUper.Core.Tests;

public class RouterTests
{
    private const string Json = """
        {
          // comments and trailing commas are allowed
          "defaultBrowser": "edge",
          "browsers": {
            "edge":   { "path": "msedge.exe" },
            "chrome": { "path": "chrome.exe" },
            "work":   { "path": "chrome.exe", "args": ["--profile-directory=Profile 1"] },
            "private":{ "path": "firefox.exe", "args": ["-private-window", "{url}"] },
          },
          "rules": [
            { "matches": ["*.slack.com", "*.atlassian.net"], "browser": "work" },
            { "match": "*.github.com", "browser": "chrome" },
            { "match": "secret.example.com/*", "browser": "private" },
          ],
        }
        """;

    private static PickUperConfig Config()
    {
        var result = ConfigLoader.Parse(Json);
        Assert.Null(result.Error);
        Assert.NotNull(result.Config);
        return result.Config;
    }

    private static Route Resolve(string url) => Router.Resolve(Config(), TargetUrl.Parse(url));

    [Fact]
    public void First_matching_rule_wins()
    {
        var route = Resolve("https://acme.slack.com/messages");
        Assert.Equal(RouteKind.Rule, route.Kind);
        Assert.Equal("work", route.BrowserKey);
        Assert.Equal(0, route.RuleIndex);
        Assert.Equal(["--profile-directory=Profile 1", "https://acme.slack.com/messages"], route.Arguments);
    }

    [Fact]
    public void Unmatched_url_goes_to_the_default_browser()
    {
        var route = Resolve("https://example.org/");
        Assert.Equal(RouteKind.Default, route.Kind);
        Assert.Equal("edge", route.BrowserKey);
        Assert.Equal(["https://example.org/"], route.Arguments);
    }

    [Fact]
    public void Url_placeholder_is_substituted_in_place()
    {
        var route = Resolve("https://secret.example.com/vault");
        Assert.Equal("private", route.BrowserKey);
        Assert.Equal(["-private-window", "https://secret.example.com/vault"], route.Arguments);
    }

    [Fact]
    public void Rule_level_args_override_the_browser_args()
    {
        var config = Config();
        config.Rules.Insert(0, new Rule { Match = "*.github.com", Browser = "work", Args = ["--incognito"] });

        var route = Router.Resolve(config, TargetUrl.Parse("https://github.com/x"));
        Assert.Equal("work", route.BrowserKey);
        Assert.Equal(["--incognito", "https://github.com/x"], route.Arguments);
    }

    [Fact]
    public void Missing_config_falls_back_instead_of_failing()
    {
        var route = Router.Resolve(null, TargetUrl.Parse("https://example.org/"));
        Assert.Equal(RouteKind.Fallback, route.Kind);
        Assert.Equal(["https://example.org/"], route.Arguments);
    }

    [Fact]
    public void Rule_pointing_at_an_unknown_browser_is_skipped_not_fatal()
    {
        var config = Config();
        config.Rules.Insert(0, new Rule { Match = "*.github.com", Browser = "does-not-exist" });

        var route = Router.Resolve(config, TargetUrl.Parse("https://github.com/x"));
        Assert.Equal("chrome", route.BrowserKey);
    }

    [Fact]
    public void Unknown_default_browser_falls_back()
    {
        var config = Config();
        config.DefaultBrowser = "nope";

        var route = Router.Resolve(config, TargetUrl.Parse("https://example.org/"));
        Assert.Equal(RouteKind.Fallback, route.Kind);
    }

    [Fact]
    public void Browser_keys_are_case_insensitive()
    {
        var config = Config();
        config.Rules.Insert(0, new Rule { Match = "*.example.net", Browser = "CHROME" });

        var route = Router.Resolve(config, TargetUrl.Parse("https://a.example.net/"));
        Assert.Equal("chrome.exe", route.Browser!.Path);
    }
}
