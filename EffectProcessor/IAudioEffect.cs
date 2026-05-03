namespace NoIDSoftwork.EffectProcessor;

/// <summary>
/// An audio effect that processes interleaved float PCM samples in-place.
/// Samples are channel-interleaved: [L0, R0, L1, R1, ...] for stereo.
/// </summary>
public interface IAudioEffect
{
    /// <summary>Human-readable effect name.</summary>
    string Name { get; }

    /// <summary>Whether this effect is applied during processing.</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Process <paramref name="count"/> interleaved float samples in-place.
    /// </summary>
    /// <param name="buffer">Interleaved float sample buffer.</param>
    /// <param name="offset">Start index within the buffer.</param>
    /// <param name="count">Number of samples to process.</param>
    /// <param name="channels">Channel count (1 = mono, 2 = stereo, etc.).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate);

    /// <summary>Reset all internal state (delay lines, envelope followers, frame counters).</summary>
    void Reset();
}
