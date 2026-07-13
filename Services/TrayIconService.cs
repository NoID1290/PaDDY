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

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Renderer = new PaddyToolStripRenderer()
            };

            var showItem = new ToolStripMenuItem("Show PaDDY", null, (_, _) => ShowRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };
            var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };
            var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };

            menu.Items.Add(showItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

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

    [SupportedOSPlatform("windows")]
    internal sealed class PaddyToolStripRenderer : ToolStripProfessionalRenderer
    {
        public PaddyToolStripRenderer() : base(new PaddyColorTable())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                e.TextColor = Color.FromArgb(240, 240, 248); // Soft white when selected
            }
            else if (!e.Item.Enabled)
            {
                e.TextColor = Color.FromArgb(112, 112, 160); // SubtleTextBrush #7070A0
            }
            else
            {
                e.TextColor = Color.FromArgb(232, 232, 244); // PrimaryTextBrush #E8E8F4
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                var g = e.Graphics;
                var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                using (var brush = new SolidBrush(Color.FromArgb(62, 74, 90))) // MenuHighlightBrush #3E4A5A
                {
                    g.FillRectangle(brush, rect);
                }
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            using (var brush = new SolidBrush(Color.FromArgb(16, 16, 30))) // MenuBgBrush #10101E
            {
                g.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // No-op to avoid default gutter rendering
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(6, e.Item.ContentRectangle.Height / 2, e.Item.Width - 12, 1);
            using (var brush = new SolidBrush(Color.FromArgb(40, 40, 53))) // #282835 (DividerBrush blended on MenuBg)
            {
                g.FillRectangle(brush, rect);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var pen = new Pen(Color.FromArgb(68, 68, 79))) // #44444F (BorderBrush blended on MenuBg)
            {
                g.DrawRectangle(pen, rect);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    internal sealed class PaddyColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(16, 16, 30); // MenuBgBrush #10101E
        public override Color MenuBorder => Color.FromArgb(68, 68, 79); // #44444F
        public override Color MenuItemSelected => Color.FromArgb(26, 38, 54); // Less bright selection background #1A2636
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(26, 38, 54);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(26, 38, 54);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color ImageMarginGradientBegin => Color.FromArgb(16, 16, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(16, 16, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(16, 16, 30);
    }
}