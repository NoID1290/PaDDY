namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Dynamic-range compressor with a peak-detecting envelope follower and a soft
/// makeup gain. Reduces the level of signals above <see cref="ThresholdDb"/> by
/// the given <see cref="Ratio"/>.
/// </summary>
public sealed class CompressorEffect : IAudioEffect
{
    public string Name => "Compressor";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Level above which compression starts, in dBFS (default -18).</summary>
    public double ThresholdDb { get; set; } = -18.0;

    /// <summary>Compression ratio (e.g. 4 = 4:1). Clamped to ≥ 1 (default 4).</summary>
    public double Ratio { get; set; } = 4.0;

    /// <summary>Envelope attack time in milliseconds (default 10).</summary>
    public double AttackMs { get; set; } = 10.0;

    /// <summary>Envelope release time in milliseconds (default 120).</summary>
    public double ReleaseMs { get; set; } = 120.0;

    /// <summary>Make-up gain applied after compression, in dB (default 0).</summary>
    public double MakeupDb { get; set; } = 0.0;

    private float _envelope;       // linear amplitude envelope
    private int _lastSampleRate;
    private float _attackCoeff;
    private float _releaseCoeff;

    public void Reset() => _envelope = 0f;

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        if (_lastSampleRate != sampleRate)
        {
            _lastSampleRate = sampleRate;
            _attackCoeff = TimeConstant(AttackMs, sampleRate);
            _releaseCoeff = TimeConstant(ReleaseMs, sampleRate);
        }

        // Recompute in case parameters changed between buffers.
        _attackCoeff = TimeConstant(AttackMs, sampleRate);
        _releaseCoeff = TimeConstant(ReleaseMs, sampleRate);

        float thresholdLin = DbToLin(ThresholdDb);
        float ratio = (float)Math.Max(1.0, Ratio);
        float makeup = DbToLin(MakeupDb);

        int frames = count / channels;
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = offset + f * channels;

            // Detect peak across channels for this frame.
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                float a = Math.Abs(buffer[baseIdx + ch]);
                if (a > peak) peak = a;
            }

            // Envelope follower (attack when rising, release when falling).
            float coeff = peak > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope = coeff * (_envelope - peak) + peak;

            // Compute gain reduction above threshold.
            float gain = 1f;
            if (_envelope > thresholdLin && _envelope > 1e-9f)
            {
                float envDb = LinToDb(_envelope);
                float overDb = envDb - (float)ThresholdDb;
                float compressedOverDb = overDb / ratio;
                float targetDb = (float)ThresholdDb + compressedOverDb;
                gain = DbToLin(targetDb - envDb);
            }

            float total = gain * makeup;
            for (int ch = 0; ch < channels; ch++)
                buffer[baseIdx + ch] *= total;
        }
    }

    private static float TimeConstant(double ms, int sampleRate)
    {
        double t = Math.Max(0.1, ms) / 1000.0;
        return (float)Math.Exp(-1.0 / (t * sampleRate));
    }

    private static float DbToLin(double db) => (float)Math.Pow(10.0, db / 20.0);

    private static float LinToDb(float lin) => (float)(20.0 * Math.Log10(Math.Max(1e-9, lin)));
}
