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
    public partial class KnobControl : UserControl
    {
        private bool _isDragging;
        private Point _dragStartPoint;
        private double _dragStartValue;

        private const double StartAngle = -135.0; // Min value angle
        private const double EndAngle = 135.0;     // Max value angle
        private const double TotalAngleRange = EndAngle - StartAngle; // 270 degrees

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(KnobControl),
                new PropertyMetadata(0.0, OnMinMaxChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(KnobControl),
                new PropertyMetadata(1.0, OnMinMaxChanged));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(KnobControl),
                new PropertyMetadata(0.01));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(KnobControl),
                new PropertyMetadata("Knob", OnLabelChanged));

        public static readonly DependencyProperty DisplayFormatProperty =
            DependencyProperty.Register(nameof(DisplayFormat), typeof(string), typeof(KnobControl),
                new PropertyMetadata("{0:F2}", OnFormatChanged));

        public static readonly DependencyProperty ShowValueTextProperty =
            DependencyProperty.Register(nameof(ShowValueText), typeof(bool), typeof(KnobControl),
                new PropertyMetadata(false, OnShowValueTextChanged));

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

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string DisplayFormat
        {
            get => (string)GetValue(DisplayFormatProperty);
            set => SetValue(DisplayFormatProperty, value);
        }

        public bool ShowValueText
        {
            get => (bool)GetValue(ShowValueTextProperty);
            set => SetValue(ShowValueTextProperty, value);
        }

        public KnobControl()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateVisuals();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob) knob.UpdateVisuals();
        }

        private static void OnMinMaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob) knob.UpdateVisuals();
        }

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob) knob.LabelText.Text = e.NewValue as string ?? "";
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob) knob.UpdateVisuals();
        }

        private static void OnShowValueTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KnobControl knob)
                knob.ValueText.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateVisuals()
        {
            double min = Minimum;
            double max = Maximum;
            if (max <= min) max = min + 1.0;

            double val = Math.Clamp(Value, min, max);
            double norm = (val - min) / (max - min);

            // Update rotation
            double currentAngle = StartAngle + (norm * TotalAngleRange);
            PointerRotation.Angle = currentAngle;

            // Render background track arc and active value arc
            double radius = 28.0;
            Point center = new Point(32.0, 32.0);

            TrackArc.Data = CreateArcGeometry(center, radius, StartAngle, EndAngle);
            ValueArc.Data = norm > 0.001 ? CreateArcGeometry(center, radius, StartAngle, currentAngle) : null;

            // Format value text
            try
            {
                ValueText.Text = string.Format(DisplayFormat, val);
            }
            catch
            {
                ValueText.Text = val.ToString("F2");
            }

            ToolTip = $"{Label}: {ValueText.Text}";
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
                double deltaY = _dragStartPoint.Y - currentPoint.Y; // Moving up increases value
                double deltaX = currentPoint.X - _dragStartPoint.X;

                double totalDelta = deltaY + (deltaX * 0.5);
                double range = Maximum - Minimum;
                double valChange = (totalDelta / 150.0) * range; // 150px drag spans full range

                double newValue = Math.Clamp(_dragStartValue + valChange, Minimum, Maximum);
                if (Step > 0)
                {
                    newValue = Math.Round(newValue / Step) * Step;
                }

                Value = Math.Clamp(newValue, Minimum, Maximum);
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
            double range = Maximum - Minimum;
            double change = Step > 0 ? Step : range * 0.05;
            if (e.Delta < 0) change = -change;

            double newValue = Math.Clamp(Value + change, Minimum, Maximum);
            if (Step > 0) newValue = Math.Round(newValue / Step) * Step;

            Value = newValue;
            e.Handled = true;
        }
    }
}
