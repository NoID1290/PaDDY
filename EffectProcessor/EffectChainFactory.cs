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
    /// Creates a per-clip effect chain: Fade → Noise Gate → Voice Gate (optional) → Pitch Shift → Compressor → Distortion → Echo → Reverb → Equalizer → Remaster.
    /// All effects are disabled by default.
    /// </summary>
    public static IEffectChain CreatePerClip()
    {
        var chain = new EffectChain();
        chain.Add(new FadeEffect());
        chain.Add(new NoiseGateEffect());
        chain.Add(new VoiceGateEffect());
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
    /// Creates a minimal voice-only processing chain optimized for voice clarity.
    /// Uses Voice Gate + Noise Gate for best noise/music suppression while preserving voice.
    /// </summary>
    public static IEffectChain CreateVoiceOnly()
    {
        var chain = new EffectChain();
        chain.Add(new VoiceGateEffect 
        { 
            IsEnabled = true,
            Mode = "Spectral",
            Threshold = 0.5f,
            SuppressMusic = false
        });
        chain.Add(new NoiseGateEffect 
        { 
            IsEnabled = true,
            ThresholdDb = -40.0,
            AttackMs = 10.0,
            ReleaseMs = 100.0
        });
        return chain;
    }
}