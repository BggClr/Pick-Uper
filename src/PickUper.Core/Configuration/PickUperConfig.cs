using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickUper.Core.Configuration;

/// <summary>
/// Root of ~/.pick-uper.json.
/// </summary>
public sealed class PickUperConfig
{
    /// <summary>Key from <see cref="Browsers"/> used when no rule matches.</summary>
    [JsonPropertyName("defaultBrowser")]
    public string? DefaultBrowser { get; set; }

    /// <summary>Named browser definitions. Keys are referenced by rules.</summary>
    [JsonPropertyName("browsers")]
    public Dictionary<string, BrowserSpec> Browsers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Evaluated top to bottom; the first match wins.</summary>
    [JsonPropertyName("rules")]
    public List<Rule> Rules { get; set; } = [];

    [JsonPropertyName("logging")]
    public LoggingOptions? Logging { get; set; }
}

/// <summary>How to start one browser.</summary>
public sealed class BrowserSpec
{
    /// <summary>Optional human readable name, used in logs and <c>--status</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Executable: an absolute path, or a bare name such as <c>chrome.exe</c> that is looked up
    /// in the "App Paths" registry key and in the usual install locations.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// Extra arguments. If any of them contains the <c>{url}</c> placeholder it is substituted,
    /// otherwise the URL is appended as the last argument.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }
}

/// <summary>One routing rule.</summary>
public sealed class Rule
{
    /// <summary>Single pattern. Combined with <see cref="Matches"/> if both are present.</summary>
    [JsonPropertyName("match")]
    public string? Match { get; set; }

    /// <summary>Several patterns; the rule fires if any of them matches.</summary>
    [JsonPropertyName("matches")]
    public List<string>? Matches { get; set; }

    /// <summary>Key from <c>browsers</c>.</summary>
    [JsonPropertyName("browser")]
    public string? Browser { get; set; }

    /// <summary>Replaces the browser's own <c>args</c> for URLs routed by this rule.</summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    /// <summary>Free-form note, ignored by the router.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    public IEnumerable<string> AllPatterns()
    {
        if (!string.IsNullOrWhiteSpace(Match))
        {
            yield return Match;
        }

        if (Matches is null)
        {
            yield break;
        }

        foreach (var pattern in Matches)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                yield return pattern;
            }
        }
    }
}

public sealed class LoggingOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Defaults to %LOCALAPPDATA%\PickUper\pick-uper.log.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PickUperConfig))]
public sealed partial class PickUperJsonContext : JsonSerializerContext;
