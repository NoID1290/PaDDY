using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PaDDY.Services
{
    public class AudioImportResult
    {
        public bool Success { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Codec { get; set; } = "WAV";
        public TimeSpan Duration { get; set; }
        public byte[] AudioData { get; set; } = Array.Empty<byte>();
        public string? ErrorMessage { get; set; }
    }

    public static class AudioImportService
    {
        private static readonly string[] SupportedExtensions = new[]
        {
            ".wav", ".mp3", ".ogg", ".flac", ".aiff", ".aif", ".wma", ".m4a", ".aac"
        };

        public static bool IsSupportedExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(SupportedExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        public static Task<AudioImportResult> ImportFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                var result = new AudioImportResult
                {
                    DisplayName = Path.GetFileNameWithoutExtension(filePath)
                };

                if (!File.Exists(filePath))
                {
                    result.Success = false;
                    result.ErrorMessage = "File does not exist.";
                    return result;
                }

                try
                {
                    // 1. Try reading with NAudio AudioFileReader (handles WAV, MP3, AIFF, and standard MediaFoundation formats like M4A/AAC/WMA)
                    using (WaveStream reader = CreateWaveReader(filePath))
                    {
                        result.Duration = reader.TotalTime;
                        var waveFormat = reader.WaveFormat;

                        // Check if file is already standard PCM WAV format
                        bool isPcmWav = (reader is WaveFileReader wfr && 
                                        (waveFormat.Encoding == WaveFormatEncoding.Pcm || waveFormat.Encoding == WaveFormatEncoding.IeeeFloat));

                        if (isPcmWav)
                        {
                            result.Codec = "WAV";
                            result.AudioData = File.ReadAllBytes(filePath);
                            result.Success = true;
                            return result;
                        }

                        // Codec check requires or prefers conversion to standard PCM WAV format for 100% compatibility across PaDDY
                        result.Codec = "WAV";
                        using (var memStream = new MemoryStream())
                        {
                            // Resample/transcode to standard 16-bit PCM WAV stream
                            var sampleProvider = reader.ToSampleProvider();
                            WaveFileWriter.WriteWavFileToStream(memStream, sampleProvider.ToWaveProvider16());
                            result.AudioData = memStream.ToArray();
                        }
                        result.Success = true;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    // Fallback: If AudioFileReader / MediaFoundation fails, attempt converting using MediaFoundationResampler directly
                    try
                    {
                        using (var mfReader = new MediaFoundationReader(filePath))
                        using (var memStream = new MemoryStream())
                        {
                            result.Duration = mfReader.TotalTime;
                            result.Codec = "WAV";
                            var sampleProvider = mfReader.ToSampleProvider();
                            WaveFileWriter.WriteWavFileToStream(memStream, sampleProvider.ToWaveProvider16());
                            result.AudioData = memStream.ToArray();
                            result.Success = true;
                            return result;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Failed to process or convert audio codec: {innerEx.Message} (Original error: {ex.Message})";
                        return result;
                    }
                }
            });
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
            return new AudioFileReader(filePath);
        }
    }
}
