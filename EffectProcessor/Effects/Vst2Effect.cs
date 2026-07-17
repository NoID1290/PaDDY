using System;
using System.IO;
using Jacobi.Vst.Host.Interop;
using Jacobi.Vst.Core.Host;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public class Vst2Effect : IAudioEffect, IDisposable
    {
        public string Name { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; } = true;

        public Vst2Effect(string pluginPath)
        {
            if (!File.Exists(pluginPath))
                throw new FileNotFoundException("VST Plugin not found.", pluginPath);
                
            Name = Path.GetFileNameWithoutExtension(pluginPath);
            Description = "VST2 Plugin (Pending VST.NET2 Integration)";
        }

        public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
        {
            // TODO: Implement actual audio processing via VST interop
        }

        public void Reset()
        {
        }

        public void OpenEditor(IntPtr hWnd)
        {
            // TODO: Open native VST editor window
            // System.Windows.MessageBox.Show("VST Editor would open here.", "VST Editor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        public void CloseEditor()
        {
        }

        public void Dispose()
        {
        }
    }
}
