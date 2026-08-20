using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Application = System.Windows.Application;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using MouseWheelEventHandler = System.Windows.Input.MouseWheelEventHandler;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Manages application-wide UI zooming / scaling.
    /// Supports Ctrl + / -, Ctrl + MouseWheel, Ctrl + 0, and settings configuration.
    /// </summary>
    public static class ZoomManager
    {
        public const double MinScale = 0.50;
        public const double MaxScale = 2.00;
        public const double DefaultScale = 1.00;
        public const double Step = 0.05; // 5% step

        private static double _currentScale = DefaultScale;
        private static bool _isInitialized;

        /// <summary>Stores the unscaled base caption height for each window that uses WindowChrome.</summary>
        private static readonly ConditionalWeakTable<Window, StrongBox<double>> _originalCaptionHeights = new();

        /// <summary>Current active UI scaling factor (e.g. 1.0 = 100%, 1.25 = 125%).</summary>
        public static double CurrentScale => _currentScale;

        /// <summary>Fired whenever the UI scale changes.</summary>
        public static event Action<double>? ScaleChanged;

        /// <summary>
        /// Initializes the zoom manager with the saved scale from settings and registers global input hooks.
        /// </summary>
        public static void Initialize(double initialScale)
        {
            if (initialScale < MinScale || initialScale > MaxScale || double.IsNaN(initialScale))
            {
                initialScale = DefaultScale;
            }

            _currentScale = Math.Round(initialScale, 2);

            if (!_isInitialized)
            {
                _isInitialized = true;
                InitializeGlobalHooks();
            }
        }

        private static void InitializeGlobalHooks()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded)
            );

            EventManager.RegisterClassHandler(
                typeof(Window),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnWindowPreviewMouseWheel)
            );

            EventManager.RegisterClassHandler(
                typeof(Window),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnWindowPreviewKeyDown)
            );
        }

        /// <summary>
        /// Sets the UI zoom scale factor.
        /// </summary>
        public static void SetScale(double scale, bool saveSettings = true)
        {
            scale = Math.Clamp(Math.Round(scale, 2), MinScale, MaxScale);

            if (Math.Abs(_currentScale - scale) < 0.001)
                return;

            _currentScale = scale;

            // Apply zoom across all open windows
            ApplyZoomToAllWindows();

            ScaleChanged?.Invoke(_currentScale);

            if (saveSettings)
            {
                try
                {
                    var settings = AppSettings.Load();
                    settings.UiScale = _currentScale;
                    settings.Save();
                }
                catch { }
            }
        }

        /// <summary>
        /// Zooms in by the standard step (5%).
        /// </summary>
        public static void ZoomIn(bool saveSettings = true)
        {
            double next = Math.Round((_currentScale + Step) * 20.0) / 20.0;
            SetScale(Math.Min(MaxScale, next), saveSettings);
        }

        /// <summary>
        /// Zooms out by the standard step (5%).
        /// </summary>
        public static void ZoomOut(bool saveSettings = true)
        {
            double next = Math.Round((_currentScale - Step) * 20.0) / 20.0;
            SetScale(Math.Max(MinScale, next), saveSettings);
        }

        /// <summary>
        /// Resets the UI zoom back to 100% (1.0).
        /// </summary>
        public static void ResetZoom(bool saveSettings = true)
        {
            SetScale(DefaultScale, saveSettings);
        }

        /// <summary>
        /// Applies the current zoom scale to a specific window.
        /// </summary>
        public static void ApplyZoomToWindow(Window? window)
        {
            if (window == null) return;

            // Ensure window-level rendering options remain crisp and avoid subpixel blur
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            RenderOptions.SetClearTypeHint(window, ClearTypeHint.Enabled);
            RenderOptions.SetBitmapScalingMode(window, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(window, EdgeMode.Unspecified);
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(window, TextHintingMode.Auto);

            if (window.Content is FrameworkElement fe)
            {
                fe.UseLayoutRounding = true;
                fe.SnapsToDevicePixels = true;

                // When scaling is applied, WPF defaults to disabling ClearType on transformed content
                // and bitmap scaling defaults to LowQuality. Explicitly forcing ClearTypeHint, Ideal
                // text formatting, and HighQuality bitmap scaling ensures sharp text and crisp icons.
                RenderOptions.SetClearTypeHint(fe, ClearTypeHint.Enabled);
                RenderOptions.SetBitmapScalingMode(fe, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(fe, EdgeMode.Unspecified);
                TextOptions.SetTextFormattingMode(fe, TextFormattingMode.Ideal);
                TextOptions.SetTextRenderingMode(fe, TextRenderingMode.ClearType);
                TextOptions.SetTextHintingMode(fe, TextHintingMode.Auto);

                if (Math.Abs(_currentScale - DefaultScale) < 0.001)
                {
                    fe.LayoutTransform = Transform.Identity;
                }
                else if (fe.LayoutTransform is ScaleTransform st)
                {
                    st.ScaleX = _currentScale;
                    st.ScaleY = _currentScale;
                }
                else
                {
                    fe.LayoutTransform = new ScaleTransform(_currentScale, _currentScale);
                }
            }

            // Dynamically scale WindowChrome caption height for custom titlebars
            try
            {
                var chrome = WindowChrome.GetWindowChrome(window);
                if (chrome != null)
                {
                    if (!_originalCaptionHeights.TryGetValue(window, out var box))
                    {
                        box = new StrongBox<double>(chrome.CaptionHeight);
                        _originalCaptionHeights.Add(window, box);
                    }

                    chrome.CaptionHeight = box.Value * _currentScale;
                }
            }
            catch { }
        }

        private static void ApplyZoomToAllWindows()
        {
            if (Application.Current == null) return;

            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.InvokeAsync(ApplyZoomToAllWindows);
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                ApplyZoomToWindow(window);
            }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                ApplyZoomToWindow(window);
            }
        }

        private static void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Zoom In: Ctrl + Plus, Ctrl + '=', or Numpad Add
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    ZoomIn();
                    e.Handled = true;
                    return;
                }

                // Zoom Out: Ctrl + Minus, or Numpad Subtract
                if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    ZoomOut();
                    e.Handled = true;
                    return;
                }

                // Reset Zoom: Ctrl + 0 or Numpad 0
                if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    ResetZoom();
                    e.Handled = true;
                    return;
                }
            }
        }

        private static void OnWindowPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    ZoomIn();
                    e.Handled = true;
                }
                else if (e.Delta < 0)
                {
                    ZoomOut();
                    e.Handled = true;
                }
            }
        }
    }
}
