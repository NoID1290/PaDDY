using System.IO;
using MessagePack;
using MessagePack.Resolvers;
using NoIDSoftwork.EffectProcessor;
using NoIDSoftwork.EffectProcessor.Effects;
using PaDDY.Models;

namespace PaDDY.Helpers;

/// <summary>
/// Loads and saves <see cref="EffectSettings"/> and converts between
/// the serializable <see cref="EffectChainConfig"/> format and live
/// <see cref="IEffectChain"/> instances.
/// </summary>
internal static class EffectSettingsManager
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    // ── Persistence ──────────────────────────────────────────────────────────

    public static EffectSettings Load()
    {
        try
        {
            AppDataPaths.EnsureAppDataRoot();
            if (File.Exists(AppDataPaths.EffectSettingsPath))
            {
                var bytes = File.ReadAllBytes(AppDataPaths.EffectSettingsPath);
                var s = MessagePackSerializer.Deserialize<EffectSettings>(bytes, SerializerOptions);
                if (s != null) return s;
            }
        }
        catch { /* fall through to defaults */ }

        return new EffectSettings();
    }

    public static void Save(EffectSettings settings)
    {
        try
        {
            AppDataPaths.EnsureAppDataRoot();
            var bytes = MessagePackSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllBytes(AppDataPaths.EffectSettingsPath, bytes);
        }
        catch { /* non-critical */ }
    }

    // ── Live chain → Config ──────────────────────────────────────────────────

    public static EffectChainConfig ToConfig(IEffectChain chain)
    {
        var config = new EffectChainConfig();
        foreach (var effect in chain.Effects)
            config.Effects.Add(EffectToConfig(effect));
        return config;
    }

    // ── Config → Live chain ──────────────────────────────────────────────────

    public static void ApplyConfig(IEffectChain chain, EffectChainConfig config)
    {
        foreach (var effect in chain.Effects)
        {
            var cfg = config.Effects.Find(e => e.EffectType == effect.GetType().Name);
            if (cfg != null)
                ApplyConfigToEffect(effect, cfg);
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private static EffectConfig EffectToConfig(IAudioEffect effect)
    {
        var cfg = new EffectConfig
        {
            EffectType = effect.GetType().Name,
            IsEnabled = effect.IsEnabled,
            Parameters = new Dictionary<string, double>()
        };

        switch (effect)
        {
            case FadeEffect fade:
                cfg.Parameters["FadeInDurationMs"] = fade.FadeInDurationMs;
                cfg.Parameters["FadeOutDurationMs"] = fade.FadeOutDurationMs;
                break;
            case EchoEffect echo:
                cfg.Parameters["DelayMs"] = echo.DelayMs;
                cfg.Parameters["Feedback"] = echo.Feedback;
                cfg.Parameters["Mix"] = echo.Mix;
                break;
            case CompressorEffect comp:
                cfg.Parameters["ThresholdDb"] = comp.ThresholdDb;
                cfg.Parameters["Ratio"] = comp.Ratio;
                cfg.Parameters["AttackMs"] = comp.AttackMs;
                cfg.Parameters["ReleaseMs"] = comp.ReleaseMs;
                cfg.Parameters["MakeupDb"] = comp.MakeupDb;
                break;
            case DistortionEffect dist:
                cfg.Parameters["Drive"] = dist.Drive;
                cfg.Parameters["Mix"] = dist.Mix;
                cfg.Parameters["OutputLevel"] = dist.OutputLevel;
                break;
            case ReverbEffect rev:
                cfg.Parameters["RoomSize"] = rev.RoomSize;
                cfg.Parameters["Damping"] = rev.Damping;
                cfg.Parameters["Mix"] = rev.Mix;
                break;
            case NoiseGateEffect gate:
                cfg.Parameters["ThresholdDb"] = gate.ThresholdDb;
                cfg.Parameters["AttackMs"] = gate.AttackMs;
                cfg.Parameters["ReleaseMs"] = gate.ReleaseMs;
                break;
            case EqualizerEffect eq:
                cfg.Parameters["SubBassDb"] = eq.SubBassDb;
                cfg.Parameters["BassDb"] = eq.BassDb;
                cfg.Parameters["MidDb"] = eq.MidDb;
                cfg.Parameters["PresenceDb"] = eq.PresenceDb;
                cfg.Parameters["TrebleDb"] = eq.TrebleDb;
                break;
            case PitchShiftEffect pitch:
                cfg.Parameters["PitchSemitones"] = pitch.PitchSemitones;
                cfg.Parameters["GrainSizeMs"] = pitch.GrainSizeMs;
                cfg.Parameters["Mix"] = pitch.Mix;
                break;
        }

        return cfg;
    }

    private static void ApplyConfigToEffect(IAudioEffect effect, EffectConfig cfg)
    {
        effect.IsEnabled = cfg.IsEnabled;
        var p = cfg.Parameters;

        switch (effect)
        {
            case FadeEffect fade:
                if (p.TryGetValue("FadeInDurationMs", out var fi)) fade.FadeInDurationMs = fi;
                if (p.TryGetValue("FadeOutDurationMs", out var fo)) fade.FadeOutDurationMs = fo;
                break;
            case EchoEffect echo:
                if (p.TryGetValue("DelayMs", out var delay)) echo.DelayMs = delay;
                if (p.TryGetValue("Feedback", out var fb)) echo.Feedback = fb;
                if (p.TryGetValue("Mix", out var mix)) echo.Mix = mix;
                break;
            case CompressorEffect comp:
                if (p.TryGetValue("ThresholdDb", out var cthr)) comp.ThresholdDb = cthr;
                if (p.TryGetValue("Ratio", out var ratio)) comp.Ratio = ratio;
                if (p.TryGetValue("AttackMs", out var catk)) comp.AttackMs = catk;
                if (p.TryGetValue("ReleaseMs", out var crel)) comp.ReleaseMs = crel;
                if (p.TryGetValue("MakeupDb", out var mk)) comp.MakeupDb = mk;
                break;
            case DistortionEffect dist:
                if (p.TryGetValue("Drive", out var drive)) dist.Drive = drive;
                if (p.TryGetValue("Mix", out var dmix)) dist.Mix = dmix;
                if (p.TryGetValue("OutputLevel", out var lvl)) dist.OutputLevel = lvl;
                break;
            case ReverbEffect rev:
                if (p.TryGetValue("RoomSize", out var room)) rev.RoomSize = room;
                if (p.TryGetValue("Damping", out var damp)) rev.Damping = damp;
                if (p.TryGetValue("Mix", out var rmix)) rev.Mix = rmix;
                break;
            case NoiseGateEffect gate:
                if (p.TryGetValue("ThresholdDb", out var thr)) gate.ThresholdDb = thr;
                if (p.TryGetValue("AttackMs", out var atk)) gate.AttackMs = atk;
                if (p.TryGetValue("ReleaseMs", out var rel)) gate.ReleaseMs = rel;
                break;
            case EqualizerEffect eq:
                if (p.TryGetValue("SubBassDb", out var sb)) eq.SubBassDb = sb;
                if (p.TryGetValue("BassDb", out var ba)) eq.BassDb = ba;
                if (p.TryGetValue("MidDb", out var mi)) eq.MidDb = mi;
                if (p.TryGetValue("PresenceDb", out var pr)) eq.PresenceDb = pr;
                if (p.TryGetValue("TrebleDb", out var tr)) eq.TrebleDb = tr;
                break;
            case PitchShiftEffect pitch:
                if (p.TryGetValue("PitchSemitones", out var psem)) pitch.PitchSemitones = psem;
                if (p.TryGetValue("GrainSizeMs", out var pgs)) pitch.GrainSizeMs = pgs;
                if (p.TryGetValue("Mix", out var pmix)) pitch.Mix = pmix;
                break;
        }
    }
}
