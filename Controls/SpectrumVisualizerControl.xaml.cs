using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace PaDDY.Controls
{
    public partial class SpectrumVisualizerControl : UserControl
    {
        private const int BarCount = 30;
        private readonly List<WpfRectangle> _bars = new();
        private readonly double[] _barHeights = new double[BarCount];
        private readonly double[] _targetHeights = new double[BarCount];
        private readonly Random _rand = new();
        private DispatcherTimer? _animTimer;
        private bool _isPlaying;

        public SpectrumVisualizerControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildBars();
            _animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 fps animation
            };
            _animTimer.Tick += AnimTimer_Tick;
            _animTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _animTimer?.Stop();
            _animTimer = null;
        }

        private void BuildBars()
        {
            BarsHost.Children.Clear();
            BarsHost.ColumnDefinitions.Clear();
            _bars.Clear();

            LinearGradientBrush greenBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0)
            };
            greenBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x5E, 0x20), 0.0));
            greenBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0xAF, 0x50), 0.7));
            greenBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x81, 0xC7, 0x84), 1.0));

            for (int i = 0; i < BarCount; i++)
            {
                BarsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                WpfRectangle rect = new WpfRectangle
                {
                    Fill = greenBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Height = 4,
                    Margin = new Thickness(1, 0, 1, 0),
                    RadiusX = 1,
                    RadiusY = 1
                };

                Grid.SetColumn(rect, i);
                BarsHost.Children.Add(rect);
                _bars.Add(rect);
                _barHeights[i] = 4;
            }
        }

        public void SetAudioLevel(float level)
        {
            _isPlaying = level > 0.0005f;
            if (_isPlaying)
            {
                // High sensitivity perceptual scaling (square-root mapping)
                double percLevel = Math.Sqrt(Math.Max(level, 0.0001f));
                double baseLevel = Math.Clamp(percLevel * 70.0, 4.0, 70.0);
                for (int i = 0; i < BarCount; i++)
                {
                    // Dynamic frequency curve centered around middle frequencies with random variation
                    double freqFactor = Math.Sin((double)i / BarCount * Math.PI) * 0.85 + 0.15;
                    double noise = _rand.NextDouble() * 0.4 + 0.8;
                    _targetHeights[i] = Math.Clamp(baseLevel * freqFactor * noise, 4.0, 70.0);
                }
            }
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying)
            {
                // Ambient subtle decay / low activity waveform
                for (int i = 0; i < BarCount; i++)
                {
                    double target = 4.0 + (Math.Sin(i * 0.4 + DateTime.Now.Ticks / 2000000.0) + 1.0) * 3.0;
                    _targetHeights[i] = target;
                }
            }

            for (int i = 0; i < BarCount && i < _bars.Count; i++)
            {
                // Smooth lerp
                _barHeights[i] += (_targetHeights[i] - _barHeights[i]) * 0.3;
                _bars[i].Height = Math.Max(2.0, _barHeights[i]);
            }
        }
    }
}
