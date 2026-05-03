namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// 5-band parametric equalizer using IIR biquad peaking filters.
/// Bands: Sub-Bass (80 Hz), Bass (250 Hz), Mid (1 kHz), Presence (4 kHz), Treble (12 kHz).
/// Gain range: −12 to +12 dB per band.
/// Coefficients follow the Audio EQ Cookbook (R. Zölzer).
/// </summary>
public sealed class EqualizerEffect : IAudioEffect
{
    public string Name => "Equalizer";
    public bool IsEnabled { get; set; } = false;

    // Band gains in dB (clamped to ±12 on apply)
    public double SubBassDb  { get; set; } = 0.0;   // 80 Hz
    public double BassDb     { get; set; } = 0.0;   // 250 Hz
    public double MidDb      { get; set; } = 0.0;   // 1 000 Hz
    public double PresenceDb { get; set; } = 0.0;   // 4 000 Hz
    public double TrebleDb   { get; set; } = 0.0;   // 12 000 Hz

    private static readonly (double Freq, double Q)[] BandDefs =
    {
        (80.0,    0.71),
        (250.0,   0.71),
        (1000.0,  0.71),
        (4000.0,  0.71),
        (12000.0, 0.71),
    };

    private const int NumBands = 5;

    // Normalised biquad coefficients (divided by a0)
    private readonly double[] _b0 = new double[NumBands];
    private readonly double[] _b1 = new double[NumBands];
    private readonly double[] _b2 = new double[NumBands];
    private readonly double[] _a1 = new double[NumBands];
    private readonly double[] _a2 = new double[NumBands];

    // Direct-Form II transposed state: [band, channel]
    private double[,] _s1 = new double[NumBands, 2];
    private double[,] _s2 = new double[NumBands, 2];

    private double[] _lastGainDb   = new double[NumBands];
    private int      _lastSampleRate;

    public void Reset()
    {
        Array.Clear(_s1, 0, _s1.Length);
        Array.Clear(_s2, 0, _s2.Length);
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        double[] gains = { SubBassDb, BassDb, MidDb, PresenceDb, TrebleDb };

        if (_lastSampleRate != sampleRate || !GainsMatch(gains))
        {
            _lastSampleRate = sampleRate;
            for (int b = 0; b < NumBands; b++)
                ComputeBiquad(b, gains[b], sampleRate);
            Array.Copy(gains, _lastGainDb, NumBands);
        }

        // Expand state storage if channel count increased
        if (_s1.GetLength(1) < channels)
        {
            _s1 = new double[NumBands, channels];
            _s2 = new double[NumBands, channels];
        }

        int chCount = Math.Min(channels, _s1.GetLength(1));

        for (int i = 0; i < count; i += channels)
        {
            int idx = offset + i;
            for (int ch = 0; ch < chCount; ch++)
            {
                double x = buffer[idx + ch];

                // Apply each biquad band in sequence (Direct-Form II Transposed)
                for (int b = 0; b < NumBands; b++)
                {
                    double y  = _b0[b] * x + _s1[b, ch];
                    _s1[b, ch] = _b1[b] * x - _a1[b] * y + _s2[b, ch];
                    _s2[b, ch] = _b2[b] * x - _a2[b] * y;
                    x = y;
                }

                buffer[idx + ch] = (float)x;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool GainsMatch(double[] gains)
    {
        for (int i = 0; i < NumBands; i++)
            if (Math.Abs(gains[i] - _lastGainDb[i]) > 1e-9) return false;
        return true;
    }

    private void ComputeBiquad(int band, double gainDb, int sampleRate)
    {
        // Audio EQ Cookbook — peaking EQ filter
        double clampedGain = Math.Clamp(gainDb, -12.0, 12.0);
        (double freq, double q) = BandDefs[band];

        double A     = Math.Pow(10.0, clampedGain / 40.0);
        double w0    = 2.0 * Math.PI * freq / sampleRate;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);

        double b0 =  1.0 + alpha * A;
        double b1 = -2.0 * cosW0;
        double b2 =  1.0 - alpha * A;
        double a0 =  1.0 + alpha / A;
        double a1 = -2.0 * cosW0;
        double a2 =  1.0 - alpha / A;

        _b0[band] = b0 / a0;
        _b1[band] = b1 / a0;
        _b2[band] = b2 / a0;
        _a1[band] = a1 / a0;
        _a2[band] = a2 / a0;
    }
}
