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
using Whisper.net.LibraryLoader;

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
        private bool _loadedUseCuda;
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

        public static bool IsModelDownloaded(string? model)
        {
            GgmlType type = MapModel(model);
            string fileName = ModelFileName(type);
            string bundledPath = Path.Combine(AppContext.BaseDirectory, "models", fileName);
            string appDataPath = Path.Combine(ModelsFolder, fileName);
            return File.Exists(bundledPath) || File.Exists(appDataPath);
        }

        public static async Task DownloadModelAsync(string? model, IProgress<(double Percent, string StatusText)>? progress, CancellationToken ct = default)
        {
            GgmlType type = MapModel(model);
            string fileName = ModelFileName(type);
            string appDataPath = Path.Combine(ModelsFolder, fileName);
            string tmp = appDataPath + ".part";

            string urlType = type switch
            {
                GgmlType.Tiny => "tiny",
                GgmlType.Small => "small",
                GgmlType.Medium => "medium",
                GgmlType.LargeV3 => "large-v3",
                _ => "base"
            };
            string url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{urlType}.bin";

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var modelStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            using var fileWriter = File.Create(tmp);
            byte[] buffer = new byte[81920];
            int read;
            long totalRead = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportTime = 0;
            long lastReportBytes = 0;

            while ((read = await modelStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileWriter.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                totalRead += read;

                if (progress != null && sw.ElapsedMilliseconds - lastReportTime > 250)
                {
                    double percent = totalBytes.HasValue ? (double)totalRead / totalBytes.Value : -1;
                    
                    double elapsedSec = (sw.ElapsedMilliseconds - lastReportTime) / 1000.0;
                    double bytesPerSec = elapsedSec > 0 ? (totalRead - lastReportBytes) / elapsedSec : 0;
                    
                    string speed = bytesPerSec > 1048576 
                        ? $"{(bytesPerSec / 1048576):F1} MB/s" 
                        : $"{(bytesPerSec / 1024):F1} KB/s";

                    string dataInfo = totalBytes.HasValue
                        ? $"{(totalRead / 1048576.0):F1} / {(totalBytes.Value / 1048576.0):F1} MB"
                        : $"{(totalRead / 1048576.0):F1} MB";

                    progress.Report((percent, $"Downloading {model} model... {dataInfo} ({speed})"));
                    
                    lastReportTime = sw.ElapsedMilliseconds;
                    lastReportBytes = totalRead;
                }
            }
            fileWriter.Close();
            File.Move(tmp, appDataPath, overwrite: true);
        }

        /// <summary>
        /// Transcribes the given audio file. Returns the recognised text, or an
        /// empty string if nothing was recognised. Never throws.
        /// </summary>
        public async Task<string> TranscribeAsync(string audioFilePath, string? model, string? language, bool useCuda = false, CancellationToken ct = default)
        {
            try
            {
                float[] samples = DecodeToMono16k(audioFilePath);
                if (samples.Length < 16000 / 4) // less than ~0.25s of audio
                    return string.Empty;

                var factory = await GetFactoryAsync(model, useCuda, ct).ConfigureAwait(false);
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

        private async Task<WhisperFactory?> GetFactoryAsync(string? model, bool useCuda, CancellationToken ct)
        {
            GgmlType type = MapModel(model);
            string key = type.ToString();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_factory != null && _loadedModelKey == key && _loadedUseCuda == useCuda)
                    return _factory;

                string fileName = ModelFileName(type);
                string bundledPath = Path.Combine(AppContext.BaseDirectory, "models", fileName);
                string appDataPath = Path.Combine(ModelsFolder, fileName);
                string? finalModelPath = null;

                if (File.Exists(bundledPath))
                {
                    finalModelPath = bundledPath;
                }
                else if (File.Exists(appDataPath))
                {
                    finalModelPath = appDataPath;
                }
                else
                {
                    using var modelStream = await WhisperGgmlDownloader.Default
                        .GetGgmlModelAsync(type, cancellationToken: ct).ConfigureAwait(false);
                    string tmp = appDataPath + ".part";
                    using (var fileWriter = File.Create(tmp))
                        await modelStream.CopyToAsync(fileWriter, ct).ConfigureAwait(false);
                    File.Move(tmp, appDataPath, overwrite: true);
                    finalModelPath = appDataPath;
                }

                // Configure CUDA runtime preference before creating the factory
                if (useCuda)
                {
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Cuda,
                        RuntimeLibrary.Cpu
                    };
                }
                else
                {
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Cpu
                    };
                }

                _factory?.Dispose();
                var factoryOptions = new WhisperFactoryOptions { UseGpu = useCuda };
                _factory = WhisperFactory.FromPath(finalModelPath, factoryOptions);
                _loadedModelKey = key;
                _loadedUseCuda = useCuda;
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
