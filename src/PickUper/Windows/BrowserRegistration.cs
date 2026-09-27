using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PickUper.Windows;

/// <summary>
/// Registers pick-uper as a browser Windows is willing to offer as the default handler for
/// http/https and .htm/.html. Everything lives under HKCU, so no elevation is needed.
///
/// Windows 10+ will not let an application make itself the default; the user has to confirm
/// the choice in Settings. <see cref="OpenDefaultAppsSettings"/> takes them there.
/// </summary>
public static partial class BrowserRegistration
{
    public const string ApplicationKey = "PickUper";
    public const string ApplicationName = "Pick-Uper";
    public const string ProgId = "PickUperURL";

    private const string Description =
        "Sends every link to the browser chosen by your ~/.pick-uper.json rules.";

    private const string ClientsPath = $@"Software\Clients\StartMenuInternet\{ApplicationKey}";
    private const string CapabilitiesPath = $@"{ClientsPath}\Capabilities";
    private const string RegisteredApplicationsPath = @"Software\RegisteredApplications";
    private const string ClassesPath = $@"Software\Classes\{ProgId}";

    private static readonly string[] UrlProtocols = ["http", "https"];
    private static readonly string[] FileExtensions = [".htm", ".html", ".shtml", ".xht", ".xhtml"];

    public static void Register(string executablePath)
    {
        var quoted = $"\"{executablePath}\"";
        var icon = $"{executablePath},0";

        using (var client = Registry.CurrentUser.CreateSubKey(ClientsPath))
        {
            client.SetValue(null, ApplicationName);

            using (var defaultIcon = client.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(null, icon);
            }

            using (var command = client.CreateSubKey(@"shell\open\command"))
            {
                command.SetValue(null, quoted);
            }

            using var capabilities = client.CreateSubKey("Capabilities");
            capabilities.SetValue("ApplicationName", ApplicationName);
            capabilities.SetValue("ApplicationDescription", Description);
            capabilities.SetValue("ApplicationIcon", icon);

            using (var startMenu = capabilities.CreateSubKey("StartMenu"))
            {
                startMenu.SetValue("StartMenuInternet", ApplicationKey);
            }

            using (var urlAssociations = capabilities.CreateSubKey("URLAssociations"))
            {
                foreach (var protocol in UrlProtocols)
                {
                    urlAssociations.SetValue(protocol, ProgId);
                }
            }

            using var fileAssociations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in FileExtensions)
            {
                fileAssociations.SetValue(extension, ProgId);
            }
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsPath))
        {
            registered.SetValue(ApplicationKey, CapabilitiesPath);
        }

        using (var progId = Registry.CurrentUser.CreateSubKey(ClassesPath))
        {
            progId.SetValue(null, ApplicationName);
            progId.SetValue("URL Protocol", string.Empty);
            progId.SetValue("FriendlyTypeName", ApplicationName);

            using (var defaultIcon = progId.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(null, icon);
            }

            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"{quoted} \"%1\"");
        }

        // Also advertise the exe itself, so "chrome-style" lookups and "Open with" find it.
        using (var appPaths = Registry.CurrentUser.CreateSubKey(
                   $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{Path.GetFileName(executablePath)}"))
        {
            appPaths.SetValue(null, executablePath);
            appPaths.SetValue("Path", Path.GetDirectoryName(executablePath) ?? string.Empty);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "pick-uper.exe");

        Registry.CurrentUser.DeleteSubKeyTree(ClientsPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClassesPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}", throwOnMissingSubKey: false);

        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsPath, writable: true))
        {
            if (registered?.GetValue(ApplicationKey) is not null)
            {
                registered.DeleteValue(ApplicationKey, throwOnMissingValue: false);
            }
        }

        NotifyShell();
    }

    /// <summary>Path recorded in the registry, or null when pick-uper is not registered.</summary>
    public static string? RegisteredExecutable()
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{ClientsPath}\shell\open\command");
        return command?.GetValue(null) as string;
    }

    /// <summary>ProgId Windows currently uses for a protocol, e.g. "https".</summary>
    public static string? CurrentUserChoice(string protocol)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{protocol}\UserChoice");
        return key?.GetValue("ProgId") as string;
    }

    public static bool IsDefaultBrowser() =>
        UrlProtocols.All(p => string.Equals(CurrentUserChoice(p), ProgId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Windows 10+ only exposes the "make default" flow through Settings.</summary>
    public static void OpenDefaultAppsSettings()
    {
        var uri = $"ms-settings:defaultapps?registeredAppUser={ApplicationName}";
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    private static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
