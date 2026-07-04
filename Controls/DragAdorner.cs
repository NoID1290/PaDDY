using System;
using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace PaDDY.Controls
{
    /// <summary>
    /// A lightweight drag ghost that renders a translucent placeholder of a dragged pad
    /// and follows the pointer, giving the drag a tactile "picked up" feel.
    ///
    /// WinUI 3 version: uses a <see cref="Popup"/> instead of WPF's Adorner system
    /// (which does not exist in WinUI 3).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class DragAdorner : IDisposable
    {
        private readonly Popup _popup;
        private readonly Border _ghost;
        private readonly Point _grabOffset;

        /// <param name="source">The element being dragged (used for sizing).</param>
        /// <param name="grabOffset">Offset of the initial pointer-down point within <paramref name="source"/>.</param>
        public DragAdorner(FrameworkElement source, Point grabOffset)
        {
            _grabOffset = grabOffset;

            double w = source.ActualWidth  > 0 ? source.ActualWidth  : 145;
            double h = source.ActualHeight > 0 ? source.ActualHeight : 90;

            _ghost = new Border
            {
                Width            = w,
                Height           = h,
                CornerRadius     = new CornerRadius(10),
                IsHitTestVisible = false,
                Opacity          = 0.80,
                Background       = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x1A, 0x1A, 0x2C)),
                BorderBrush      = new SolidColorBrush(Windows.UI.Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                BorderThickness  = new Thickness(1),
                RenderTransform  = new RotateTransform { Angle = -1.5, CenterX = w / 2, CenterY = h / 2 },
                // ThemeShadow gives a subtle elevation shadow (WinUI 3 equivalent of DropShadowEffect)
                Shadow           = new ThemeShadow(),
                Translation      = new System.Numerics.Vector3(0, 0, 32),   // elevation for ThemeShadow
            };

            _popup = new Popup
            {
                Child            = _ghost,
                IsHitTestVisible = false,
                IsOpen           = false,
            };
        }

        /// <summary>
        /// Attaches the popup to <paramref name="host"/> and makes it visible at the given position.
        /// Call once the drag starts and the host panel is known.
        /// </summary>
        public void AttachTo(UIElement host)
        {
            if (_popup.Parent == null)
                (host as Panel)?.Children.Add(_popup);     // add to visual tree so popup has a root
        }

        /// <summary>
        /// Shows the ghost (opens the popup) and positions it under the cursor.
        /// <paramref name="pointerPosition"/> is the absolute position of the pointer in screen coordinates.
        /// </summary>
        public void Show(Point pointerPosition)
        {
            _popup.HorizontalOffset = pointerPosition.X - _grabOffset.X;
            _popup.VerticalOffset   = pointerPosition.Y - _grabOffset.Y;
            _popup.IsOpen           = true;
        }

        /// <summary>Updates the ghost position during drag.</summary>
        public void UpdatePosition(Point pointerPosition)
        {
            _popup.HorizontalOffset = pointerPosition.X - _grabOffset.X;
            _popup.VerticalOffset   = pointerPosition.Y - _grabOffset.Y;
        }

        /// <summary>Hides and cleans up the ghost popup.</summary>
        public void Dispose()
        {
            _popup.IsOpen = false;
            if (_popup.Parent is Panel panel)
                panel.Children.Remove(_popup);
        }
    }
}
