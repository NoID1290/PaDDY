namespace NoIDSoftwork.EffectProcessor.Effects;

public enum RemasterPreset
{
    Custom,
    CleanAndTransparent,
    WarmAnalog,
    PunchyClub,
    VocalAcoustic,
    LoudMaximizer
}

/// <summary>
/// Professional multi-stage Track Remastering effect.
/// Includes 3-band mastering EQ (Warmth, Punch, Brilliance), Mid/Side stereo widening,
/// harmonic tape/tube saturation, bus glue compression, and brickwall peak limiting.
/// </summary>
public sealed class RemasterEffect : IAudioEffect
{
    public string Name => "Remaster";
    public bool IsEnabled { get; set; } = false;

    // ── Mastering EQ ──────────────────────────────────────────────────────────
    /// <summary>Low-shelf warmth boost/cut in dB @ 120Hz (−6.0 to +6.0 dB).</summary>
    public double WarmthDb { get; set; } = 1.5;

    /// <summary>Peaking mid clarity/punch boost/cut in dB @ 2.5kHz (−6.0 to +6.0 dB).</summary>
    public double PunchDb { get; set; } = 1.0;

    /// <summary>High-shelf air/brilliance boost/cut in dB @ 10kHz (−6.0 to +6.0 dB).</summary>
    public double BrillianceDb { get; set; } = 2.0;

    // ── Spatial & Saturation ──────────────────────────────────────────────────
    /// <summary>Mid/Side stereo width expander factor (0.0 = Mono, 1.0 = Normal, 2.0 = Ultra Wide).</summary>
    public double StereoWidth { get; set; } = 1.2;

    /// <summary>Harmonic analog tape/tube saturation drive factor (0.0 to 1.0).</summary>
    public double Drive { get; set; } = 0.15;

    // ── Dynamic Bus Compression ───────────────────────────────────────────────
    /// <summary>Bus glue compressor threshold in dBFS (−30.0 to 0.0 dB).</summary>
    public double ThresholdDb { get; set; } = -12.0;

    /// <summary>Bus glue compression ratio (1.0 to 8.0).</summary>
    public double Ratio { get; set; } = 2.5;

    // ── Peak Limiter ──────────────────────────────────────────────────────────
    /// <summary>Brickwall peak limiter output ceiling in dBFS (−6.0 to 0.0 dB).</summary>
    public double LimiterCeilingDb { get; set; } = -0.3;

    private RemasterPreset _preset = RemasterPreset.WarmAnalog;
    public RemasterPreset Preset
    {
        get => _preset;
        set
        {
            _preset = value;
            if (value != RemasterPreset.Custom)
            {
                ApplyPreset(value);
            }
        }
    }

    public void ApplyPreset(RemasterPreset preset)
    {
        _preset = preset;
        switch (preset)
        {
            case RemasterPreset.CleanAndTransparent:
                WarmthDb = 0.5;
                PunchDb = 0.5;
                BrillianceDb = 1.0;
                StereoWidth = 1.05;
                Drive = 0.05;
                ThresholdDb = -10.0;
                Ratio = 1.8;
                LimiterCeilingDb = -0.3;
                break;

            case RemasterPreset.WarmAnalog:
                WarmthDb = 2.5;
                PunchDb = 0.8;
                BrillianceDb = 1.2;
                StereoWidth = 1.15;
                Drive = 0.35;
                ThresholdDb = -14.0;
                Ratio = 2.5;
                LimiterCeilingDb = -0.3;
                break;

            case RemasterPreset.PunchyClub:
                WarmthDb = 3.0;
                PunchDb = 2.5;
                BrillianceDb = 2.5;
                StereoWidth = 1.35;
                Drive = 0.25;
                ThresholdDb = -16.0;
                Ratio = 4.0;
                LimiterCeilingDb = -0.2;
                break;

            case RemasterPreset.VocalAcoustic:
                WarmthDb = 1.0;
                PunchDb = 2.2;
                BrillianceDb = 1.8;
                StereoWidth = 1.10;
                Drive = 0.10;
                ThresholdDb = -12.0;
                Ratio = 2.0;
                LimiterCeilingDb = -0.5;
                break;

            case RemasterPreset.LoudMaximizer:
                WarmthDb = 1.5;
                PunchDb = 1.5;
                BrillianceDb = 3.0;
                StereoWidth = 1.30;
                Drive = 0.45;
                ThresholdDb = -18.0;
                Ratio = 5.0;
                LimiterCeilingDb = -0.1;
                break;
        }
    }

    // ── Internal DSP State ─────────────────────────────────────────────────────
    private const int NumBands = 3;
    private readonly double[] _b0 = new double[NumBands];
    private readonly double[] _b1 = new double[NumBands];
    private readonly double[] _b2 = new double[NumBands];
    private readonly double[] _a1 = new double[NumBands];
    private readonly double[] _a2 = new double[NumBands];

    private double[,] _s1 = new double[NumBands, 2];
    private double[,] _s2 = new double[NumBands, 2];

    private double _lastWarmth, _lastPunch, _lastBrilliance;
    private int _lastSampleRate;

    private float _compEnvelope;
    private float _limiterEnvelope;

    public void Reset()
    {
        Array.Clear(_s1, 0, _s1.Length);
        Array.Clear(_s2, 0, _s2.Length);
        _compEnvelope = 0f;
        _limiterEnvelope = 0f;
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        // 1. Recompute EQ Biquads if coefficients changed
        if (_lastSampleRate != sampleRate ||
            Math.Abs(WarmthDb - _lastWarmth) > 1e-6 ||
            Math.Abs(PunchDb - _lastPunch) > 1e-6 ||
            Math.Abs(BrillianceDb - _lastBrilliance) > 1e-6)
        {
            _lastSampleRate = sampleRate;
            _lastWarmth = WarmthDb;
            _lastPunch = PunchDb;
            _lastBrilliance = BrillianceDb;

            ComputeLowShelf(0, WarmthDb, 120.0, sampleRate);
            ComputePeaking(1, PunchDb, 2500.0, 0.8, sampleRate);
            ComputeHighShelf(2, BrillianceDb, 10000.0, sampleRate);
        }

        if (_s1.GetLength(1) < channels)
        {
            _s1 = new double[NumBands, channels];
            _s2 = new double[NumBands, channels];
        }

        int chCount = Math.Min(channels, _s1.GetLength(1));
        int frames = count / channels;

        float compAttCoeff = (float)Math.Exp(-1.0 / (0.015 * sampleRate)); // 15ms attack
        float compRelCoeff = (float)Math.Exp(-1.0 / (0.150 * sampleRate)); // 150ms release
        float limAttCoeff = (float)Math.Exp(-1.0 / (0.001 * sampleRate));  // 1ms attack
        float limRelCoeff = (float)Math.Exp(-1.0 / (0.050 * sampleRate));  // 50ms release

        float thresholdLin = DbToLin(ThresholdDb);
        float ratio = (float)Math.Max(1.0, Ratio);
        float ceilingLin = DbToLin(LimiterCeilingDb);
        float drive = (float)Math.Clamp(Drive, 0.0, 1.0);
        float width = (float)Math.Clamp(StereoWidth, 0.0, 2.0);

        for (int f = 0; f < frames; f++)
        {
            int baseIdx = offset + f * channels;

            // Step A: 3-Band Mastering EQ (per channel)
            for (int ch = 0; ch < chCount; ch++)
            {
                double x = buffer[baseIdx + ch];
                for (int b = 0; b < NumBands; b++)
                {
                    double y = _b0[b] * x + _s1[b, ch];
                    _s1[b, ch] = _b1[b] * x - _a1[b] * y + _s2[b, ch];
                    _s2[b, ch] = _b2[b] * x - _a2[b] * y;
                    x = y;
                }
                buffer[baseIdx + ch] = (float)x;
            }

            // Step B: Mid/Side Stereo Widening (if stereo)
            if (channels >= 2)
            {
                float left = buffer[baseIdx];
                float right = buffer[baseIdx + 1];

                float mid = (left + right) * 0.5f;
                float side = (left - right) * 0.5f * width;

                buffer[baseIdx] = mid + side;
                buffer[baseIdx + 1] = mid - side;
            }

            // Step C: Harmonic Saturation / Tape Warmth
            if (drive > 0.001f)
            {
                float driveScale = 1.0f + 2.0f * drive;
                float normFactor = 1.0f / (1.0f + 0.5f * drive);
                for (int ch = 0; ch < channels; ch++)
                {
                    float orig = buffer[baseIdx + ch];
                    float sat = (float)Math.Tanh(orig * driveScale) * normFactor;
                    buffer[baseIdx + ch] = orig * (1.0f - drive) + sat * drive;
                }
            }

            // Step D: Bus Glue Compression
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                float a = Math.Abs(buffer[baseIdx + ch]);
                if (a > peak) peak = a;
            }

            float cCoeff = peak > _compEnvelope ? compAttCoeff : compRelCoeff;
            _compEnvelope = cCoeff * (_compEnvelope - peak) + peak;

            float compGain = 1f;
            if (_compEnvelope > thresholdLin && _compEnvelope > 1e-9f)
            {
                float envDb = LinToDb(_compEnvelope);
                float overDb = envDb - (float)ThresholdDb;
                float targetDb = (float)ThresholdDb + (overDb / ratio);
                compGain = DbToLin(targetDb - envDb);
            }

            for (int ch = 0; ch < channels; ch++)
            {
                buffer[baseIdx + ch] *= compGain;
            }

            // Step E: Brickwall Peak Limiter
            float postPeak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                float a = Math.Abs(buffer[baseIdx + ch]);
                if (a > postPeak) postPeak = a;
            }

            float lCoeff = postPeak > _limiterEnvelope ? limAttCoeff : limRelCoeff;
            _limiterEnvelope = lCoeff * (_limiterEnvelope - postPeak) + postPeak;

            if (_limiterEnvelope > ceilingLin && _limiterEnvelope > 1e-9f)
            {
                float limGain = ceilingLin / _limiterEnvelope;
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[baseIdx + ch] *= limGain;
                }
            }
        }
    }

    // ── Biquad Coefficient Calculations (RBJ Audio EQ Cookbook) ───────────────
    private void ComputeLowShelf(int band, double gainDb, double freq, int sampleRate)
    {
        double clamped = Math.Clamp(gainDb, -6.0, 6.0);
        double A = Math.Pow(10.0, clamped / 40.0);
        double w0 = 2.0 * Math.PI * freq / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / Math.Sqrt(2.0);

        double b0 = A * ((A + 1.0) - (A - 1.0) * cosW0 + 2.0 * Math.Sqrt(A) * alpha);
        double b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cosW0);
        double b2 = A * ((A + 1.0) - (A - 1.0) * cosW0 - 2.0 * Math.Sqrt(A) * alpha);
        double a0 = (A + 1.0) + (A - 1.0) * cosW0 + 2.0 * Math.Sqrt(A) * alpha;
        double a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cosW0);
        double a2 = (A + 1.0) + (A - 1.0) * cosW0 - 2.0 * Math.Sqrt(A) * alpha;

        SetCoeffs(band, b0, b1, b2, a0, a1, a2);
    }

    private void ComputeHighShelf(int band, double gainDb, double freq, int sampleRate)
    {
        double clamped = Math.Clamp(gainDb, -6.0, 6.0);
        double A = Math.Pow(10.0, clamped / 40.0);
        double w0 = 2.0 * Math.PI * freq / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / Math.Sqrt(2.0);

        double b0 = A * ((A + 1.0) + (A - 1.0) * cosW0 + 2.0 * Math.Sqrt(A) * alpha);
        double b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cosW0);
        double b2 = A * ((A + 1.0) + (A - 1.0) * cosW0 - 2.0 * Math.Sqrt(A) * alpha);
        double a0 = (A + 1.0) - (A - 1.0) * cosW0 + 2.0 * Math.Sqrt(A) * alpha;
        double a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cosW0);
        double a2 = (A + 1.0) - (A - 1.0) * cosW0 - 2.0 * Math.Sqrt(A) * alpha;

        SetCoeffs(band, b0, b1, b2, a0, a1, a2);
    }

    private void ComputePeaking(int band, double gainDb, double freq, double q, int sampleRate)
    {
        double clamped = Math.Clamp(gainDb, -6.0, 6.0);
        double A = Math.Pow(10.0, clamped / 40.0);
        double w0 = 2.0 * Math.PI * freq / sampleRate;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);

        double b0 = 1.0 + alpha * A;
        double b1 = -2.0 * cosW0;
        double b2 = 1.0 - alpha * A;
        double a0 = 1.0 + alpha / A;
        double a1 = -2.0 * cosW0;
        double a2 = 1.0 - alpha / A;

        SetCoeffs(band, b0, b1, b2, a0, a1, a2);
    }

    private void SetCoeffs(int band, double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0[band] = b0 / a0;
        _b1[band] = b1 / a0;
        _b2[band] = b2 / a0;
        _a1[band] = a1 / a0;
        _a2[band] = a2 / a0;
    }

    private static float DbToLin(double db) => (float)Math.Pow(10.0, db / 20.0);
    private static float LinToDb(float lin) => (float)(20.0 * Math.Log10(Math.Max(1e-9, lin)));
}
