using System.Diagnostics;
using System.Reflection;
using PickUper.Core.Configuration;
using PickUper.Core.Diagnostics;
using PickUper.Core.Matching;
using PickUper.Core.Routing;
using PickUper.Windows;

namespace PickUper;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);

        if (options.Url is not null && !options.IsCommand)
        {
            return Route(options.Url, options.ConfigPath);
        }

        // Anything that talks to the user needs the console of whoever started us.
        ConsoleBridge.AttachToParent();

        return options.Command switch
        {
            CliCommand.Install => Install(options),
            CliCommand.Uninstall => Uninstall(options),
            CliCommand.Register => Register(),
            CliCommand.Unregister => Unregister(),
            CliCommand.Status => Status(options.ConfigPath),
            CliCommand.Check => Check(options.Url, options.ConfigPath),
            CliCommand.InitConfig => InitConfig(options.ConfigPath),
            CliCommand.SetDefault => SetDefault(),
            CliCommand.Version => PrintVersion(),
            CliCommand.Help => PrintHelp(0),
            _ => PrintHelp(1),
        };
    }

    // ---------------------------------------------------------------- routing

    private static int Route(string rawUrl, string? configPath)
    {
        var load = LoadConfig(configPath);
        var url = TargetUrl.Parse(rawUrl);
        var route = Router.Resolve(load.Config, url);

        if (load.Error is not null)
        {
            Log.Warn($"{load.Error} — falling back to an auto-detected browser");
        }

        Log.Info($"url: {url.Original} | {route.Describe()}");

        var resolved = ResolveExecutable(route);
        if (resolved is null)
        {
            Log.Error($"no browser found for {url.Original} ({route.Describe()})");
            return 1;
        }

        var result = BrowserLauncher.Launch(resolved.ExecutablePath, route.Arguments);
        if (result.Success)
        {
            return 0;
        }

        // The configured browser is gone or refused to start: still open the link somewhere.
        var fallback = BrowserResolver.AutoDetect();
        if (fallback is not null && !IsSelf(fallback.ExecutablePath))
        {
            Log.Warn($"retrying with auto-detected {fallback.ExecutablePath}");
            if (BrowserLauncher.Launch(fallback.ExecutablePath, [url.Original]).Success)
            {
                return 0;
            }
        }

        Log.Error($"could not open {url.Original}: {result.Error}");
        return 1;
    }

    private static ResolvedBrowser? ResolveExecutable(Route route)
    {
        var resolved = BrowserResolver.Resolve(route.Browser?.Path);

        if (resolved is not null && IsSelf(resolved.ExecutablePath))
        {
            // A rule pointing back at pick-uper would loop forever.
            Log.Error($"browser '{route.BrowserKey}' points at pick-uper itself — ignoring it");
            resolved = null;
        }

        if (resolved is null && route.Browser?.Path is { } configured)
        {
            Log.Warn($"browser '{route.BrowserKey}' ({configured}) was not found");
        }

        if (resolved is null)
        {
            resolved = BrowserResolver.AutoDetect();
            if (resolved is not null && IsSelf(resolved.ExecutablePath))
            {
                resolved = null;
            }
        }

        return resolved;
    }

    private static bool IsSelf(string executablePath)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(executablePath),
                Path.GetFullPath(self),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // --------------------------------------------------------------- commands

    private static int Install(CommandLineOptions options)
    {
        if (!Installer.IsInstalled)
        {
            var sourceExe = Environment.ProcessPath;
            if (sourceExe is null)
            {
                Console.Error.WriteLine("cannot determine the path of the running executable");
                return 1;
            }

            try
            {
                Installer.CopyInto(sourceExe);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"install failed: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"Installed to {Installer.TargetExecutablePath}");

            // Re-run the rest of the setup through the installed copy, so steps that key
            // off the running executable's own path (register, status) see it correctly.
            return RunInstalledSetup(options);
        }

        try
        {
            Installer.CreateStartMenuShortcut(Environment.ProcessPath!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warning: could not create the Start Menu shortcut: {ex.Message}");
        }

        var configPath = options.ConfigPath ?? ConfigLoader.PreferredPath(HomeDirectory());
        if (!File.Exists(configPath))
        {
            var initResult = InitConfig(options.ConfigPath);
            if (initResult != 0)
            {
                return initResult;
            }
        }
        else
        {
            Console.WriteLine($"Config already exists: {configPath}");
        }

        var registerResult = Register();
        if (registerResult != 0)
        {
            return registerResult;
        }

        Console.WriteLine();
        Status(options.ConfigPath);

        return options.NoSettings ? 0 : SetDefault();
    }

    private static int RunInstalledSetup(CommandLineOptions options)
    {
        var startInfo = new ProcessStartInfo(Installer.TargetExecutablePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add("--install");
        if (options.ConfigPath is not null)
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(options.ConfigPath);
        }

        if (options.NoSettings)
        {
            startInfo.ArgumentList.Add("--no-settings");
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine($"could not start {Installer.TargetExecutablePath}");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int Uninstall(CommandLineOptions options)
    {
        try
        {
            BrowserRegistration.Unregister();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warning: unregistration failed: {ex.Message}");
        }

        Installer.KillOtherRunningInstances();
        Installer.RemoveStartMenuShortcut();
        Installer.RemoveLogDirectory();

        if (options.RemoveConfig)
        {
            var configPath = options.ConfigPath ?? ConfigLoader.PreferredPath(HomeDirectory());
            try
            {
                File.Delete(configPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"warning: could not remove {configPath}: {ex.Message}");
            }
        }

        // The install directory holds the exe we're running from — it can only be removed
        // after this process exits, so hand that off to a short-lived detached script.
        Installer.ScheduleInstallDirectoryRemoval();

        Console.WriteLine("pick-uper removed. Pick another browser in Settings > Apps > Default apps.");
        return 0;
    }

    private static int Register()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            Console.Error.WriteLine("cannot determine the path of the running executable");
            return 1;
        }

        try
        {
            BrowserRegistration.Register(exe);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"registration failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Registered '{BrowserRegistration.ApplicationName}' at {exe}");
        Console.WriteLine();
        Console.WriteLine("Windows does not let an app make itself the default browser.");
        Console.WriteLine("Finish in Settings -> Apps -> Default apps -> Pick-Uper, or run:");
        Console.WriteLine("  pick-uper --set-default");
        return 0;
    }

    private static int Unregister()
    {
        try
        {
            BrowserRegistration.Unregister();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unregistration failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Unregistered. Pick another default browser in Settings.");
        return 0;
    }

    private static int SetDefault()
    {
        if (BrowserRegistration.RegisteredExecutable() is null)
        {
            Console.Error.WriteLine("not registered yet — run 'pick-uper --register' first");
            return 1;
        }

        try
        {
            BrowserRegistration.OpenDefaultAppsSettings();
            Console.WriteLine("Opened Settings — pick 'Pick-Uper' for HTTP, HTTPS, .htm and .html.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not open Settings: {ex.Message}");
            return 1;
        }
    }

    private static int Status(string? configPath)
    {
        Console.WriteLine($"pick-uper {Version()}");
        Console.WriteLine($"executable : {Environment.ProcessPath}");

        var registered = BrowserRegistration.RegisteredExecutable();
        Console.WriteLine($"registered : {registered ?? "no"}");
        Console.WriteLine($"default    : {(BrowserRegistration.IsDefaultBrowser() ? "yes" : "no")}" +
                          $" (https -> {BrowserRegistration.CurrentUserChoice("https") ?? "unknown"})");

        var load = LoadConfig(configPath);
        Console.WriteLine($"config     : {load.SourcePath ?? "(none)"}");
        if (load.Error is not null)
        {
            Console.WriteLine($"  error: {load.Error}");
        }

        foreach (var warning in load.Warnings)
        {
            Console.WriteLine($"  warning: {warning}");
        }

        if (load.Config is { } config)
        {
            Console.WriteLine($"  rules: {config.Rules.Count}, browsers: {config.Browsers.Count}," +
                              $" default: {config.DefaultBrowser ?? "(none)"}");

            foreach (var (key, spec) in config.Browsers)
            {
                var resolved = BrowserResolver.Resolve(spec.Path);
                Console.WriteLine($"  - {key,-16} {(resolved?.ExecutablePath ?? $"NOT FOUND ({spec.Path})")}");
            }
        }

        Console.WriteLine("browsers found on this machine:");
        foreach (var browser in BrowserResolver.DetectInstalled())
        {
            Console.WriteLine($"  - {browser.Key,-10} {browser.DisplayName,-20} {browser.ExecutablePath}");
        }

        return 0;
    }

    private static int Check(string? rawUrl, string? configPath)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            Console.Error.WriteLine("usage: pick-uper --check <url>");
            return 1;
        }

        var load = LoadConfig(configPath);
        Console.WriteLine($"config : {load.SourcePath ?? "(none)"}");
        if (load.Error is not null)
        {
            Console.WriteLine($"error  : {load.Error}");
        }

        foreach (var warning in load.Warnings)
        {
            Console.WriteLine($"warning: {warning}");
        }

        var url = TargetUrl.Parse(rawUrl);
        var route = Router.Resolve(load.Config, url);
        var resolved = ResolveExecutable(route);

        Console.WriteLine($"url    : {url.Original}");
        Console.WriteLine($"host   : {url.Authority}{(url.IsWebUrl ? string.Empty : "  (not a web url)")}");
        Console.WriteLine($"match  : {route.Describe()}");
        Console.WriteLine($"exe    : {resolved?.ExecutablePath ?? "NOT FOUND"}" +
                          $"{(resolved is null ? string.Empty : $"  [{resolved.Source}]")}");
        Console.WriteLine($"command: {resolved?.ExecutablePath ?? "?"} " +
                          string.Join(' ', route.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        return resolved is null ? 1 : 0;
    }

    private static int InitConfig(string? configPath)
    {
        var path = configPath ?? ConfigLoader.PreferredPath(HomeDirectory());

        if (File.Exists(path))
        {
            Console.Error.WriteLine($"{path} already exists — not overwriting it");
            return 1;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, BuildStarterConfig());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not write {path}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Wrote {path} — edit it, then check a URL with 'pick-uper --check https://github.com'.");
        return 0;
    }

    /// <summary>
    /// A config seeded with the browsers actually found on this machine, at their real
    /// paths, so a fresh install needs no editing to work. Falls back to the static
    /// example template when nothing is detected.
    /// </summary>
    private static string BuildStarterConfig()
    {
        var detected = BrowserResolver.DetectInstalled().ToList();
        if (detected.Count == 0)
        {
            return DefaultConfig.Template;
        }

        var config = new PickUperConfig
        {
            DefaultBrowser = detected[0].Key,
            Browsers = new Dictionary<string, BrowserSpec>(StringComparer.OrdinalIgnoreCase),
            Logging = new LoggingOptions { Enabled = false },
        };

        foreach (var browser in detected)
        {
            config.Browsers[browser.Key] = new BrowserSpec { Name = browser.DisplayName, Path = browser.ExecutablePath };
        }

        var header = """
            // pick-uper — routes every clicked link to the browser you want.
            //
            // "browsers" below were detected on this machine, with their real paths. Add
            // more yourself (any absolute .exe path works), or a separate profile by
            // reusing a "path" with different "args", e.g. "--profile-directory=Profile 1"
            // for Chrome/Edge or "-P name" for Firefox.
            //
            // Add "rules" to send specific sites elsewhere — evaluated top to bottom, the
            // first match wins, e.g.:
            //   { "match": "*.github.com", "browser": "chrome" }
            //   { "matches": ["*.slack.com", "*.atlassian.net"], "browser": "work" }
            // Patterns:  example.com | *.example.com | example.com/path/* | https://*.example.com/*
            //            regex:^https://.*\.corp\.example\.com/
            // Unmatched URLs go to "defaultBrowser".

            """;

        return header + ConfigLoader.Serialize(config) + Environment.NewLine;
    }

    // ---------------------------------------------------------------- helpers

    private static ConfigLoadResult LoadConfig(string? configPath)
    {
        var load = ConfigLoader.Load(HomeDirectory(), configPath);

        Log.FilePath = load.Config?.Logging?.Path is { Length: > 0 } custom
            ? Environment.ExpandEnvironmentVariables(custom)
            : Log.DefaultPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Log.Verbose = load.Config?.Logging?.Enabled ?? false;

        foreach (var warning in load.Warnings)
        {
            Log.Warn($"config: {warning}");
        }

        return load;
    }

    private static string HomeDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile)
            ? Environment.GetEnvironmentVariable("USERPROFILE") ?? Directory.GetCurrentDirectory()
            : profile;
    }

    private static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private static int PrintVersion()
    {
        Console.WriteLine($"pick-uper {Version()}");
        return 0;
    }

    private static int PrintHelp(int exitCode)
    {
        var output = exitCode == 0 ? Console.Out : Console.Error;
        output.WriteLine($"""
            pick-uper {Version()} — a default browser that forwards links to other browsers.

            Usage:
              pick-uper <url>              open the URL in the browser your rules select
              pick-uper --install          install to {Installer.InstallDirectory}, register and set up a config
              pick-uper --uninstall        undo --install: unregister, remove shortcut and installed files
              pick-uper --register         register as a browser candidate (HKCU, no admin)
              pick-uper --unregister       remove the registration
              pick-uper --set-default      open Windows Settings to finish making it default
              pick-uper --status           show registration, config and detected browsers
              pick-uper --check <url>      show which browser a URL would open, without opening it
              pick-uper --init-config      write a starter {Path.GetFileName(ConfigLoader.PreferredPath(""))}
              pick-uper --version          print the version

            Options:
              --config <path>              use this config file instead of the default locations
              --no-settings                with --install, skip opening the Default apps page
              --remove-config              with --uninstall, also delete the config file

            Config is read from (first match wins):
              %USERPROFILE%\.pick-uper.json
              %USERPROFILE%\pick-uper.json
              %USERPROFILE%\.config\pick-uper\config.json
              or the path in %PICKUPER_CONFIG%
            """);
        return exitCode;
    }
}
