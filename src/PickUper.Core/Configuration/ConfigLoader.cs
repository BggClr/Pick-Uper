using System.Text.Json;

namespace PickUper.Core.Configuration;

public sealed record ConfigLoadResult(
    PickUperConfig? Config,
    string? SourcePath,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public bool IsLoaded => Config is not null;

    public static ConfigLoadResult NotFound(IReadOnlyList<string> searched) =>
        new(null, null, $"config not found, searched: {string.Join(", ", searched)}", []);
}

public static class ConfigLoader
{
    /// <summary>Overrides the search entirely when set.</summary>
    public const string PathEnvironmentVariable = "PICKUPER_CONFIG";

    /// <summary>Config file locations, in probing order.</summary>
    public static IReadOnlyList<string> CandidatePaths(string homeDirectory) =>
    [
        Path.Combine(homeDirectory, ".pick-uper.json"),
        Path.Combine(homeDirectory, "pick-uper.json"),
        Path.Combine(homeDirectory, ".config", "pick-uper", "config.json"),
    ];

    /// <summary>Path used by <c>--init-config</c>.</summary>
    public static string PreferredPath(string homeDirectory) => CandidatePaths(homeDirectory)[0];

    /// <summary>
    /// The config path that would actually be used — same override-then-candidate-list
    /// order as <see cref="Load"/> — or null if none of them exist yet. Used by
    /// <c>--init-config</c> to tell "no config anywhere" from "one exists, just not at the
    /// preferred path", so it never writes a second, shadowing file.
    /// </summary>
    public static string? FindExisting(string homeDirectory, string? explicitPath = null)
    {
        var overridePath = explicitPath ?? Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath) ? overridePath : null;
        }

        return CandidatePaths(homeDirectory).FirstOrDefault(File.Exists);
    }

    public static ConfigLoadResult Load(string homeDirectory, string? explicitPath = null)
    {
        var overridePath = explicitPath ?? Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath)
                ? LoadFrom(overridePath)
                : new ConfigLoadResult(null, overridePath, $"config not found: {overridePath}", []);
        }

        var existing = FindExisting(homeDirectory);
        return existing is not null ? LoadFrom(existing) : ConfigLoadResult.NotFound(CandidatePaths(homeDirectory));
    }

    private static ConfigLoadResult LoadFrom(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new ConfigLoadResult(null, path, $"cannot read {path}: {ex.Message}", []);
        }

        var parsed = Parse(json);
        return parsed with { SourcePath = path };
    }

    public static ConfigLoadResult Parse(string json)
    {
        PickUperConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, PickUperJsonContext.Default.PickUperConfig);
        }
        catch (JsonException ex)
        {
            return new ConfigLoadResult(null, null, $"invalid JSON: {ex.Message}", []);
        }

        if (config is null)
        {
            return new ConfigLoadResult(null, null, "config is empty", []);
        }

        // Deserialization replaces the field initializer, so restore the case-insensitive
        // lookup that rules rely on when they reference a browser key.
        if (config.Browsers.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            config.Browsers = new Dictionary<string, BrowserSpec>(config.Browsers, StringComparer.OrdinalIgnoreCase);
        }

        return new ConfigLoadResult(config, null, null, Validate(config));
    }

    /// <summary>
    /// Non-fatal problems. A config with warnings still routes: unknown browser references
    /// simply fall through to the default browser.
    /// </summary>
    public static IReadOnlyList<string> Validate(PickUperConfig config)
    {
        var warnings = new List<string>();

        if (config.Browsers.Count == 0)
        {
            warnings.Add("'browsers' is empty — every URL will fall back to an auto-detected browser");
        }

        foreach (var (key, spec) in config.Browsers)
        {
            if (string.IsNullOrWhiteSpace(spec?.Path))
            {
                warnings.Add($"browser '{key}' has no 'path'");
            }
        }

        if (string.IsNullOrWhiteSpace(config.DefaultBrowser))
        {
            warnings.Add("'defaultBrowser' is not set — unmatched URLs go to an auto-detected browser");
        }
        else if (!config.Browsers.ContainsKey(config.DefaultBrowser))
        {
            warnings.Add($"'defaultBrowser' references unknown browser '{config.DefaultBrowser}'");
        }

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            var patterns = rule.AllPatterns().ToList();

            if (patterns.Count == 0)
            {
                warnings.Add($"rule #{i + 1} has no 'match' or 'matches'");
            }

            foreach (var pattern in patterns)
            {
                var error = Matching.UrlMatcher.ValidatePattern(pattern);
                if (error is not null)
                {
                    warnings.Add($"rule #{i + 1}: {error}");
                }
            }

            if (string.IsNullOrWhiteSpace(rule.Browser))
            {
                warnings.Add($"rule #{i + 1} has no 'browser'");
            }
            else if (!config.Browsers.ContainsKey(rule.Browser))
            {
                warnings.Add($"rule #{i + 1} references unknown browser '{rule.Browser}'");
            }
        }

        return warnings;
    }

    public static string Serialize(PickUperConfig config) =>
        JsonSerializer.Serialize(config, PickUperJsonContext.Default.PickUperConfig);
}
