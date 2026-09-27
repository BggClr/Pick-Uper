using System.Diagnostics;
using PickUper.Core.Diagnostics;

namespace PickUper.Windows;

public sealed record LaunchResult(bool Success, string Description, string? Error = null);

public static class BrowserLauncher
{
    public static LaunchResult Launch(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            // CreateProcess rather than ShellExecute: we already have a concrete .exe, and
            // ArgumentList escaping is only honoured on this path.
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var description = $"{executable} {string.Join(' ', arguments.Select(Quote))}";

        try
        {
            using var process = Process.Start(startInfo);
            Log.Info($"launched: {description}");
            return new LaunchResult(true, description);
        }
        catch (Exception ex)
        {
            Log.Error($"failed to launch {description}", ex);
            return new LaunchResult(false, description, ex.Message);
        }
    }

    /// <summary>
    /// Opens a URL with whatever Windows considers the handler — used only if pick-uper is
    /// somehow not the default, otherwise it would call itself.
    /// </summary>
    public static LaunchResult OpenWithShell(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return new LaunchResult(true, $"shell open {url}");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, $"shell open {url}", ex.Message);
        }
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
