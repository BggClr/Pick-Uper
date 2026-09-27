using System.Runtime.InteropServices;

namespace PickUper.Windows;

/// <summary>
/// pick-uper is a WinExe so that clicking a link never flashes a console window. That also
/// means CLI output goes nowhere — unless we attach to whatever console started us, or, if
/// there isn't one (e.g. double-clicked from Explorer), allocate a brand new one.
/// </summary>
public static partial class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    private static bool _attached;
    private static bool _allocated;

    /// <summary>True once we own a console freshly created for this process — as opposed
    /// to one we merely attached to — which is when it makes sense to pause before it would
    /// otherwise vanish on exit.</summary>
    public static bool OwnsConsole => _allocated;

    public static void EnsureConsole()
    {
        if (_attached || _allocated)
        {
            return;
        }

        if (AttachConsole(AttachParentProcess))
        {
            _attached = true;
        }
        else if (AllocConsole())
        {
            _allocated = true;
        }
        else
        {
            return;
        }

        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(stdout);

        var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetError(stderr);

        Console.SetIn(new StreamReader(Console.OpenStandardInput()));

        Console.WriteLine();
    }

    /// <summary>
    /// Waits for a keypress, but only when we allocated our own console — one we merely
    /// attached to belongs to a shell that's still there after we exit, so it doesn't need it.
    /// </summary>
    public static void PauseIfOwned()
    {
        if (!_allocated)
        {
            return;
        }

        Console.WriteLine();
        Console.Write("Press any key to continue . . . ");
        Console.ReadKey(intercept: true);
        Console.WriteLine();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
