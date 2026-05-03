namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Feedback echo/delay effect with per-channel circular delay buffers.
/// </summary>
public sealed class EchoEffect : IAudioEffect
{
    public string Name => "Echo";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Delay time in milliseconds (default 200).</summary>
    public double DelayMs { get; set; } = 200.0;

    /// <summary>
    /// Feedback amount 0–1 (default 0.3).
    /// Values above ~0.9 risk runaway and are clamped to 0.99.
    /// </summary>
    public double Feedback { get; set; } = 0.3;

    /// <summary>Wet/dry mix 0–1 (0 = fully dry, 1 = fully wet, default 0.4).</summary>
    public double Mix { get; set; } = 0.4;

    private float[][] _delayBuffers = [];
    private int[] _writePositions = [];
    private int _delaySamples;
    private int _lastSampleRate;
    private int _lastChannels;

    public void Reset()
    {
        foreach (var buf in _delayBuffers)
            Array.Clear(buf, 0, buf.Length);
        Array.Clear(_writePositions, 0, _writePositions.Length);
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        int delaySamples = Math.Max(1, (int)(sampleRate * DelayMs / 1000.0));

        // Reallocate if format or delay length changed
        if (_lastSampleRate != sampleRate || _lastChannels != channels || _delaySamples != delaySamples)
        {
            _delaySamples = delaySamples;
            _lastSampleRate = sampleRate;
            _lastChannels = channels;
            _delayBuffers = new float[channels][];
            _writePositions = new int[channels];
            for (int ch = 0; ch < channels; ch++)
                _delayBuffers[ch] = new float[delaySamples + 1];
        }

        float feedback = (float)Math.Clamp(Feedback, 0.0, 0.99);
        float wet = (float)Math.Clamp(Mix, 0.0, 1.0);
        float dry = 1.0f - wet;
        int bufLen = _delayBuffers[0].Length;

        for (int i = 0; i < count; i += channels)
        {
            int sampleIdx = offset + i;
            for (int ch = 0; ch < channels; ch++)
            {
                float[] delayBuf = _delayBuffers[ch];
                int writePos = _writePositions[ch];
                int readPos = (writePos - _delaySamples + bufLen) % bufLen;

                float delayed = delayBuf[readPos];
                float input = buffer[sampleIdx + ch];

                // Write feedback-mixed signal into delay line
                delayBuf[writePos] = input + feedback * delayed;

                // Output: dry input + wet delayed
                buffer[sampleIdx + ch] = dry * input + wet * delayed;

                _writePositions[ch] = (writePos + 1) % bufLen;
            }
        }
    }
}
