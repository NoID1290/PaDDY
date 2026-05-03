namespace PaDDY.Models;

/// <summary>
/// Root settings object for all effect chains.
/// GlobalChain applies to all clips; PerClipChains are keyed by RecordingId.
/// </summary>
public class EffectSettings
{
    /// <summary>
    /// Global effect chain (Noise Gate, Echo, EQ).
    /// Applied during real-time capture when enabled.
    /// </summary>
    public EffectChainConfig GlobalChain { get; set; } = new();

    /// <summary>
    /// Per-clip override chains, keyed by <see cref="RecordingEntry.RecordingId"/>.
    /// Each entry may include a Fade effect in addition to the global effects.
    /// </summary>
    public Dictionary<string, EffectChainConfig> PerClipChains { get; set; } = new();
}
