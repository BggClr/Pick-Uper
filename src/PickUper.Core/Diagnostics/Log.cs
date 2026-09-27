using System.Globalization;

namespace PickUper.Core.Diagnostics;

/// <summary>
/// Tiny append-only log. Errors are always recorded, everything else only when the config
/// asks for it — this process has no console to complain to when Windows launches it.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static bool Verbose { get; set; }

    public static string? FilePath { get; set; }

    public static string DefaultPath(string localAppData) =>
        Path.Combine(localAppData, "PickUper", "pick-uper.log");

    public static void Info(string message)
    {
        if (Verbose)
        {
            Write("INFO ", message);
        }
    }

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var path = FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Rotate(path);

                var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                File.AppendAllText(path, $"{stamp} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break opening a link.
        }
    }

    private static void Rotate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var backup = path + ".1";
        File.Delete(backup);
        File.Move(path, backup);
    }
}
