using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NoIDSoftwork.AudioProcessor;

namespace PaDDY.Services
{
    public class AudioImportResult
    {
        public bool Success { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string PadColor { get; set; } = string.Empty;
        public string Codec { get; set; } = "wav";
        public TimeSpan Duration { get; set; }
        public byte[] AudioData { get; set; } = Array.Empty<byte>();
        public string? ErrorMessage { get; set; }
    }

    public class AudioSourceInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string SuggestedName { get; set; } = string.Empty;
        public string SourceExtension { get; set; } = string.Empty;
        public string DetectedFormat { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public long FileSizeBytes { get; set; }

        public string DurationFormatted =>
            Duration.TotalSeconds < 60
                ? $"{Duration.TotalSeconds:0.0}s"
                : $"{(int)Duration.TotalMinutes}m {Duration.Seconds:00}s";

        public string FileSizeFormatted
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
                return $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
            }
        }
    }

    public static class AudioImportService
    {
        public static readonly string[] SupportedExtensions = new[]
        {
            ".wav", ".mp3", ".ogg", ".flac", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".opus"
        };

        public static bool IsSupportedExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(SupportedExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsSupportedExtensionOrDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (File.Exists(path)) return IsSupportedExtension(path);
            if (Directory.Exists(path))
            {
                try
                {
                    return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Any(IsSupportedExtension);
                }
                catch { return false; }
            }
            return false;
        }

        public static List<string> ExpandAudioFiles(IEnumerable<string> paths)
        {
            var result = new List<string>();
            if (paths == null) return result;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (File.Exists(path))
                {
                    if (IsSupportedExtension(path)) result.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                            .Where(IsSupportedExtension);
                        result.AddRange(files);
                    }
                    catch { }
                }
            }
            return result;
        }

        public static AudioSourceInfo GetSourceAudioInfo(string filePath)
        {
            var info = new AudioSourceInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                SuggestedName = Path.GetFileNameWithoutExtension(filePath),
                SourceExtension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                DetectedFormat = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()
            };

            if (!File.Exists(filePath)) return info;

            try
            {
                info.FileSizeBytes = new FileInfo(filePath).Length;

                using var reader = AudioReaderFactory.Open(filePath);
                info.Duration = reader.TotalTime;
                info.SampleRate = reader.WaveFormat.SampleRate;
                info.Channels = reader.WaveFormat.Channels;
                info.BitsPerSample = reader.WaveFormat.BitsPerSample;
                if (string.IsNullOrEmpty(info.DetectedFormat))
                {
                    info.DetectedFormat = reader.WaveFormat.Encoding.ToString();
                }
            }
            catch
            {
                try
                {
                    using var mfReader = new MediaFoundationReader(filePath);
                    info.Duration = mfReader.TotalTime;
                    info.SampleRate = mfReader.WaveFormat.SampleRate;
                    info.Channels = mfReader.WaveFormat.Channels;
                    info.BitsPerSample = mfReader.WaveFormat.BitsPerSample;
                }
                catch { }
            }

            return info;
        }

        public static Task<AudioImportResult> ImportFileAsync(string filePath)
        {
            return ConvertAndImportAsync(filePath, "wav", null);
        }

        public static async Task<AudioImportResult> ConvertAndImportAsync(
            string filePath,
            string targetCodec,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var result = new AudioImportResult
                {
                    DisplayName = Path.GetFileNameWithoutExtension(filePath),
                    Codec = targetCodec.ToLowerInvariant()
                };

                if (!File.Exists(filePath))
                {
                    result.Success = false;
                    result.ErrorMessage = "File does not exist.";
                    return result;
                }

                string normalizedTarget = targetCodec.Trim().ToLowerInvariant();
                if (normalizedTarget == "m4a") normalizedTarget = "aac";

                string sourceExt = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
                if (sourceExt == "m4a") sourceExt = "aac";

                // Fast path: If source and target are the same format, and it is a standard PCM WAV or supported container
                bool isSameFormat = string.Equals(sourceExt, normalizedTarget, StringComparison.OrdinalIgnoreCase);

                if (isSameFormat && normalizedTarget == "wav")
                {
                    try
                    {
                        using var wfr = new WaveFileReader(filePath);
                        if (wfr.WaveFormat.Encoding == WaveFormatEncoding.Pcm || wfr.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                        {
                            result.Duration = wfr.TotalTime;
                            result.AudioData = File.ReadAllBytes(filePath);
                            result.Success = true;
                            progress?.Report(100.0);
                            return result;
                        }
                    }
                    catch { }
                }
                else if (isSameFormat && (normalizedTarget == "mp3" || normalizedTarget == "flac" || normalizedTarget == "ogg" || normalizedTarget == "opus"))
                {
                    try
                    {
                        using var reader = AudioReaderFactory.Open(filePath);
                        if (reader.TotalTime > TimeSpan.Zero)
                        {
                            result.Duration = reader.TotalTime;
                            result.AudioData = File.ReadAllBytes(filePath);
                            result.Success = true;
                            progress?.Report(100.0);
                            return result;
                        }
                    }
                    catch { }
                }

                // Transcoding / Conversion pipeline
                string tempOutExt = StreamingRecorderFactory.ExtensionFor(normalizedTarget);
                string tempOutPath = Path.Combine(Path.GetTempPath(), $"paddy_conv_{Guid.NewGuid():N}.{tempOutExt}");

                IUnifiedAudioReader? unifiedReader = null;
                WaveStream? fallbackReader = null;

                try
                {
                    try
                    {
                        unifiedReader = AudioReaderFactory.Open(filePath);
                    }
                    catch
                    {
                        fallbackReader = CreateWaveReader(filePath);
                    }

                    WaveFormat srcFormat = unifiedReader?.WaveFormat ?? fallbackReader!.WaveFormat;
                    TimeSpan totalTime = unifiedReader?.TotalTime ?? fallbackReader!.TotalTime;
                    result.Duration = totalTime;

                    using var recorder = StreamingRecorderFactory.Create(normalizedTarget);
                    recorder.BeginRecording(tempOutPath, srcFormat);

                    byte[] buffer = new byte[16384];
                    int bytesRead;
                    double totalMs = totalTime.TotalMilliseconds;

                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (unifiedReader != null)
                        {
                            bytesRead = unifiedReader.Read(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            bytesRead = fallbackReader!.Read(buffer, 0, buffer.Length);
                        }

                        if (bytesRead <= 0) break;

                        recorder.AppendSamples(buffer, 0, bytesRead);

                        if (totalMs > 0 && progress != null)
                        {
                            double currentMs = unifiedReader?.CurrentTime.TotalMilliseconds ?? fallbackReader!.CurrentTime.TotalMilliseconds;
                            double pct = Math.Clamp((currentMs / totalMs) * 100.0, 0.0, 99.0);
                            progress.Report(pct);
                        }
                    }

                    var recordedDuration = recorder.Finish();
                    if (recordedDuration > TimeSpan.Zero)
                    {
                        result.Duration = recordedDuration;
                    }

                    progress?.Report(100.0);

                    if (File.Exists(tempOutPath))
                    {
                        result.AudioData = File.ReadAllBytes(tempOutPath);
                        result.Success = true;
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = "Conversion completed but output file was not found.";
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.ErrorMessage = "Conversion canceled by user.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Failed to convert audio: {ex.Message}";
                    return result;
                }
                finally
                {
                    unifiedReader?.Dispose();
                    fallbackReader?.Dispose();

                    if (File.Exists(tempOutPath))
                    {
                        try { File.Delete(tempOutPath); } catch { }
                    }
                }
            }, ct);
        }

        private static WaveStream CreateWaveReader(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".wav")
            {
                try
                {
                    return new WaveFileReader(filePath);
                }
                catch
                {
                    return new AudioFileReader(filePath);
                }
            }
            try
            {
                return new AudioFileReader(filePath);
            }
            catch
            {
                return new MediaFoundationReader(filePath);
            }
        }
    }
}
