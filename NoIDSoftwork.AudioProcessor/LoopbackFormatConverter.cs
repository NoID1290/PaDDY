using System;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Converts multi-channel IEEE float audio to stereo IEEE float.
    /// Performs proper 7.1/5.1/quad/mono→stereo downmix with ITU-R BS.775 coefficients,
    /// then optionally resamples to a target sample rate using linear interpolation.
    /// </summary>
    public sealed class LoopbackFormatConverter
    {
        // Standard ITU-R BS.775 downmix coefficients
        private const float CenterMix = 0.7071f;  // -3 dB
        private const float SurroundMix = 0.7071f; // -3 dB
        private const float LfeMix = 0.5f;         // -6 dB (optional, often omitted)

        private readonly WaveFormat _sourceFormat;
        private readonly WaveFormat _outputFormat;
        private readonly int _sourceChannels;
        private readonly int _sourceSampleRate;
        private readonly int _targetSampleRate;
        private readonly bool _needsResample;

        /// <summary>The output format (always stereo IEEE float).</summary>
        public WaveFormat OutputFormat => _outputFormat;

        /// <summary>The source format this converter was created for.</summary>
        public WaveFormat SourceFormat => _sourceFormat;

        /// <param name="sourceFormat">The multi-channel source format from the capture device.</param>
        /// <param name="targetSampleRate">Target sample rate. 0 = keep source rate.</param>
        public LoopbackFormatConverter(WaveFormat sourceFormat, int targetSampleRate = 0)
        {
            _sourceFormat = sourceFormat ?? throw new ArgumentNullException(nameof(sourceFormat));
            _sourceChannels = sourceFormat.Channels;
            _sourceSampleRate = sourceFormat.SampleRate;
            _targetSampleRate = targetSampleRate > 0 ? targetSampleRate : _sourceSampleRate;
            _needsResample = _targetSampleRate != _sourceSampleRate;
            _outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(_targetSampleRate, 2);
        }

        /// <summary>
        /// Converts a buffer of source-format audio data to stereo float.
        /// Returns the output byte array and its valid byte count.
        /// </summary>
        public (byte[] Buffer, int ByteCount) Process(byte[] sourceBuffer, int sourceByteCount)
        {
            int bytesPerSample = _sourceFormat.BitsPerSample / 8;
            if (bytesPerSample <= 0)
                return (Array.Empty<byte>(), 0);

            int sourceFrames = sourceByteCount / (_sourceChannels * bytesPerSample);
            if (sourceFrames == 0)
                return (Array.Empty<byte>(), 0);

            // Step 1: Downmix to stereo float
            float[] stereoSamples = DownmixToStereo(sourceBuffer, sourceByteCount, sourceFrames);

            // Step 2: Resample if needed
            float[] outputSamples;
            if (_needsResample)
                outputSamples = Resample(stereoSamples);
            else
                outputSamples = stereoSamples;

            // Step 3: Convert float array to byte array
            int outputBytes = outputSamples.Length * 4;
            var outputBuffer = new byte[outputBytes];
            Buffer.BlockCopy(outputSamples, 0, outputBuffer, 0, outputBytes);

            return (outputBuffer, outputBytes);
        }

        /// <summary>
        /// Convenience: process and produce a WaveInEventArgs-compatible result.
        /// </summary>
        public WaveInEventArgs ProcessToEventArgs(byte[] sourceBuffer, int sourceByteCount)
        {
            var (buf, count) = Process(sourceBuffer, sourceByteCount);
            return new WaveInEventArgs(buf, count);
        }

        private float[] DownmixToStereo(byte[] buffer, int byteCount, int frames)
        {
            // Output: interleaved L, R pairs
            var output = new float[frames * 2];
            int bytesPerSample = _sourceFormat.BitsPerSample / 8;
            bool isFloat = _sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat && _sourceFormat.BitsPerSample == 32;

            if (_sourceChannels == 2)
            {
                for (int i = 0; i < frames * 2; i++)
                {
                    int sourceOffset = i * bytesPerSample;
                    output[i] = ReadSampleAsFloat(buffer, sourceOffset, bytesPerSample, isFloat);
                }
                return output;
            }

            if (_sourceChannels == 1)
            {
                // Mono → duplicate to both channels
                for (int f = 0; f < frames; f++)
                {
                    int sourceOffset = f * bytesPerSample;
                    float s = ReadSampleAsFloat(buffer, sourceOffset, bytesPerSample, isFloat);
                    output[f * 2] = s;
                    output[f * 2 + 1] = s;
                }
                return output;
            }

            // Multi-channel downmix
            // Standard channel order: FL, FR, FC, LFE, BL/SL, BR/SR, SL, SR
            for (int f = 0; f < frames; f++)
            {
                int frameOffset = f * _sourceChannels * bytesPerSample;
                float fl = ReadSampleAsFloat(buffer, frameOffset, bytesPerSample, isFloat);
                float fr = _sourceChannels > 1 ? ReadSampleAsFloat(buffer, frameOffset + bytesPerSample, bytesPerSample, isFloat) : fl;
                float fc = _sourceChannels > 2 ? ReadSampleAsFloat(buffer, frameOffset + (2 * bytesPerSample), bytesPerSample, isFloat) : 0f;
                float lfe = _sourceChannels > 3 ? ReadSampleAsFloat(buffer, frameOffset + (3 * bytesPerSample), bytesPerSample, isFloat) : 0f;
                float bl = _sourceChannels > 4 ? ReadSampleAsFloat(buffer, frameOffset + (4 * bytesPerSample), bytesPerSample, isFloat) : 0f;
                float br = _sourceChannels > 5 ? ReadSampleAsFloat(buffer, frameOffset + (5 * bytesPerSample), bytesPerSample, isFloat) : 0f;
                float sl = _sourceChannels > 6 ? ReadSampleAsFloat(buffer, frameOffset + (6 * bytesPerSample), bytesPerSample, isFloat) : 0f;
                float sr = _sourceChannels > 7 ? ReadSampleAsFloat(buffer, frameOffset + (7 * bytesPerSample), bytesPerSample, isFloat) : 0f;

                // ITU-R BS.775 stereo downmix
                float left  = fl + CenterMix * fc + SurroundMix * bl + SurroundMix * sl + LfeMix * lfe;
                float right = fr + CenterMix * fc + SurroundMix * br + SurroundMix * sr + LfeMix * lfe;

                // Normalize to prevent clipping (peak coefficient sum ≈ 1 + 0.707 + 0.707 + 0.707 + 0.5 ≈ 3.62)
                // We apply a conservative normalization factor
                const float normFactor = 1.0f / 2.5f;
                output[f * 2] = left * normFactor;
                output[f * 2 + 1] = right * normFactor;
            }

            return output;
        }

        private static float ReadSampleAsFloat(byte[] buffer, int byteOffset, int bytesPerSample, bool isFloat)
        {
            if (isFloat)
                return Math.Clamp(BitConverter.ToSingle(buffer, byteOffset), -1f, 1f);

            return bytesPerSample switch
            {
                2 => BitConverter.ToInt16(buffer, byteOffset) / 32768f,
                3 => ReadPcm24(buffer, byteOffset) / 8388608f,
                4 => BitConverter.ToInt32(buffer, byteOffset) / 2147483648f,
                _ => 0f
            };
        }

        private static int ReadPcm24(byte[] buffer, int byteOffset)
        {
            int sample = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
            if ((sample & 0x800000) != 0)
                sample |= unchecked((int)0xFF000000);
            return sample;
        }

        private float[] Resample(float[] stereoInput)
        {
            int inputFrames = stereoInput.Length / 2;
            double ratio = (double)_targetSampleRate / _sourceSampleRate;
            int outputFrames = (int)(inputFrames * ratio);
            var output = new float[outputFrames * 2];

            for (int i = 0; i < outputFrames; i++)
            {
                double srcPos = i / ratio;
                int srcIndex = (int)srcPos;
                float frac = (float)(srcPos - srcIndex);

                int idx0 = Math.Min(srcIndex, inputFrames - 1) * 2;
                int idx1 = Math.Min(srcIndex + 1, inputFrames - 1) * 2;

                // Linear interpolation for each channel
                output[i * 2]     = stereoInput[idx0]     * (1f - frac) + stereoInput[idx1]     * frac;
                output[i * 2 + 1] = stereoInput[idx0 + 1] * (1f - frac) + stereoInput[idx1 + 1] * frac;
            }

            return output;
        }

        /// <summary>
        /// Creates a silence buffer of the specified duration in the output format.
        /// </summary>
        public (byte[] Buffer, int ByteCount) CreateSilence(int frames)
        {
            int bytes = frames * _outputFormat.BlockAlign;
            return (new byte[bytes], bytes);
        }
    }
}
