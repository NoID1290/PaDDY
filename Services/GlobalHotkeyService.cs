using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace PaDDY.Services
{
    /// <summary>
    /// Registers and manages a global hotkey using Win32 RegisterHotKey.
    /// WinUI 3 version: gets HWND via WindowNative and subclasses via SetWindowSubclass.
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

        // Subclassing via comctl32
        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        public event Action? HotkeyPressed;

        private IntPtr _hwnd;
        private SUBCLASSPROC? _subclassProc;
        private bool _registered;
        private bool _disposed;

        /// <summary>
        /// Attaches to the given WinUI 3 Window and registers the hotkey.
        /// Call after the window is created (so the HWND exists).
        /// </summary>
        public void Register(Window window, uint modifiers, uint virtualKey)
        {
            if (_registered) Unregister();

            _hwnd = WindowNative.GetWindowHandle(window);
            if (_hwnd == IntPtr.Zero) return;

            // Install a window subclass to intercept WM_HOTKEY messages.
            _subclassProc = SubclassWndProc;
            SetWindowSubclass(_hwnd, _subclassProc, (IntPtr)1, IntPtr.Zero);

            _registered = RegisterHotKey(_hwnd, HotkeyId, modifiers, virtualKey);
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
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HotkeyId);
                if (_subclassProc != null)
                    RemoveWindowSubclass(_hwnd, _subclassProc, (IntPtr)1);
            }
            _subclassProc = null;
            _hwnd = IntPtr.Zero;
            _registered = false;
        }

        private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
                return IntPtr.Zero;
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister();
        }
    }
}
