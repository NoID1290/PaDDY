using NoIDSoftwork.EffectProcessor.Effects;

namespace NoIDSoftwork.EffectProcessor;

/// <summary>
/// Factory for creating pre-populated effect chains.
/// </summary>
public static class EffectChainFactory
{
    /// <summary>
    /// Creates a global effect chain: Noise Gate → Compressor → Distortion → Echo → Reverb → Equalizer.
    /// Fade is intentionally omitted — it is per-clip only.
    /// All effects are disabled by default.
    /// </summary>
    public static IEffectChain CreateGlobal()
    {
        var chain = new EffectChain();
        chain.Add(new NoiseGateEffect());
        chain.Add(new PitchShiftEffect());
        chain.Add(new CompressorEffect());
        chain.Add(new DistortionEffect());
        chain.Add(new EchoEffect());
        chain.Add(new ReverbEffect());
        chain.Add(new EqualizerEffect());
        chain.Add(new RemasterEffect());
        return chain;
    }

    /// <summary>
    /// Creates a per-clip effect chain: Fade → Noise Gate → Pitch Shift → Compressor → Distortion → Echo → Reverb → Equalizer → Remaster.
    /// All effects are disabled by default.
    /// </summary>
    public static IEffectChain CreatePerClip()
    {
        var chain = new EffectChain();
        chain.Add(new FadeEffect());
        chain.Add(new NoiseGateEffect());
        chain.Add(new PitchShiftEffect());
        chain.Add(new CompressorEffect());
        chain.Add(new DistortionEffect());
        chain.Add(new EchoEffect());
        chain.Add(new ReverbEffect());
        chain.Add(new EqualizerEffect());
        chain.Add(new RemasterEffect());
        return chain;
    }
}
