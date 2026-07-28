using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PaDDY.Helpers;

public static class ConsoleHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x00000000;

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private static bool _hasConsole;
    private static TextWriterTraceListener? _traceListener;
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;

    public static void ShowConsole()
    {
        if (_hasConsole) return;

        if (AllocConsole())
        {
            _hasConsole = true;

            // Save original streams
            _originalOut = Console.Out;
            _originalError = Console.Error;

            // Open CONOUT$ directly to bypass .NET stream caching
            IntPtr stdOutHandle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (stdOutHandle != IntPtr.Zero && stdOutHandle != (IntPtr)(-1))
            {
                var safeHandle = new SafeFileHandle(stdOutHandle, true);
                var fileStream = new FileStream(safeHandle, FileAccess.Write);
                var stdOutWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(stdOutWriter);
            }

            IntPtr stdErrHandle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (stdErrHandle != IntPtr.Zero && stdErrHandle != (IntPtr)(-1))
            {
                var safeHandle = new SafeFileHandle(stdErrHandle, true);
                var fileStream = new FileStream(safeHandle, FileAccess.Write);
                var stdErrWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetError(stdErrWriter);
            }

            Console.Title = "PaDDY Debug Console";

            // Add trace listener so Debug.WriteLine also outputs to this console
            _traceListener = new TextWriterTraceListener(Console.Out);
            Trace.Listeners.Add(_traceListener);

            // Disable the close button on the console window to prevent it from terminating the app
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
            {
                IntPtr menu = GetSystemMenu(hwnd, false);
                if (menu != IntPtr.Zero)
                {
                    DeleteMenu(menu, SC_CLOSE, MF_BYCOMMAND);
                }
            }

            Console.WriteLine("=== PaDDY Debug Console Initialized ===");
            Console.WriteLine("Press Ctrl+Alt+D in the main window to toggle debug mode and hide this console.");
            Console.WriteLine();
        }
    }

    public static void HideConsole()
    {
        if (!_hasConsole) return;

        if (_traceListener != null)
        {
            Trace.Listeners.Remove(_traceListener);
            _traceListener.Dispose();
            _traceListener = null;
        }

        // Restore original streams
        if (_originalOut != null) Console.SetOut(_originalOut);
        if (_originalError != null) Console.SetError(_originalError);

        FreeConsole();
        _hasConsole = false;
    }
}
