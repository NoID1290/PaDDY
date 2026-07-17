using System;
using System.IO;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public static class VstPluginManager
    {
        public static IAudioEffect LoadPlugin(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath) || !File.Exists(pluginPath))
            {
                throw new FileNotFoundException("Plugin file not found.", pluginPath);
            }

            var extension = Path.GetExtension(pluginPath).ToLowerInvariant();

            if (extension == ".dll")
            {
                // Assume VST2 for now. Vst3HostSharp or NPlug could handle .vst3.
                return new Vst2Effect(pluginPath);
            }
            else if (extension == ".vst3")
            {
                // In a complete implementation, this would instantiate Vst3Effect.
                // For now, we'll throw NotSupportedException until the VST3 interop is fully added.
                throw new NotSupportedException("VST3 support is pending integration with a VST3 host wrapper.");
            }
            
            throw new NotSupportedException($"Unsupported plugin extension: {extension}");
        }
    }
}
