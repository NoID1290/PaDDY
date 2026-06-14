using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PaDDY.Services
{
    /// <summary>
    /// Wraps a Windows Forms <see cref="NotifyIcon"/> to provide system-tray
    /// presence for PaDDY: show/restore, open settings, and exit. The owning
    /// window supplies the callbacks; this service holds no WPF references.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private bool _disposed;

        /// <summary>Raised when the user asks to show/restore the main window.</summary>
        public event Action? ShowRequested;

        /// <summary>Raised when the user picks "Settings" from the tray menu.</summary>
        public event Action? SettingsRequested;

        /// <summary>Raised when the user picks "Exit" from the tray menu.</summary>
        public event Action? ExitRequested;

        public TrayIconService(string tooltip = "PaDDY")
        {
            _icon = new NotifyIcon
            {
                Text = tooltip,
                Icon = LoadAppIcon(),
                Visible = true  // Set once here; never toggled afterward
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show PaDDY", null, (_, _) => ShowRequested?.Invoke());
            menu.Items.Add("Settings…", null, (_, _) => SettingsRequested?.Invoke());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
            _icon.ContextMenuStrip = menu;

            _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
        }

        // Removed the Visible property — callers can no longer hide the icon.
        // If you need to expose visibility for other reasons, make the setter
        // a no-op or throw, so the icon stays pinned to the tray at all times.

        /// <summary>Displays a balloon notification from the tray icon.</summary>
        public void ShowBalloon(string title, string message, int timeoutMs = 2000)
        {
            if (_disposed) return;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(timeoutMs);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrEmpty(exe))
                {
                    Icon? extracted = Icon.ExtractAssociatedIcon(exe);
                    if (extracted != null) return extracted;
                }
            }
            catch { /* fall through */ }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Hide only at true shutdown so Windows cleans up the tray slot.
            // This runs when the app is actually exiting, not when the window closes.
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}