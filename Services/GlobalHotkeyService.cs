using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;

namespace PaDDY.Services
{
    /// <summary>
    /// Registers and manages a global hotkey using Win32 RegisterHotKey.
    /// Hooks into the WPF window's message pump; fires HotkeyPressed when triggered.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class GlobalHotkeyService : IDisposable
    {
        // Win32 constants
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 9001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action? HotkeyPressed;

        private HwndSource? _hwndSource;
        private bool _registered;
        private bool _disposed;

        /// <summary>
        /// Attaches to the given WPF Window and registers the hotkey.
        /// Call after the window is loaded (so the HWND exists).
        /// </summary>
        public void Register(Window window, uint modifiers, uint virtualKey)
        {
            if (_registered) Unregister();

            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);

            _registered = RegisterHotKey(hwnd, HotkeyId, modifiers, virtualKey);
        }

        /// <summary>Re-registers with new modifier/key combination. Silently no-ops if not yet registered.</summary>
        public void Reregister(Window window, uint modifiers, uint virtualKey)
        {
            Unregister();
            Register(window, modifiers, virtualKey);
        }

        public void Unregister()
        {
            if (!_registered) return;
            IntPtr hwnd = _hwndSource?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
                UnregisterHotKey(hwnd, HotkeyId);
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            _registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister();
        }
    }
}
