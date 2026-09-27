namespace PickUper;

internal enum CliCommand
{
    None,
    OpenUrl,
    Install,
    Uninstall,
    Register,
    Unregister,
    SetDefault,
    Status,
    Check,
    InitConfig,
    Version,
    Help,
}

internal sealed record CommandLineOptions(
    CliCommand Command, string? Url, string? ConfigPath, bool NoSettings = false, bool RemoveConfig = false)
{
    public bool IsCommand => Command is not (CliCommand.None or CliCommand.OpenUrl);
}

internal static class CommandLine
{
    public static CommandLineOptions Parse(string[] args)
    {
        var command = CliCommand.None;
        string? url = null;
        string? configPath = null;
        var noSettings = false;
        var removeConfig = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--install" or "--setup":
                    command = CliCommand.Install;
                    break;
                case "--uninstall":
                    command = CliCommand.Uninstall;
                    break;
                case "--no-settings":
                    noSettings = true;
                    break;
                case "--remove-config":
                    removeConfig = true;
                    break;
                case "--register" or "-r" or "/register":
                    command = CliCommand.Register;
                    break;
                case "--unregister" or "-u" or "/unregister":
                    command = CliCommand.Unregister;
                    break;
                case "--set-default" or "--make-default":
                    command = CliCommand.SetDefault;
                    break;
                case "--status" or "-s" or "/status":
                    command = CliCommand.Status;
                    break;
                case "--check" or "--dry-run" or "-c":
                    command = CliCommand.Check;
                    break;
                case "--init-config" or "--init":
                    command = CliCommand.InitConfig;
                    break;
                case "--version" or "-v":
                    command = CliCommand.Version;
                    break;
                case "--help" or "-h" or "-?" or "/?" or "/help":
                    command = CliCommand.Help;
                    break;
                case "--config":
                    if (i + 1 < args.Length)
                    {
                        configPath = args[++i];
                    }

                    break;
                default:
                    // Windows hands us the URL (or file path) as a bare argument.
                    url ??= arg;
                    if (command is CliCommand.None)
                    {
                        command = CliCommand.OpenUrl;
                    }

                    break;
            }
        }

        if (command is CliCommand.None)
        {
            command = CliCommand.Help;
        }

        return new CommandLineOptions(command, url, configPath, noSettings, removeConfig);
    }
}
