using System.Reflection;
using System.Windows;
using System.IO;
using PaDDY.Services;
using PaDDY.Helpers;
using System;
using Microsoft.Win32;
using MessagingToolkit = System.Windows.MessageBox;

namespace PaDDY
{
    public partial class AboutWindow : Window
    {
        private Helpers.DX11ImageSource? _dx11Image;
        private double _time;

        public AboutWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                var asm = Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                string displayVersion;
                if (!string.IsNullOrEmpty(infoVersion))
                {
                    var plusIdx = infoVersion.IndexOf('+'); // strip build metadata if any
                    var infoBase = plusIdx >= 0 ? infoVersion[..plusIdx] : infoVersion;
                    displayVersion = $"Version {infoBase}";
                }
                else if (ver != null)
                {
                    displayVersion = ver.Revision > 0
                        ? $"Version {ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                        : $"Version {ver.Major}.{ver.Minor}.{ver.Build}";
                }
                else
                {
                    displayVersion = "Version —";
                }

                VersionLabel.Text = displayVersion;

                // Setup DX11 D3DImage Interop test
                try
                {
                    _dx11Image = new Helpers.DX11ImageSource();
                    _dx11Image.RenderFrame += OnRenderFrame;
                    VisualizerImage.Source = _dx11Image;

                    VisualizerImage.SizeChanged += (s, e) =>
                    {
                        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                        {
                            _dx11Image.Resize((int)e.NewSize.Width, (int)e.NewSize.Height);
                        }
                    };

                    System.Windows.Media.CompositionTarget.Rendering += OnCompositionRendering;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize DX11 interop visualizer: {ex}");
                }
            };

            PreviewKeyDown += OnPreviewKeyDown;

            Closed += (s, e) =>
            {
                System.Windows.Media.CompositionTarget.Rendering -= OnCompositionRendering;
                _dx11Image?.Dispose();
                App.DebugModeChanged -= OnDebugModeChanged;
            };

            App.DebugModeChanged += OnDebugModeChanged;
        }

        private void OnDebugModeChanged()
        {
            if (App.IsDebugMode && DevVisualizerBorder.Visibility != Visibility.Visible)
                ToggleDevVisualizer();
            else if (!App.IsDebugMode && DevVisualizerBorder.Visibility == Visibility.Visible)
                ToggleDevVisualizer();
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var isD = e.Key == System.Windows.Input.Key.D || (e.Key == System.Windows.Input.Key.System && e.SystemKey == System.Windows.Input.Key.D);
            if ((System.Windows.Input.Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt) && isD)
            {
                e.Handled = true;
                App.ToggleDebugMode();
            }
        }

        private void ToggleDevVisualizer()
        {
            if (DevVisualizerBorder.Visibility == Visibility.Visible)
            {
                DevVisualizerBorder.Visibility = Visibility.Collapsed;
                this.Height = 650;
            }
            else
            {
                DevVisualizerBorder.Visibility = Visibility.Visible;
                this.Height = 740;
                if (VisualizerImage.ActualWidth > 0 && VisualizerImage.ActualHeight > 0)
                {
                    _dx11Image?.Resize((int)VisualizerImage.ActualWidth, (int)VisualizerImage.ActualHeight);
                }
            }
        }

        private void OnRenderFrame(Vortice.Direct3D11.ID3D11Texture2D texture, Vortice.Direct3D11.ID3D11DeviceContext context)
        {
            if (_dx11Image?.D3D11Device == null) return;
            using var rtv = _dx11Image.D3D11Device.CreateRenderTargetView(texture);
            
            float r = (float)(Math.Sin(_time) * 0.5 + 0.5);
            float g = (float)(Math.Cos(_time * 1.5) * 0.5 + 0.5);
            float b = (float)(Math.Sin(_time * 0.7) * 0.5 + 0.5);
            
            context.ClearRenderTargetView(rtv, new Vortice.Mathematics.Color4(r, g, b, 1.0f));
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            if (DevVisualizerBorder.Visibility != Visibility.Visible) return;
            _time += 0.03;
            _dx11Image?.Invalidate();
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CreditsButton_Click(object sender, RoutedEventArgs e)
        {
            new CreditsWindow { Owner = this }.ShowDialog();
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/NoID1290/PaDDY",
                UseShellExecute = true
            });
        }

    }
}
