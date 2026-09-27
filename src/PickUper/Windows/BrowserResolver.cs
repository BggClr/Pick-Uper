using Microsoft.Win32;

namespace PickUper.Windows;

public sealed record ResolvedBrowser(string ExecutablePath, string Source);

/// <summary>A browser found on this machine, ready to drop straight into a config file.</summary>
public sealed record DetectedBrowser(string Key, string DisplayName, string ExecutablePath);

/// <summary>
/// Turns a configured <c>path</c> ("chrome.exe", "chrome", or a full path) into a real executable.
/// </summary>
public static class BrowserResolver
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>Friendly names accepted in the config, mapped to their executable name.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["edge"] = "msedge.exe",
        ["msedge"] = "msedge.exe",
        ["chrome"] = "chrome.exe",
        ["google chrome"] = "chrome.exe",
        ["firefox"] = "firefox.exe",
        ["brave"] = "brave.exe",
        ["opera"] = "opera.exe",
        ["vivaldi"] = "vivaldi.exe",
        ["chromium"] = "chrome.exe",
        ["yandex"] = "browser.exe",
        ["iexplore"] = "iexplore.exe",
        ["zen"] = "zen.exe",
        ["arc"] = "Arc.exe",
        ["librewolf"] = "librewolf.exe",
        ["waterfox"] = "waterfox.exe",
        ["tor"] = "firefox.exe",
    };

    /// <summary>Probed, in order, when nothing else claims to be a browser.</summary>
    private static readonly string[] AutoDetectOrder =
        ["msedge.exe", "chrome.exe", "firefox.exe", "brave.exe", "vivaldi.exe", "opera.exe"];

    /// <summary>
    /// One canonical (key, exe, display name) per browser, in preference order — this is
    /// what <see cref="DetectInstalled"/> scans and what <c>--init-config</c> uses to build
    /// a starter config's "browsers" section from what's actually installed.
    /// </summary>
    private static readonly (string Key, string ExeName, string DisplayName)[] KnownBrowsers =
    [
        ("edge", "msedge.exe", "Microsoft Edge"),
        ("chrome", "chrome.exe", "Google Chrome"),
        ("firefox", "firefox.exe", "Mozilla Firefox"),
        ("brave", "brave.exe", "Brave"),
        ("vivaldi", "vivaldi.exe", "Vivaldi"),
        ("opera", "opera.exe", "Opera"),
        ("yandex", "browser.exe", "Yandex Browser"),
        ("zen", "zen.exe", "Zen"),
        ("arc", "Arc.exe", "Arc"),
        ("librewolf", "librewolf.exe", "LibreWolf"),
        ("waterfox", "waterfox.exe", "Waterfox"),
        ("iexplore", "iexplore.exe", "Internet Explorer"),
    ];

    private static readonly string[] RelativeInstallPaths =
    [
        @"Google\Chrome\Application\chrome.exe",
        @"Microsoft\Edge\Application\msedge.exe",
        @"Mozilla Firefox\firefox.exe",
        @"BraveSoftware\Brave-Browser\Application\brave.exe",
        @"Vivaldi\Application\vivaldi.exe",
        @"Opera\opera.exe",
        @"Yandex\YandexBrowser\Application\browser.exe",
        @"Zen Browser\zen.exe",
        @"LibreWolf\librewolf.exe",
        @"Waterfox\waterfox.exe",
        @"Internet Explorer\iexplore.exe",
    ];

    public static ResolvedBrowser? Resolve(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));

        if (System.IO.Path.IsPathRooted(value))
        {
            return File.Exists(value) ? new ResolvedBrowser(value, "path") : null;
        }

        var exeName = Aliases.TryGetValue(value, out var alias) ? alias : value;
        if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exeName += ".exe";
        }

        var fromAppPaths = FromAppPaths(exeName);
        if (fromAppPaths is not null)
        {
            return new ResolvedBrowser(fromAppPaths, "App Paths");
        }

        var fromProgramFiles = FromInstallDirectories(exeName);
        if (fromProgramFiles is not null)
        {
            return new ResolvedBrowser(fromProgramFiles, "install directory");
        }

        var fromPath = FromEnvironmentPath(exeName);
        return fromPath is not null ? new ResolvedBrowser(fromPath, "PATH") : null;
    }

    /// <summary>Last resort when the config is missing or broken: any browser we can find.</summary>
    public static ResolvedBrowser? AutoDetect()
    {
        foreach (var exe in AutoDetectOrder)
        {
            var resolved = Resolve(exe);
            if (resolved is not null)
            {
                return resolved with { Source = "auto-detected" };
            }
        }

        return null;
    }

    /// <summary>Every browser we can find, for <c>--status</c> and <c>--init-config</c>.</summary>
    public static IEnumerable<DetectedBrowser> DetectInstalled()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, exeName, displayName) in KnownBrowsers)
        {
            var resolved = Resolve(exeName);
            if (resolved is not null && seen.Add(resolved.ExecutablePath))
            {
                yield return new DetectedBrowser(key, displayName, resolved.ExecutablePath);
            }
        }
    }

    private static string? FromAppPaths(string exeName)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var view in new[] { AppPathsKey, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" })
            {
                using var key = root.OpenSubKey($@"{view}\{exeName}");
                if (key?.GetValue(null) is string raw)
                {
                    var path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }

        return null;
    }

    private static string? FromInstallDirectories(string exeName)
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];

        foreach (var relative in RelativeInstallPaths)
        {
            if (!relative.EndsWith(@"\" + exeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                var candidate = System.IO.Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? FromEnvironmentPath(string exeName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = System.IO.Path.Combine(directory.Trim().Trim('"'), exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }
}
