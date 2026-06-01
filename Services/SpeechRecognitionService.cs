using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Helpers;
using Whisper.net;
using Whisper.net.Ggml;

namespace PaDDY.Services
{
    /// <summary>
    /// Offline speech-to-text using Whisper.net. Decodes a recorded clip to
    /// 16 kHz mono and transcribes it. The ggml model is downloaded once on
    /// first use and cached under the app data folder.
    /// </summary>
    public sealed class SpeechRecognitionService : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private WhisperFactory? _factory;
        private string? _loadedModelKey;
        private bool _disposed;

        private static GgmlType MapModel(string? model) => (model ?? "base").Trim().ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large" => GgmlType.LargeV3,
            _ => GgmlType.Base,
        };

        private static string ModelFileName(GgmlType type) => $"ggml-{type.ToString().ToLowerInvariant()}.bin";

        private static string ModelsFolder
        {
            get
            {
                string dir = Path.Combine(AppDataPaths.AppDataRoot, "models");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Transcribes the given audio file. Returns the recognised text, or an
        /// empty string if nothing was recognised. Never throws.
        /// </summary>
        public async Task<string> TranscribeAsync(string audioFilePath, string? model, string? language, CancellationToken ct = default)
        {
            try
            {
                float[] samples = DecodeToMono16k(audioFilePath);
                if (samples.Length < 16000 / 4) // less than ~0.25s of audio
                    return string.Empty;

                var factory = await GetFactoryAsync(model, ct).ConfigureAwait(false);
                if (factory == null) return string.Empty;

                string lang = string.IsNullOrWhiteSpace(language) ? "auto" : language.Trim();

                using var processor = factory.CreateBuilder()
                    .WithLanguage(lang)
                    .Build();

                var sb = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(samples, ct).ConfigureAwait(false))
                {
                    sb.Append(segment.Text);
                }

                return sb.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<WhisperFactory?> GetFactoryAsync(string? model, CancellationToken ct)
        {
            GgmlType type = MapModel(model);
            string key = type.ToString();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_factory != null && _loadedModelKey == key)
                    return _factory;

                string modelPath = Path.Combine(ModelsFolder, ModelFileName(type));
                if (!File.Exists(modelPath))
                {
                    using var modelStream = await WhisperGgmlDownloader.Default
                        .GetGgmlModelAsync(type, cancellationToken: ct).ConfigureAwait(false);
                    string tmp = modelPath + ".part";
                    using (var fileWriter = File.Create(tmp))
                        await modelStream.CopyToAsync(fileWriter, ct).ConfigureAwait(false);
                    File.Move(tmp, modelPath, overwrite: true);
                }

                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(modelPath);
                _loadedModelKey = key;
                return _factory;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Decodes any supported audio file to a 16 kHz mono float buffer.
        /// </summary>
        private static float[] DecodeToMono16k(string audioFilePath)
        {
            using IUnifiedAudioReader reader = AudioReaderFactory.Open(audioFilePath);
            ISampleProvider source = reader.AsSampleProvider();

            // Resample to 16 kHz while preserving channel count, then downmix.
            ISampleProvider resampled = source.WaveFormat.SampleRate == 16000
                ? source
                : new WdlResamplingSampleProvider(source, 16000);

            int channels = resampled.WaveFormat.Channels;
            var mono = new System.Collections.Generic.List<float>(16000 * 8);
            var buffer = new float[16000 * channels];
            int read;
            while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (channels == 1)
                {
                    for (int i = 0; i < read; i++)
                        mono.Add(buffer[i]);
                }
                else
                {
                    for (int i = 0; i + channels <= read; i += channels)
                    {
                        float sum = 0f;
                        for (int c = 0; c < channels; c++)
                            sum += buffer[i + c];
                        mono.Add(sum / channels);
                    }
                }
            }

            return mono.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _factory?.Dispose();
            _factory = null;
            _gate.Dispose();
        }
    }
}
