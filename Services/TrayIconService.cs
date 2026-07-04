using System;
using System.Runtime.Versioning;

namespace PaDDY.Services
{
    /// <summary>
    /// System-tray icon service — STUBBED for WinUI 3 migration.
    /// 
    /// The WPF version used Microsoft.UI.Xaml.Forms.NotifyIcon which is not compatible
    /// with WinUI 3 (UseWindowsForms is not available). This will be re-implemented
    /// using H.NotifyIcon.WinUI or raw Shell_NotifyIcon P/Invoke after the migration.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class TrayIconService : IDisposable
    {
        private bool _disposed;

        /// <summary>Raised when the user asks to show/restore the main window.</summary>
        public event Action? ShowRequested;

        /// <summary>Raised when the user picks "Settings" from the tray menu.</summary>
        public event Action? SettingsRequested;

        /// <summary>Raised when the user picks "Exit" from the tray menu.</summary>
        public event Action? ExitRequested;

        public TrayIconService(string tooltip = "PaDDY")
        {
            // TODO: Re-implement tray icon for WinUI 3 (H.NotifyIcon.WinUI or Shell_NotifyIcon P/Invoke)
        }

        /// <summary>Displays a balloon notification from the tray icon. Currently a no-op.</summary>
        public void ShowBalloon(string title, string message, int timeoutMs = 2000)
        {
            // No-op: tray icon is deactivated for WinUI 3 migration.
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}