using NoIDSoftwork.EffectProcessor.Effects;

namespace NoIDSoftwork.EffectProcessor;

/// <summary>
/// Factory for creating pre-populated effect chains.
/// </summary>
public static class EffectChainFactory
{
    /// <summary>
    /// Creates a global effect chain: Noise Gate → Echo → Equalizer.
    /// Fade is intentionally omitted — it is per-clip only.
    /// All effects are disabled by default.
    /// </summary>
    public static IEffectChain CreateGlobal()
    {
        var chain = new EffectChain();
        chain.Add(new NoiseGateEffect());
        chain.Add(new EchoEffect());
        chain.Add(new EqualizerEffect());
        return chain;
    }

    /// <summary>
    /// Creates a per-clip effect chain: Fade → Noise Gate → Echo → Equalizer.
    /// All effects are disabled by default.
    /// </summary>
    public static IEffectChain CreatePerClip()
    {
        var chain = new EffectChain();
        chain.Add(new FadeEffect());
        chain.Add(new NoiseGateEffect());
        chain.Add(new EchoEffect());
        chain.Add(new EqualizerEffect());
        return chain;
    }
}
