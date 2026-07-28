using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NoIDSoftwork.EffectProcessor.Effects;

namespace PaDDY
{
    public partial class VstPluginWindow : Window
    {
        private readonly List<IVstEffect> _vstEffects;
        private VstHwndHost? _activeHwndHost;
        private IVstEffect? _currentEffect;

        public VstPluginWindow(List<IVstEffect> vstEffects, int initialIndex = 0)
        {
            InitializeComponent();
            _vstEffects = vstEffects ?? new List<IVstEffect>();

            PopulatePluginComboBox(initialIndex);
            Closed += VstPluginWindow_Closed;
        }

        private void PopulatePluginComboBox(int initialIndex)
        {
            PluginComboBox.Items.Clear();

            if (_vstEffects.Count == 0)
            {
                NoPluginText.Visibility = Visibility.Visible;
                EnablePluginCheckBox.IsEnabled = false;
                return;
            }

            for (int i = 0; i < _vstEffects.Count; i++)
            {
                var vst = _vstEffects[i];
                string typeLabel = vst is Vst2Effect ? "VST2" : (vst is Vst3Effect ? "VST3" : "VST");
                PluginComboBox.Items.Add($"{vst.Name} ({typeLabel})");
            }

            int indexToSelect = Math.Clamp(initialIndex, 0, _vstEffects.Count - 1);
            PluginComboBox.SelectedIndex = indexToSelect;
        }

        private void PluginComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int index = PluginComboBox.SelectedIndex;
            if (index < 0 || index >= _vstEffects.Count)
            {
                UnloadCurrentEditor();
                return;
            }

            LoadPluginEditor(_vstEffects[index]);
        }

        private void LoadPluginEditor(IVstEffect effect)
        {
            UnloadCurrentEditor();

            _currentEffect = effect;
            EnablePluginCheckBox.IsChecked = effect.IsEnabled;

            if (effect.HasEditor)
            {
                ParametersScrollViewer.Visibility = Visibility.Collapsed;
                NoPluginText.Visibility = Visibility.Collapsed;

                _activeHwndHost = new VstHwndHost(effect);
                NativeGuiHost.Content = _activeHwndHost;

                if (effect.GetEditorSize(out int width, out int height) && width > 50 && height > 50)
                {
                    Width = Math.Max(width + 40, 480);
                    Height = Math.Max(height + 120, 360);
                }
            }
            else
            {
                NativeGuiHost.Content = null;
                BuildParameterControls(effect);
            }
        }

        private void BuildParameterControls(IVstEffect effect)
        {
            ParametersStackPanel.Children.Clear();
            int count = effect.GetParameterCount();

            if (count == 0)
            {
                ParametersScrollViewer.Visibility = Visibility.Collapsed;
                NoPluginText.Text = $"{effect.Name}\n(No custom GUI or editable parameters exposed)";
                NoPluginText.Visibility = Visibility.Visible;
                return;
            }

            NoPluginText.Visibility = Visibility.Collapsed;
            ParametersScrollViewer.Visibility = Visibility.Visible;

            for (int i = 0; i < count; i++)
            {
                var param = effect.GetParameterInfo(i);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                var nameLbl = new TextBlock
                {
                    Text = param.Name,
                    Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush"),
                    FontSize = 12,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameLbl, 0);

                var slider = new Slider
                {
                    Minimum = 0.0,
                    Maximum = 1.0,
                    Value = param.Value,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                    Tag = i
                };
                Grid.SetColumn(slider, 1);

                string dispText = !string.IsNullOrEmpty(param.Display) ? $"{param.Display} {param.Label}".Trim() : param.Value.ToString("F2");
                var valLbl = new TextBlock
                {
                    Text = dispText,
                    Foreground = (System.Windows.Media.Brush)FindResource("AccentGreenBrush"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                Grid.SetColumn(valLbl, 2);

                int paramIdx = i;
                slider.ValueChanged += (s, args) =>
                {
                    float newVal = (float)args.NewValue;
                    effect.SetParameterValue(paramIdx, newVal);
                    var updated = effect.GetParameterInfo(paramIdx);
                    valLbl.Text = !string.IsNullOrEmpty(updated.Display) ? $"{updated.Display} {updated.Label}".Trim() : newVal.ToString("F2");
                };

                row.Children.Add(nameLbl);
                row.Children.Add(slider);
                row.Children.Add(valLbl);
                ParametersStackPanel.Children.Add(row);
            }
        }

        private void UnloadCurrentEditor()
        {
            if (_activeHwndHost != null)
            {
                NativeGuiHost.Content = null;
                _activeHwndHost.Dispose();
                _activeHwndHost = null;
            }
            _currentEffect = null;
        }

        private void EnablePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEffect != null)
            {
                _currentEffect.IsEnabled = EnablePluginCheckBox.IsChecked == true;
            }
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void VstPluginWindow_Closed(object? sender, EventArgs e)
        {
            UnloadCurrentEditor();
        }
    }

    public class VstHwndHost : HwndHost
    {
        private readonly IVstEffect _vstEffect;
        private IntPtr _hwndHost = IntPtr.Zero;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        public VstHwndHost(IVstEffect vstEffect)
        {
            _vstEffect = vstEffect;
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _hwndHost = CreateWindowEx(
                0, "static", "",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 400, 300,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            _vstEffect.OpenEditor(_hwndHost);
            return new HandleRef(this, _hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _vstEffect.CloseEditor();
            if (_hwndHost != IntPtr.Zero)
            {
                DestroyWindow(_hwndHost);
                _hwndHost = IntPtr.Zero;
            }
        }
    }
}
