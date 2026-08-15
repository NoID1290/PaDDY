using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using PaDDY.Models;

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
        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        [DllImport("gdi32.dll")]
        private static extern bool RemoveFontMemResourceEx(IntPtr handle);

        private readonly NotifyIcon _icon;
        private bool _disposed;

        private PrivateFontCollection? _fontCollection;
        private IntPtr _fontBuffer = IntPtr.Zero;
        private IntPtr _gdiFontHandle = IntPtr.Zero;
        private Font? _menuFont;

        /// <summary>Raised when the user asks to show/restore the main window.</summary>
        public event Action? ShowRequested;

        /// <summary>Raised when the user picks "Settings" from the tray menu.</summary>
        public event Action? SettingsRequested;

        /// <summary>Raised when the user asks to toggle audio monitoring from the tray menu.</summary>
        public event Action? ToggleMonitoringRequested;

        /// <summary>Raised when the user asks to toggle pad monitoring from the tray menu.</summary>
        public event Action? TogglePadMonitoringRequested;

        /// <summary>Callback to query if monitoring is currently active.</summary>
        public Func<bool>? IsMonitoringActiveFunc { get; set; }

        /// <summary>Callback to query if pad monitoring is currently enabled.</summary>
        public Func<bool>? IsPadMonitoringActiveFunc { get; set; }

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

            var showItem = new ToolStripMenuItem(LocalizationManager.Instance["TrayOpen"], null, (_, _) => ShowRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };
            var settingsItem = new ToolStripMenuItem(LocalizationManager.Instance["TraySettings"], null, (_, _) => SettingsRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };

            var toggleMonitoringItem = new ToolStripMenuItem(LocalizationManager.Instance["TrayStartMonitoring"], null, (_, _) => ToggleMonitoringRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };
            var togglePadMonitorItem = new ToolStripMenuItem(LocalizationManager.Instance["TrayPadMonitorOn"], null, (_, _) => TogglePadMonitoringRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };

            var exitItem = new ToolStripMenuItem(LocalizationManager.Instance["TrayExit"], null, (_, _) => ExitRequested?.Invoke()) { Padding = new Padding(12, 6, 12, 6) };

            void RefreshMenuItems()
            {
                showItem.Text = LocalizationManager.Instance["TrayOpen"];
                settingsItem.Text = LocalizationManager.Instance["TraySettings"];
                exitItem.Text = LocalizationManager.Instance["TrayExit"];

                bool isMonitoring = IsMonitoringActiveFunc?.Invoke() ?? false;
                toggleMonitoringItem.Text = isMonitoring
                    ? (LocalizationManager.Instance["TrayStopMonitoring"] ?? "Stop Monitoring")
                    : (LocalizationManager.Instance["TrayStartMonitoring"] ?? "Start Monitoring");

                bool isPadMonitoring = IsPadMonitoringActiveFunc?.Invoke() ?? false;
                togglePadMonitorItem.Text = isPadMonitoring
                    ? (LocalizationManager.Instance["TrayPadMonitorOn"] ?? "Pad Monitor: On")
                    : (LocalizationManager.Instance["TrayPadMonitorOff"] ?? "Pad Monitor: Off");
            }

            LocalizationManager.Instance.PropertyChanged += (_, _) => RefreshMenuItems();
            menu.Opening += (_, _) => RefreshMenuItems();

            menu.Items.Add(showItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(toggleMonitoringItem);
            menu.Items.Add(togglePadMonitorItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();

            UpdateMenuFont();
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

        /// <summary>Loads and applies the integrated font selected in the AppSettings to the context menu.</summary>
        public void UpdateMenuFont()
        {
            try
            {
                var settings = AppSettings.Load();
                var variantKey = settings.AppFontVariant;

                var entry = App.FontVariants.FirstOrDefault(v => string.Equals(v.Key, variantKey, StringComparison.OrdinalIgnoreCase));
                if (entry == default)
                {
                    if (string.Equals(variantKey, "display", StringComparison.OrdinalIgnoreCase) || string.Equals(variantKey, "regular", StringComparison.OrdinalIgnoreCase))
                        entry = App.FontVariants.FirstOrDefault(v => v.Key == "normal");
                    else if (string.Equals(variantKey, "generalsans", StringComparison.OrdinalIgnoreCase) || string.Equals(variantKey, "general_sans", StringComparison.OrdinalIgnoreCase))
                        entry = App.FontVariants.FirstOrDefault(v => v.Key == "general-sans");
                    else
                        entry = App.FontVariants.FirstOrDefault(v => v.Key == "condensed");

                    if (entry == default) entry = App.FontVariants.First();
                }

                var uri = new Uri($"pack://application:,,,/Themes/Fonts/{entry.FileName}");
                var streamResource = System.Windows.Application.GetResourceStream(uri);
                if (streamResource != null)
                {
                    using (var stream = streamResource.Stream)
                    {
                        byte[] fontData = new byte[stream.Length];
                        stream.ReadExactly(fontData, 0, fontData.Length);

                        IntPtr oldBuffer = _fontBuffer;
                        PrivateFontCollection? oldCollection = _fontCollection;
                        Font? oldFont = _menuFont;
                        IntPtr oldGdiHandle = _gdiFontHandle;

                        _fontBuffer = Marshal.AllocCoTaskMem(fontData.Length);
                        Marshal.Copy(fontData, 0, _fontBuffer, fontData.Length);

                        uint pcFonts = 0;
                        _gdiFontHandle = AddFontMemResourceEx(_fontBuffer, (uint)fontData.Length, IntPtr.Zero, ref pcFonts);

                        _fontCollection = new PrivateFontCollection();
                        _fontCollection.AddMemoryFont(_fontBuffer, fontData.Length);

                        if (_fontCollection.Families.Length > 0)
                        {
                            var family = _fontCollection.Families[0];
                            // Match the general aesthetic font size of the application context menus
                            _menuFont = new Font(family, 10f, FontStyle.Regular);

                            if (_icon.ContextMenuStrip != null)
                            {
                                _icon.ContextMenuStrip.Font = _menuFont;
                                foreach (ToolStripItem item in _icon.ContextMenuStrip.Items)
                                {
                                    item.Font = _menuFont;
                                }
                            }
                        }

                        if (oldFont != null)
                        {
                            oldFont.Dispose();
                        }
                        if (oldCollection != null)
                        {
                            oldCollection.Dispose();
                        }
                        if (oldGdiHandle != IntPtr.Zero)
                        {
                            RemoveFontMemResourceEx(oldGdiHandle);
                        }
                        if (oldBuffer != IntPtr.Zero)
                        {
                            Marshal.FreeCoTaskMem(oldBuffer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update tray menu font: {ex.Message}");
            }
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

            if (_menuFont != null)
            {
                _menuFont.Dispose();
                _menuFont = null;
            }
            if (_fontCollection != null)
            {
                _fontCollection.Dispose();
                _fontCollection = null;
            }
            if (_gdiFontHandle != IntPtr.Zero)
            {
                RemoveFontMemResourceEx(_gdiFontHandle);
                _gdiFontHandle = IntPtr.Zero;
            }
            if (_fontBuffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_fontBuffer);
                _fontBuffer = IntPtr.Zero;
            }

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