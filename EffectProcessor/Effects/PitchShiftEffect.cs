namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Real-time pitch shifter using two overlapping delay-line "grains" read
/// at a variable rate and crossfaded with triangular windows. Shifts pitch
/// up or down in semitones without changing playback speed.
/// </summary>
public sealed class PitchShiftEffect : IAudioEffect
{
    public string Name => "Pitch Shift";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Pitch shift in semitones. Negative = lower, positive = higher (default 0, range -24..+24).</summary>
    public double PitchSemitones { get; set; } = 0.0;

    /// <summary>
    /// Grain size in milliseconds (default 50). Smaller grains track fast
    /// or percussive material better; larger grains sound smoother on
    /// sustained tones but add more latency-like smearing.
    /// </summary>
    public double GrainSizeMs { get; set; } = 50.0;

    /// <summary>Wet/dry mix 0–1 (0 = fully dry, 1 = fully wet, default 1.0).</summary>
    public double Mix { get; set; } = 1.0;

    private float[][] _delayBuffers = [];
    private double[] _phase1 = [];
    private double[] _phase2 = [];
    private int _writePos;
    private int _bufferLen;
    private int _grainSamples;
    private int _lastSampleRate;
    private int _lastChannels;

    public void Reset()
    {
        foreach (var buf in _delayBuffers)
            Array.Clear(buf, 0, buf.Length);
        Array.Clear(_phase1, 0, _phase1.Length);
        Array.Clear(_phase2, 0, _phase2.Length);
        _writePos = 0;
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        int grainSamples = Math.Max(16, (int)(sampleRate * Math.Clamp(GrainSizeMs, 10.0, 500.0) / 1000.0));

        // Reallocate if format, channel count, or grain size changed.
        if (_lastSampleRate != sampleRate || _lastChannels != channels || _grainSamples != grainSamples)
        {
            _grainSamples = grainSamples;
            _bufferLen = grainSamples * 4; // headroom for delay excursion + interpolation
            _lastSampleRate = sampleRate;
            _lastChannels = channels;

            _delayBuffers = new float[channels][];
            _phase1 = new double[channels];
            _phase2 = new double[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                _delayBuffers[ch] = new float[_bufferLen];
                _phase1[ch] = 0.0;
                _phase2[ch] = grainSamples / 2.0; // 50% offset for smooth crossfade
            }
            _writePos = 0;
        }

        double ratio = Math.Pow(2.0, Math.Clamp(PitchSemitones, -24.0, 24.0) / 12.0);
        double step = 1.0 - ratio; // per-sample drift of each grain's read delay
        float wet = (float)Math.Clamp(Mix, 0.0, 1.0);
        float dry = 1.0f - wet;

        for (int i = 0; i < count; i += channels)
        {
            int sampleIdx = offset + i;

            for (int ch = 0; ch < channels; ch++)
            {
                float[] buf = _delayBuffers[ch];
                float input = buffer[sampleIdx + ch];
                buf[_writePos] = input;

                double phase1 = _phase1[ch];
                double phase2 = _phase2[ch];

                float grain1 = ReadInterpolated(buf, _writePos - phase1, _bufferLen);
                float grain2 = ReadInterpolated(buf, _writePos - phase2, _bufferLen);

                float w1 = TriangleWindow(phase1, grainSamples);
                float w2 = TriangleWindow(phase2, grainSamples);
                float wSum = w1 + w2;

                float shifted = wSum > 0.0001f ? (grain1 * w1 + grain2 * w2) / wSum : input;
                buffer[sampleIdx + ch] = dry * input + wet * shifted;

                // Advance grain phases; wrap when a grain exhausts its delay range
                // (hidden by the triangular window being ~0 at that instant).
                phase1 += step;
                if (phase1 >= grainSamples) phase1 -= grainSamples;
                else if (phase1 < 0) phase1 += grainSamples;

                phase2 += step;
                if (phase2 >= grainSamples) phase2 -= grainSamples;
                else if (phase2 < 0) phase2 += grainSamples;

                _phase1[ch] = phase1;
                _phase2[ch] = phase2;
            }

            _writePos = (_writePos + 1) % _bufferLen;
        }
    }

    /// <summary>Reads a fractionally-delayed sample from the circular buffer via linear interpolation.</summary>
    private static float ReadInterpolated(float[] buf, double delayedPos, int bufferLen)
    {
        double pos = delayedPos % bufferLen;
        if (pos < 0) pos += bufferLen;

        int i0 = (int)pos;
        int i1 = (i0 + 1) % bufferLen;
        float frac = (float)(pos - i0);

        return buf[i0] + frac * (buf[i1] - buf[i0]);
    }

    /// <summary>Triangular envelope: 0 at grain edges, 1 at grain center — crossfades the two overlapping grains.</summary>
    private static float TriangleWindow(double phase, int grainSamples)
    {
        double normalized = phase / grainSamples; // 0..1
        return (float)(1.0 - Math.Abs(2.0 * normalized - 1.0));
    }
}
