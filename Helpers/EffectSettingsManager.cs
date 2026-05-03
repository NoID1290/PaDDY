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
            IsEnabled  = effect.IsEnabled,
            Parameters = new Dictionary<string, double>()
        };

        switch (effect)
        {
            case FadeEffect fade:
                cfg.Parameters["FadeInDurationMs"]  = fade.FadeInDurationMs;
                cfg.Parameters["FadeOutDurationMs"] = fade.FadeOutDurationMs;
                break;
            case EchoEffect echo:
                cfg.Parameters["DelayMs"]  = echo.DelayMs;
                cfg.Parameters["Feedback"] = echo.Feedback;
                cfg.Parameters["Mix"]      = echo.Mix;
                break;
            case NoiseGateEffect gate:
                cfg.Parameters["ThresholdDb"] = gate.ThresholdDb;
                cfg.Parameters["AttackMs"]    = gate.AttackMs;
                cfg.Parameters["ReleaseMs"]   = gate.ReleaseMs;
                break;
            case EqualizerEffect eq:
                cfg.Parameters["SubBassDb"]  = eq.SubBassDb;
                cfg.Parameters["BassDb"]     = eq.BassDb;
                cfg.Parameters["MidDb"]      = eq.MidDb;
                cfg.Parameters["PresenceDb"] = eq.PresenceDb;
                cfg.Parameters["TrebleDb"]   = eq.TrebleDb;
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
                if (p.TryGetValue("FadeInDurationMs",  out var fi)) fade.FadeInDurationMs  = fi;
                if (p.TryGetValue("FadeOutDurationMs", out var fo)) fade.FadeOutDurationMs = fo;
                break;
            case EchoEffect echo:
                if (p.TryGetValue("DelayMs",  out var delay)) echo.DelayMs  = delay;
                if (p.TryGetValue("Feedback", out var fb))    echo.Feedback = fb;
                if (p.TryGetValue("Mix",      out var mix))   echo.Mix      = mix;
                break;
            case NoiseGateEffect gate:
                if (p.TryGetValue("ThresholdDb", out var thr)) gate.ThresholdDb = thr;
                if (p.TryGetValue("AttackMs",    out var atk)) gate.AttackMs    = atk;
                if (p.TryGetValue("ReleaseMs",   out var rel)) gate.ReleaseMs   = rel;
                break;
            case EqualizerEffect eq:
                if (p.TryGetValue("SubBassDb",  out var sb))  eq.SubBassDb  = sb;
                if (p.TryGetValue("BassDb",     out var ba))  eq.BassDb     = ba;
                if (p.TryGetValue("MidDb",      out var mi))  eq.MidDb      = mi;
                if (p.TryGetValue("PresenceDb", out var pr))  eq.PresenceDb = pr;
                if (p.TryGetValue("TrebleDb",   out var tr))  eq.TrebleDb   = tr;
                break;
        }
    }
}
