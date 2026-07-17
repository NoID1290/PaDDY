using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PaDDY.Helpers;

namespace PaDDY.Views
{
    public partial class SplashWindow : Window
    {
        // ── P/Invoke Definitions for Aero Glass / Acrylic Backdrop ─────────
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        public SplashWindow()
        {
            InitializeComponent();
            
            // Retrieve palette and apply theme colors to the loading overlay
            try
            {
                var settings = AppSettings.Load();
                var palette = ThemeManager.GetPalette(settings.Theme);
                if (palette != null)
                {
                    var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["AccentGreenBrush"]);
                    var secondary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["SubtleTextBrush"]);
                    var text = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["PrimaryTextBrush"]);
                    SplashLoadingOverlay.ApplyThemeColors(accent, secondary, text);
                }
            }
            catch
            {
                // Fallback gracefully on any theme loading failure
            }

            SplashLoadingOverlay.Show("Loading data...");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyGlassEffect();
        }

        private void ApplyGlassEffect()
        {
            try
            {
                var windowHelper = new WindowInteropHelper(this);
                IntPtr hwnd = windowHelper.Handle;

                var settings = AppSettings.Load();
                var palette = ThemeManager.GetPalette(settings.Theme);

                // Default tint color (semi-transparent dark)
                int tintColor = 0x600D0D14; // Default AARRGGBB color format for dark theme: #600D0D14 (ABGR: 0x60140D0D)
                if (palette != null && palette.TryGetValue("WindowBgBrush", out var winBgHex))
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(winBgHex);
                    byte alpha = 0x60; // 96/255 opacity for nice glass effect
                    if (settings.Theme == "light" || settings.Theme == "sepia")
                    {
                        alpha = 0x90; // slightly higher opacity for light themes so text remains readable
                    }
                    // Format for DWM composition is AABBGGRR (ABGR in memory)
                    tintColor = (alpha << 24) | (color.B << 16) | (color.G << 8) | color.R;
                }

                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    GradientColor = tintColor,
                    AccentFlags = 2 // enable/draw backdrop
                };

                int accentStructSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(hwnd, ref data);

                Marshal.FreeHGlobal(accentPtr);
            }
            catch
            {
                // Gracefully ignore on non-Windows platforms or environments where DWM composition fails
            }
        }

        public void UpdateMessage(string message)
        {
            Dispatcher.Invoke(() => SplashLoadingOverlay.Show(message));
        }

        public void UpdateProgress(double fraction)
        {
            Dispatcher.Invoke(() => SplashLoadingOverlay.ShowProgress(fraction));
        }

        public void HideProgress()
        {
            Dispatcher.Invoke(() => SplashLoadingOverlay.HideProgress());
        }
    }
}
