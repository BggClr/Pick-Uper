using PickUper.Core.Matching;
using Xunit;

namespace PickUper.Core.Tests;

public class UrlMatcherTests
{
    [Theory]
    // exact host
    [InlineData("github.com", "https://github.com/anthropics", true)]
    [InlineData("github.com", "https://gist.github.com/x", false)]
    [InlineData("GitHub.COM", "https://github.com/", true)]
    // subdomain wildcard covers the apex too
    [InlineData("*.github.com", "https://github.com/", true)]
    [InlineData("*.github.com", "https://gist.github.com/x", true)]
    [InlineData("*.github.com", "https://a.b.github.com/x", true)]
    [InlineData("*.github.com", "https://evilgithub.com/x", false)]
    [InlineData("*.github.com", "https://github.com.evil.ru/x", false)]
    // path prefixes
    [InlineData("github.com/anthropics/*", "https://github.com/anthropics/claude", true)]
    [InlineData("github.com/anthropics/*", "https://github.com/anthropics", true)]
    [InlineData("github.com/anthropics/*", "https://github.com/other", false)]
    [InlineData("mail.google.com/*", "https://mail.google.com/", true)]
    [InlineData("*.google.com/mail/*", "https://mail.google.com/mail/u/0", true)]
    // query strings are part of the path pattern space
    [InlineData("youtube.com/watch*", "https://youtube.com/watch?v=abc", true)]
    // ports
    [InlineData("localhost:*/*", "http://localhost:3000/app", true)]
    [InlineData("localhost:3000/*", "http://localhost:3000/app", true)]
    [InlineData("localhost:3000/*", "http://localhost:4000/app", false)]
    [InlineData("localhost/*", "http://localhost/app", true)]
    // full-url patterns
    [InlineData("https://*.corp.local/*", "https://vpn.corp.local/x", true)]
    [InlineData("https://*.corp.local/*", "http://vpn.corp.local/x", false)]
    // regex
    [InlineData(@"regex:^https://([^/]*\.)?corp\.local/", "https://vpn.corp.local/x", true)]
    [InlineData(@"regex:^https://([^/]*\.)?corp\.local/", "https://corp.local.evil/x", false)]
    // single-char wildcard
    [InlineData("t?st.com", "https://test.com/", true)]
    [InlineData("t?st.com", "https://teest.com/", false)]
    public void Matches_pattern_against_url(string pattern, string url, bool expected) =>
        Assert.Equal(expected, UrlMatcher.Matches(pattern, TargetUrl.Parse(url)));

    [Fact]
    public void Host_patterns_do_not_match_local_files()
    {
        var url = TargetUrl.Parse(@"file:///C:/temp/report.html");
        Assert.False(UrlMatcher.Matches("*.github.com", url));
        Assert.False(url.IsWebUrl);
    }

    [Fact]
    public void Regex_patterns_can_match_anything_including_files()
    {
        var url = TargetUrl.Parse(@"file:///C:/temp/report.html");
        Assert.True(UrlMatcher.Matches(@"regex:\.html$", url));
    }

    [Fact]
    public void Bare_host_without_scheme_is_still_routable()
    {
        var url = TargetUrl.Parse("example.com/docs");
        Assert.True(url.IsWebUrl);
        Assert.Equal("example.com", url.Host);
        Assert.True(UrlMatcher.Matches("example.com/docs*", url));
    }

    [Fact]
    public void Invalid_regex_is_reported_and_never_matches()
    {
        Assert.NotNull(UrlMatcher.ValidatePattern("regex:[unclosed"));
        Assert.False(UrlMatcher.Matches("regex:[unclosed", TargetUrl.Parse("https://x.com/")));
        Assert.Null(UrlMatcher.ValidatePattern("*.github.com"));
    }
}
