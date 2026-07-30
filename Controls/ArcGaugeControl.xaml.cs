using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PaDDY.Controls
{
    public partial class ArcGaugeControl : UserControl
    {
        private bool _isDragging;
        private Point _dragStartPoint;
        private double _dragStartValue;

        private const double StartAngle = -110.0;
        private const double EndAngle = 110.0;
        private const double TotalAngleRange = EndAngle - StartAngle;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(ArcGaugeControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(ArcGaugeControl),
                new PropertyMetadata(-24.0, OnMinMaxChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ArcGaugeControl),
                new PropertyMetadata(24.0, OnMinMaxChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public ArcGaugeControl()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateVisuals();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArcGaugeControl gauge) gauge.UpdateVisuals();
        }

        private static void OnMinMaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArcGaugeControl gauge) gauge.UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            double min = Minimum;
            double max = Maximum;
            if (max <= min) max = min + 1.0;

            double val = Math.Clamp(Value, min, max);
            double norm = (val - min) / (max - min);

            double currentAngle = StartAngle + (norm * TotalAngleRange);
            NeedleRotation.Angle = currentAngle;

            double radius = 70.0;
            Point center = new Point(85.0, 85.0);

            TrackArc.Data = CreateArcGeometry(center, radius, StartAngle, EndAngle);
            ValueArc.Data = CreateArcGeometry(center, radius, StartAngle, currentAngle);

            GainValueText.Text = val == 0.0 ? "0.0 dB" : $"{val:+0.0;-0.0} dB";
            ToolTip = $"Master Gain: {GainValueText.Text}";
        }

        private static Geometry CreateArcGeometry(Point center, double radius, double startAngleDeg, double endAngleDeg)
        {
            if (Math.Abs(endAngleDeg - startAngleDeg) < 0.1)
                return Geometry.Empty;

            double startRad = (startAngleDeg - 90.0) * Math.PI / 180.0;
            double endRad = (endAngleDeg - 90.0) * Math.PI / 180.0;

            Point startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));

            Point endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            bool isLargeArc = Math.Abs(endAngleDeg - startAngleDeg) > 180.0;

            PathGeometry geom = new PathGeometry();
            PathFigure fig = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };

            fig.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            geom.Figures.Add(fig);
            return geom;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                _dragStartValue = Value;
                CaptureMouse();
                Focus();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(this);
                double deltaY = _dragStartPoint.Y - currentPoint.Y;
                double deltaX = currentPoint.X - _dragStartPoint.X;

                double totalDelta = deltaY + (deltaX * 0.5);
                double range = Maximum - Minimum;
                double valChange = (totalDelta / 160.0) * range;

                double newValue = Math.Clamp(_dragStartValue + valChange, Minimum, Maximum);
                Value = Math.Round(newValue, 1);
                e.Handled = true;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double change = e.Delta > 0 ? 0.5 : -0.5;
            Value = Math.Clamp(Math.Round(Value + change, 1), Minimum, Maximum);
            e.Handled = true;
        }
    }
}
