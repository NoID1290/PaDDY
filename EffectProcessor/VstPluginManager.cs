using System;
using System.Collections.Generic;
using System.IO;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public static class VstPluginManager
    {
        /// <summary>
        /// Controls whether VST3 plugins are enabled/loaded.
        /// VST3 support is experimental and only accessible in Dev Mode (Ctrl+Alt+D).
        /// </summary>
        public static bool IsVst3Enabled { get; set; } = false;

        /// <summary>
        /// Load a single VST plugin from a file path (.dll for VST2, .vst3 for VST3).
        /// </summary>
        public static IAudioEffect LoadPlugin(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                throw new FileNotFoundException("Plugin file not found.", pluginPath);

            // VST3 bundles are directories; regular files use File.Exists
            bool exists = File.Exists(pluginPath) || Directory.Exists(pluginPath);
            if (!exists)
                throw new FileNotFoundException("Plugin file not found.", pluginPath);

            var extension = Path.GetExtension(pluginPath).ToLowerInvariant();

            if (extension == ".dll")
            {
                return new Vst2Effect(pluginPath);
            }
            else if (extension == ".vst3")
            {
                if (!IsVst3Enabled)
                    throw new InvalidOperationException("VST3 plugins are experimental and only accessible in Developer Mode (Ctrl+Alt+D).");
                return new Vst3Effect(pluginPath);
            }
            
            throw new NotSupportedException($"Unsupported plugin extension: {extension}");
        }

        /// <summary>
        /// Loads all default vendored VST plugins from the application's Plugins directory.
        /// Returns both VST2 (.dll) and VST3 (.vst3) plugins found in Plugins/VST2/ and Plugins/VST3/.
        /// </summary>
        public static List<IVstEffect> LoadDefaultPlugins()
        {
            var plugins = new List<IVstEffect>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Ensure embedded VST plugins are extracted if missing from disk
            EnsureEmbeddedPluginsExtracted(baseDir);

            // Load VST2 plugins from Plugins/VST2/
            string vst2Dir = Path.Combine(baseDir, "Plugins", "VST2");
            if (Directory.Exists(vst2Dir))
            {
                foreach (string dllPath in Directory.GetFiles(vst2Dir, "*.dll"))
                {
                    try
                    {
                        var effect = new Vst2Effect(dllPath);
                        plugins.Add(effect);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load VST2 plugin '{Path.GetFileName(dllPath)}': {ex.Message}");
                    }
                }
            }

            // Load VST3 plugins from Plugins/VST3/ (Dev Mode only)
            if (IsVst3Enabled)
            {
                string vst3Dir = Path.Combine(baseDir, "Plugins", "VST3");
                if (Directory.Exists(vst3Dir))
                {
                    // VST3 bundles are directories ending in .vst3
                    foreach (string bundleDir in Directory.GetDirectories(vst3Dir, "*.vst3"))
                    {
                        try
                        {
                            var effect = new Vst3Effect(bundleDir);
                            plugins.Add(effect);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load VST3 plugin '{Path.GetFileName(bundleDir)}': {ex.Message}");
                        }
                    }

                    // Also check for standalone .vst3 files (some plugins ship as single files)
                    foreach (string vst3File in Directory.GetFiles(vst3Dir, "*.vst3"))
                    {
                        try
                        {
                            var effect = new Vst3Effect(vst3File);
                            plugins.Add(effect);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load VST3 file '{Path.GetFileName(vst3File)}': {ex.Message}");
                        }
                    }
                }
            }

            return plugins;
        }

        /// <summary>
        /// Attempts to load a user-configured VST plugin from the given path.
        /// Returns null on failure.
        /// </summary>
        public static IVstEffect? TryLoadUserPlugin(string? pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                return null;

            bool exists = File.Exists(pluginPath) || Directory.Exists(pluginPath);
            if (!exists)
                return null;

            try
            {
                var extension = Path.GetExtension(pluginPath).ToLowerInvariant();
                if (extension == ".dll")
                    return new Vst2Effect(pluginPath);
                else if (extension == ".vst3")
                {
                    if (!IsVst3Enabled)
                        return null;
                    return new Vst3Effect(pluginPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load user VST plugin '{Path.GetFileName(pluginPath)}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Extracts embedded default VST plugins to disk if they are missing or deleted.
        /// </summary>
        private static void EnsureEmbeddedPluginsExtracted(string baseDir)
        {
            var assembly = typeof(VstPluginManager).Assembly;
            string[] embeddedPluginResources = new[]
            {
                "Plugins.VST2.mdaDe-ess.dll",
                "Plugins.VST2.mdaDynamics.dll"
            };

            foreach (string resourceName in embeddedPluginResources)
            {
                try
                {
                    // Map logical resource name to output disk path (e.g. Plugins/VST2/mdaDe-ess.dll)
                    string fileName = resourceName.Replace("Plugins.VST2.", "");
                    string targetDir = Path.Combine(baseDir, "Plugins", "VST2");
                    string targetPath = Path.Combine(targetDir, fileName);

                    if (!File.Exists(targetPath))
                    {
                        Directory.CreateDirectory(targetDir);
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream != null)
                        {
                            using var fileStream = File.Create(targetPath);
                            stream.CopyTo(fileStream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to unpack embedded resource '{resourceName}': {ex.Message}");
                }
            }
        }
    }
}
