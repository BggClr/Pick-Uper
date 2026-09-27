using System.Runtime.InteropServices;

namespace PickUper.Windows;

/// <summary>
/// pick-uper is a WinExe so that clicking a link never flashes a console window. That also
/// means CLI output goes nowhere — unless we attach to the console we were started from.
/// </summary>
public static partial class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    private static bool _attached;

    public static void AttachToParent()
    {
        if (_attached || !AttachConsole(AttachParentProcess))
        {
            return;
        }

        _attached = true;

        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(stdout);

        var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetError(stderr);

        Console.WriteLine();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);
}
