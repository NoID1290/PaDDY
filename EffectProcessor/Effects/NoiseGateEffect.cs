namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Envelope-follower noise gate. Attenuates signals that fall below a
/// configurable RMS threshold, with smooth attack/release transitions.
/// Uses a 1-pole IIR envelope follower — no FFT required.
/// </summary>
public sealed class NoiseGateEffect : IAudioEffect
{
    public string Name => "Noise Gate";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Gate-open threshold in dBFS (default -40).</summary>
    public double ThresholdDb { get; set; } = -40.0;

    /// <summary>Attack time in milliseconds — how fast the gate opens (default 10).</summary>
    public double AttackMs { get; set; } = 10.0;

    /// <summary>Release time in milliseconds — how fast the gate closes (default 100).</summary>
    public double ReleaseMs { get; set; } = 100.0;

    private float[] _gainState  = [];
    private float[] _envelope   = [];
    private int     _lastSampleRate;
    private int     _lastChannels;

    public void Reset()
    {
        Array.Fill(_gainState, 1.0f);
        Array.Clear(_envelope, 0, _envelope.Length);
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        if (_lastSampleRate != sampleRate || _lastChannels != channels)
        {
            _lastSampleRate = sampleRate;
            _lastChannels   = channels;
            _gainState      = new float[channels];
            _envelope       = new float[channels];
            Array.Fill(_gainState, 1.0f);
        }

        // 1-pole IIR coefficients
        float attackCoeff  = AttackMs  > 0 ? (float)Math.Exp(-1.0 / (sampleRate * AttackMs  / 1000.0)) : 0f;
        float releaseCoeff = ReleaseMs > 0 ? (float)Math.Exp(-1.0 / (sampleRate * ReleaseMs / 1000.0)) : 0f;

        // dBFS threshold → linear amplitude
        float thresholdLinear = (float)Math.Pow(10.0, ThresholdDb / 20.0);

        for (int i = 0; i < count; i += channels)
        {
            int sampleIdx = offset + i;
            for (int ch = 0; ch < channels; ch++)
            {
                float sample    = buffer[sampleIdx + ch];
                float absSample = Math.Abs(sample);

                // Leaky peak envelope follower
                _envelope[ch] = absSample > _envelope[ch]
                    ? absSample
                    : releaseCoeff * _envelope[ch];

                // Target: gate open (1) above threshold, closed (0) below
                float target = _envelope[ch] >= thresholdLinear ? 1.0f : 0.0f;

                // Smooth gain towards target using attack or release coefficient
                float coeff = target > _gainState[ch] ? attackCoeff : releaseCoeff;
                _gainState[ch] = coeff * _gainState[ch] + (1f - coeff) * target;

                buffer[sampleIdx + ch] = sample * _gainState[ch];
            }
        }
    }
}
