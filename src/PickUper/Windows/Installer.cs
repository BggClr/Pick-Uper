using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PickUper.Windows;

/// <summary>
/// Puts pick-uper into %LOCALAPPDATA%\Programs\pick-uper and creates the Start Menu
/// shortcut that makes it show up in Settings > Default apps, and tears both back down.
/// This is everything scripts/install.ps1 and scripts/uninstall.ps1 used to do by hand —
/// now it's just `pick-uper --install` / `pick-uper --uninstall`.
/// </summary>
public static class Installer
{
    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "pick-uper");

    public static string TargetExecutablePath { get; } = Path.Combine(InstallDirectory, "pick-uper.exe");

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickUper");

    private static string StartMenuShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "Pick-Uper.lnk");

    /// <summary>True when the running process is already the installed copy.</summary>
    public static bool IsInstalled => PathsEqual(Environment.ProcessPath, TargetExecutablePath);

    /// <summary>
    /// Copies <paramref name="sourceExe"/> into <see cref="InstallDirectory"/>, replacing
    /// any previously installed copy. The registry points at this exact path, so it must
    /// stay stable across updates.
    /// </summary>
    public static void CopyInto(string sourceExe)
    {
        Directory.CreateDirectory(InstallDirectory);

        if (PathsEqual(sourceExe, TargetExecutablePath))
        {
            return;
        }

        KillOtherRunningInstances();
        File.Copy(sourceExe, TargetExecutablePath, overwrite: true);
    }

    /// <summary>Stops every other running pick-uper process, but never this one.</summary>
    public static void KillOtherRunningInstances()
    {
        foreach (var process in Process.GetProcessesByName("pick-uper"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                try
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Already gone by the time we got here.
                }
            }
        }
    }

    public static void RemoveStartMenuShortcut()
    {
        try
        {
            File.Delete(StartMenuShortcutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void RemoveLogDirectory()
    {
        try
        {
            Directory.Delete(LogDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes <see cref="InstallDirectory"/>, including the running exe itself. Windows
    /// won't let this process delete its own open file, so the actual removal happens in a
    /// tiny detached script that waits for this process to exit first.
    /// </summary>
    public static void ScheduleInstallDirectoryRemoval()
    {
        if (!Directory.Exists(InstallDirectory))
        {
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"pick-uper-uninstall-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(scriptPath, $"""
            @echo off
            ping -n 2 127.0.0.1 >nul
            rmdir /s /q "{InstallDirectory}"
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo(scriptPath)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    /// <summary>
    /// Writes the Start Menu shortcut via WScript.Shell (the same COM automation object
    /// scripts/install.ps1 used to drive from PowerShell).
    /// </summary>
    // COM late-binding (Type.GetTypeFromProgID + InvokeMember) resolves members through
    // WScript.Shell's IDispatch at runtime, not via .NET reflection metadata, so trimming
    // can't remove anything it needs — the analyzer just can't see that.
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "COM late-binding, see above.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "COM late-binding, see above.")]
    public static void CreateStartMenuShortcut(string targetExe)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable");

        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("could not create a WScript.Shell instance");

        try
        {
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                [StartMenuShortcutPath]) ?? throw new InvalidOperationException("CreateShortcut returned null");

            try
            {
                var shortcutType = shortcut.GetType();
                SetProperty(shortcutType, shortcut, "TargetPath", targetExe);
                SetProperty(shortcutType, shortcut, "Arguments", "--status");
                SetProperty(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(targetExe) ?? InstallDirectory);
                SetProperty(shortcutType, shortcut, "Description", "Pick-Uper — routes links to the right browser");
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "COM late-binding, see CreateStartMenuShortcut.")]
    private static void SetProperty(Type type, object target, string property, string value) =>
        type.InvokeMember(property, BindingFlags.SetProperty, null, target, [value]);

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
