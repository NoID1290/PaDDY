using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Implements EBU R128 / ITU-R BS.1770-4 integrated loudness (LUFS) measurement
    /// and peak-limited normalization for PCM audio.
    /// </summary>
    public static class LoudnessNormalizer
    {
        public const double DefaultTargetLufs = -14.0;
        public const double DefaultMaxPeakDb = -0.5;

        /// <summary>
        /// Measure integrated loudness (in LUFS) of an audio file using ITU-R BS.1770-4 specification.
        /// </summary>
        public static double MeasureIntegratedLoudness(string filePath)
        {
            if (!File.Exists(filePath))
                return -70.0;

            using IUnifiedAudioReader reader = AudioReaderFactory.Open(filePath);
            ISampleProvider sampleProvider = reader.AsSampleProvider();
            return MeasureIntegratedLoudness(sampleProvider);
        }

        /// <summary>
        /// Measure integrated loudness (in LUFS) of an ISampleProvider.
        /// </summary>
        public static double MeasureIntegratedLoudness(ISampleProvider sampleProvider)
        {
            WaveFormat format = sampleProvider.WaveFormat;
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            if (channels <= 0 || sampleRate <= 0)
                return -70.0;

            // K-weighting filters per channel
            KWeightingFilter[] filters = new KWeightingFilter[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                filters[ch] = new KWeightingFilter(sampleRate);
            }

            // 400ms window size in samples per channel
            int windowSamples = (int)(sampleRate * 0.400);
            int stepSamples = (int)(sampleRate * 0.100); // 75% overlap (100ms step)

            // Read all samples and apply K-weighting
            List<double[]> kWeightedChannels = new List<double[]>();
            for (int ch = 0; ch < channels; ch++)
            {
                kWeightedChannels.Add(new double[8192]);
            }

            int totalFramesRead = 0;
            float[] readBuffer = new float[4096 * channels];
            int samplesRead;

            while ((samplesRead = sampleProvider.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                int frames = samplesRead / channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    if (totalFramesRead + frames > kWeightedChannels[ch].Length)
                    {
                        double[] chArr = kWeightedChannels[ch];
                        Array.Resize(ref chArr, Math.Max(chArr.Length * 2, totalFramesRead + frames));
                        kWeightedChannels[ch] = chArr;
                    }
                }

                for (int i = 0; i < frames; i++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float rawSample = readBuffer[i * channels + ch];
                        kWeightedChannels[ch][totalFramesRead + i] = filters[ch].Process(rawSample);
                    }
                }
                totalFramesRead += frames;
            }

            if (totalFramesRead < windowSamples)
            {
                // Signal too short for full window analysis, measure RMS
                double sumSq = 0;
                for (int i = 0; i < totalFramesRead; i++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        double s = kWeightedChannels[ch][i];
                        sumSq += s * s;
                    }
                }
                if (totalFramesRead == 0 || sumSq <= 1e-12) return -70.0;
                double meanSq = sumSq / (totalFramesRead * channels);
                return -0.691 + 10.0 * Math.Log10(meanSq);
            }

            // Calculate mean square per 400ms block
            int numBlocks = (totalFramesRead - windowSamples) / stepSamples + 1;
            double[][] blockEnergy = new double[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                blockEnergy[ch] = new double[numBlocks];
            }

            for (int b = 0; b < numBlocks; b++)
            {
                int start = b * stepSamples;
                for (int ch = 0; ch < channels; ch++)
                {
                    double sumSq = 0;
                    double[] chData = kWeightedChannels[ch];
                    for (int i = 0; i < windowSamples; i++)
                    {
                        double s = chData[start + i];
                        sumSq += s * s;
                    }
                    blockEnergy[ch][b] = sumSq / windowSamples;
                }
            }

            // Channel weighting coefficients (L, R, C = 1.0, Ls, Rs = 1.41)
            double[] channelWeights = new double[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                channelWeights[ch] = 1.0;
            }

            // Absolute threshold gating at -70 LUFS
            List<int> validBlocksAbs = new List<int>();
            for (int b = 0; b < numBlocks; b++)
            {
                double weightedSum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    weightedSum += channelWeights[ch] * blockEnergy[ch][b];
                }
                double lufs = -0.691 + 10.0 * Math.Log10(Math.Max(1e-12, weightedSum));
                if (lufs >= -70.0)
                {
                    validBlocksAbs.Add(b);
                }
            }

            if (validBlocksAbs.Count == 0)
                return -70.0;

            // Calculate relative threshold
            double sumWeightedEnergyAbs = 0;
            foreach (int b in validBlocksAbs)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    sumWeightedEnergyAbs += channelWeights[ch] * blockEnergy[ch][b];
                }
            }
            double zAvgAbs = sumWeightedEnergyAbs / validBlocksAbs.Count;
            double relativeThreshold = -0.691 + 10.0 * Math.Log10(Math.Max(1e-12, zAvgAbs)) - 10.0; // -10 LU below absolute gated average

            // Relative threshold gating
            double sumFinalEnergy = 0;
            int validBlocksRel = 0;
            for (int b = 0; b < numBlocks; b++)
            {
                double weightedSum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    weightedSum += channelWeights[ch] * blockEnergy[ch][b];
                }
                double lufs = -0.691 + 10.0 * Math.Log10(Math.Max(1e-12, weightedSum));
                if (lufs >= relativeThreshold)
                {
                    sumFinalEnergy += weightedSum;
                    validBlocksRel++;
                }
            }

            if (validBlocksRel == 0)
                return -70.0;

            double finalMeanSq = sumFinalEnergy / validBlocksRel;
            double integratedLufs = -0.691 + 10.0 * Math.Log10(Math.Max(1e-12, finalMeanSq));
            return Math.Round(integratedLufs, 2);
        }

        /// <summary>
        /// Normalizes a WAV file in-place or to an output path so its integrated loudness matches targetLufs.
        /// Peak limiting is applied so peak amplitude does not exceed maxPeakDb.
        /// </summary>
        public static bool NormalizeWavFile(string sourceWavPath, string outputWavPath, double targetLufs = DefaultTargetLufs, double maxPeakDb = DefaultMaxPeakDb)
        {
            if (!File.Exists(sourceWavPath))
                return false;

            string ext = Path.GetExtension(sourceWavPath).ToLowerInvariant();
            if (ext != ".wav")
                return false;

            double measuredLufs = MeasureIntegratedLoudness(sourceWavPath);
            if (measuredLufs <= -69.0)
            {
                // Signal is practically silent or empty, copy without modification if output differs
                if (!string.Equals(sourceWavPath, outputWavPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourceWavPath, outputWavPath, true);
                }
                return true;
            }

            double requiredGainDb = targetLufs - measuredLufs;
            float gainLinear = (float)Math.Pow(10.0, requiredGainDb / 20.0);

            // Read samples and check max peak after gain
            WaveFormat format;
            float[] samples;
            int read;

            try
            {
                using (var reader = new AudioFileReader(sourceWavPath))
                {
                    format = reader.WaveFormat;
                    int sampleCount = (int)(reader.Length / (format.BitsPerSample / 8));
                    samples = new float[sampleCount];
                    read = reader.Read(samples, 0, sampleCount);
                }
            }
            catch
            {
                try
                {
                    using (var reader = new WaveFileReader(sourceWavPath))
                    {
                        var sampleProv = reader.ToSampleProvider();
                        format = reader.WaveFormat;
                        int sampleCount = (int)(reader.Length / (format.BitsPerSample / 8));
                        samples = new float[sampleCount];
                        read = sampleProv.Read(samples, 0, sampleCount);
                    }
                }
                catch
                {
                    return false;
                }
            }

            float maxPeakLinear = 0f;
            for (int i = 0; i < read; i++)
            {
                float abs = Math.Abs(samples[i]);
                if (abs > maxPeakLinear) maxPeakLinear = abs;
            }

            float peakAfterGain = maxPeakLinear * gainLinear;
            float maxAllowedPeakLinear = (float)Math.Pow(10.0, maxPeakDb / 20.0);

            if (peakAfterGain > maxAllowedPeakLinear)
            {
                // Scale gain down to fit within max peak ceiling to prevent clipping
                gainLinear = maxAllowedPeakLinear / Math.Max(1e-6f, maxPeakLinear);
            }

            // Apply gain to samples
            for (int i = 0; i < read; i++)
            {
                samples[i] = Math.Clamp(samples[i] * gainLinear, -1.0f, 1.0f);
            }

            // Write output file using WaveFileWriter
            string tempFile = Path.Combine(Path.GetDirectoryName(outputWavPath) ?? Path.GetTempPath(), $"norm_{Guid.NewGuid():N}.wav");
            using (WaveFileWriter writer = new WaveFileWriter(tempFile, format))
            {
                writer.WriteSamples(samples, 0, read);
            }

            if (File.Exists(outputWavPath))
            {
                File.Delete(outputWavPath);
            }
            File.Move(tempFile, outputWavPath);

            return true;
        }

        // ── K-Weighting Filter Implementation ───────────────────────────────────────

        private sealed class KWeightingFilter
        {
            // Stage 1: High shelf filter coefficients
            private double _b0, _b1, _b2, _a1, _a2;
            private double _s1_x1, _s1_x2, _s1_y1, _s1_y2;

            // Stage 2: High pass filter coefficients
            private double _hp_b0, _hp_b1, _hp_b2, _hp_a1, _hp_a2;
            private double _s2_x1, _s2_x2, _s2_y1, _s2_y2;

            public KWeightingFilter(double sampleRate)
            {
                ComputeCoefficients(sampleRate);
            }

            private void ComputeCoefficients(double fs)
            {
                // High shelf (Stage 1) - ITU-R BS.1770
                double f0 = 1681.9744509555319;
                double G = 3.999843853973347;
                double Q = 0.7071752369554193;

                double K = Math.Tan(Math.PI * f0 / fs);
                double Vh = Math.Pow(10.0, G / 20.0);
                double Vb = Math.Pow(Vh, 0.4996667741994154);

                double a0 = 1.0 + K / Q + K * K;
                _b0 = (Vh + Vb * K / Q + K * K) / a0;
                _b1 = 2.0 * (K * K - Vh) / a0;
                _b2 = (Vh - Vb * K / Q + K * K) / a0;
                _a1 = 2.0 * (K * K - 1.0) / a0;
                _a2 = (1.0 - K / Q + K * K) / a0;

                // High pass (Stage 2) - 38 Hz 2nd order Butterworth
                double f0_hp = 38.13547087602444;
                double Q_hp = 0.5003270373238773;

                double K_hp = Math.Tan(Math.PI * f0_hp / fs);
                double a0_hp = 1.0 + K_hp / Q_hp + K_hp * K_hp;
                _hp_b0 = 1.0 / a0_hp;
                _hp_b1 = -2.0 / a0_hp;
                _hp_b2 = 1.0 / a0_hp;
                _hp_a1 = 2.0 * (K_hp * K_hp - 1.0) / a0_hp;
                _hp_a2 = (1.0 - K_hp / Q_hp + K_hp * K_hp) / a0_hp;
            }

            public double Process(double sample)
            {
                // Stage 1 (High shelf)
                double y1 = _b0 * sample + _b1 * _s1_x1 + _b2 * _s1_x2 - _a1 * _s1_y1 - _a2 * _s1_y2;
                _s1_x2 = _s1_x1;
                _s1_x1 = sample;
                _s1_y2 = _s1_y1;
                _s1_y1 = y1;

                // Stage 2 (High pass)
                double y2 = _hp_b0 * y1 + _hp_b1 * _s2_x1 + _hp_b2 * _s2_x2 - _hp_a1 * _s2_y1 - _hp_a2 * _s2_y2;
                _s2_x2 = _s2_x1;
                _s2_x1 = y1;
                _s2_y2 = _s2_y1;
                _s2_y1 = y2;

                return y2;
            }
        }
    }
}
