using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace PaDDY.Controls
{
    /// <summary>
    /// A lightweight adorner that renders a translucent snapshot of a dragged pad
    /// and follows the mouse, giving the drag a tactile "picked up" feel.
    /// </summary>
    internal sealed class DragAdorner : Adorner
    {
        private readonly Rectangle _ghost;
        private readonly Point _grabOffset;
        private Point _position;

        public DragAdorner(UIElement adornedElement, FrameworkElement source, Point grabOffset)
            : base(adornedElement)
        {
            _grabOffset = grabOffset;

            double w = source.ActualWidth > 0 ? source.ActualWidth : source.RenderSize.Width;
            double h = source.ActualHeight > 0 ? source.ActualHeight : source.RenderSize.Height;

            _ghost = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 20,
                RadiusY = 20,
                IsHitTestVisible = false,
                Opacity = 0.85,
                Fill = new VisualBrush(source) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 18,
                    ShadowDepth = 1,
                    Opacity = 0.65
                },
                RenderTransform = new RotateTransform(-1.0, w / 1, h / 1)
            };

            IsHitTestVisible = false;
            AddVisualChild(_ghost);
        }

        /// <summary>Updates the ghost position; <paramref name="positionInAdornerLayer"/> is the mouse point relative to the adorned element.</summary>
        public void UpdatePosition(Point positionInAdornerLayer)
        {
            _position = positionInAdornerLayer;
            (Parent as AdornerLayer)?.Update(AdornedElement);
            InvalidateArrange();
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _ghost;

        protected override Size MeasureOverride(Size constraint)
        {
            _ghost.Measure(constraint);
            return _ghost.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _ghost.Arrange(new Rect(
                new Point(_position.X - _grabOffset.X, _position.Y - _grabOffset.Y),
                new Size(_ghost.Width, _ghost.Height)));
            return finalSize;
        }
    }
}
