using PickUper.Core.Configuration;
using PickUper.Core.Matching;

namespace PickUper.Core.Routing;

public enum RouteKind
{
    /// <summary>A rule matched.</summary>
    Rule,

    /// <summary>No rule matched; <c>defaultBrowser</c> was used.</summary>
    Default,

    /// <summary>Nothing usable in the config; the platform layer has to auto-detect a browser.</summary>
    Fallback,
}

public sealed record Route(
    RouteKind Kind,
    string? BrowserKey,
    BrowserSpec? Browser,
    IReadOnlyList<string> Arguments,
    string? MatchedPattern,
    int RuleIndex,
    string? Reason)
{
    public string Describe() => Kind switch
    {
        RouteKind.Rule => $"rule #{RuleIndex + 1} ('{MatchedPattern}') -> {BrowserKey}",
        RouteKind.Default => $"default -> {BrowserKey}",
        _ => $"fallback (auto-detect): {Reason}",
    };
}

public static class Router
{
    public static Route Resolve(PickUperConfig? config, TargetUrl url)
    {
        if (config is null)
        {
            return Fallback("no config loaded", url);
        }

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            if (string.IsNullOrWhiteSpace(rule.Browser) ||
                !config.Browsers.TryGetValue(rule.Browser, out var spec))
            {
                continue; // validated separately; never block the click
            }

            foreach (var pattern in rule.AllPatterns())
            {
                if (!UrlMatcher.Matches(pattern, url))
                {
                    continue;
                }

                var args = ArgumentBuilder.Build(rule.Args ?? spec.Args, url.Original);
                return new Route(RouteKind.Rule, rule.Browser, spec, args, pattern, i, null);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultBrowser) &&
            config.Browsers.TryGetValue(config.DefaultBrowser, out var defaultSpec))
        {
            return new Route(
                RouteKind.Default,
                config.DefaultBrowser,
                defaultSpec,
                ArgumentBuilder.Build(defaultSpec.Args, url.Original),
                null,
                -1,
                null);
        }

        return Fallback(
            config.DefaultBrowser is null
                ? "no rule matched and 'defaultBrowser' is not set"
                : $"no rule matched and 'defaultBrowser' ('{config.DefaultBrowser}') is not defined in 'browsers'",
            url);
    }

    private static Route Fallback(string reason, TargetUrl url) =>
        new(RouteKind.Fallback, null, null, [url.Original], null, -1, reason);
}

public static class ArgumentBuilder
{
    public const string UrlPlaceholder = "{url}";

    /// <summary>
    /// Substitutes <c>{url}</c> where the user put it, or appends the URL when the template
    /// does not mention it.
    /// </summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<string>? template, string url)
    {
        if (template is null || template.Count == 0)
        {
            return [url];
        }

        var hasPlaceholder = template.Any(
            a => a.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase));

        var result = new List<string>(template.Count + 1);
        foreach (var arg in template)
        {
            result.Add(hasPlaceholder
                ? arg.Replace(UrlPlaceholder, url, StringComparison.OrdinalIgnoreCase)
                : arg);
        }

        if (!hasPlaceholder)
        {
            result.Add(url);
        }

        return result;
    }
}
