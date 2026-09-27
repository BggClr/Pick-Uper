namespace PickUper.Core.Matching;

/// <summary>
/// The thing we were asked to open. Usually an http(s) URL, but Windows also hands us
/// local file paths when pick-uper is registered for .htm/.html.
/// </summary>
public sealed class TargetUrl
{
    private TargetUrl(string original, string scheme, string host, int port, string pathAndQuery, bool isWebUrl)
    {
        Original = original;
        Scheme = scheme;
        Host = host;
        Port = port;
        PathAndQuery = pathAndQuery;
        IsWebUrl = isWebUrl;
    }

    /// <summary>Exactly what was passed on the command line.</summary>
    public string Original { get; }

    public string Scheme { get; }

    /// <summary>Host without port; empty for file paths.</summary>
    public string Host { get; }

    /// <summary>-1 when the URL carries no explicit port.</summary>
    public int Port { get; }

    /// <summary>Always starts with '/'.</summary>
    public string PathAndQuery { get; }

    /// <summary>False for local files and anything that does not parse as an absolute URI.</summary>
    public bool IsWebUrl { get; }

    /// <summary>Host or host:port, matched when a pattern spells out a port.</summary>
    public string Authority => Port >= 0 ? $"{Host}:{Port}" : Host;

    public static TargetUrl Parse(string value)
    {
        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var isWeb = uri.Scheme is "http" or "https";
            var explicitPort = !uri.IsDefaultPort && uri.Port > 0;
            return new TargetUrl(
                trimmed,
                uri.Scheme,
                uri.Host,
                explicitPort ? uri.Port : -1,
                string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery,
                isWeb);
        }

        // "example.com/path" typed without a scheme — still routable by host.
        if (!trimmed.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate("http://" + trimmed, UriKind.Absolute, out var guessed) &&
            guessed.Host.Contains('.'))
        {
            return new TargetUrl(
                trimmed,
                "http",
                guessed.Host,
                guessed.IsDefaultPort ? -1 : guessed.Port,
                string.IsNullOrEmpty(guessed.PathAndQuery) ? "/" : guessed.PathAndQuery,
                isWebUrl: true);
        }

        return new TargetUrl(trimmed, scheme: "", host: "", port: -1, pathAndQuery: trimmed, isWebUrl: false);
    }

    public override string ToString() => Original;
}
