namespace PaDDY.Models;

/// <summary>
/// Root settings object for all effect chains.
/// GlobalChain applies to all clips; PerClipChains are keyed by RecordingId.
/// 
/// Voice Detection Settings:
/// - VoiceActivityDetector uses spectral analysis to distinguish voice from noise/music
/// - Configured separately from the VAD (Voice Activity Detection) algorithm in AppSettings
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

    /// <summary>
    /// Voice Activity Detection settings for filtering noise/music from voice.
    /// Uses spectral analysis focusing on human vocal ranges (85-3000 Hz).
    /// </summary>
    public VoiceDetectionConfig VoiceDetection { get; set; } = new();
}

/// <summary>
/// Configuration for voice activity detection and voice-specific effects.
/// </summary>
public sealed class VoiceDetectionConfig
{
    /// <summary>
    /// Enable voice-only filtering (removes music/noise from non-voice content).
    /// Uses spectral analysis of human vocal ranges.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Detection mode: Spectral, Sensitive, or Strict.
    /// See VoiceActivityDetector enum for details.
    /// </summary>
    public string Mode { get; set; } = "Spectral";

    /// <summary>
    /// Confidence threshold for declaring voice active (0.0 - 1.0).
    /// Default: Medium (0.5) which corresponds to ~50% confidence.
    /// Set to 0.3f for Low (~30%), 0.7f for High (~70%), 0.85f for VeryHigh (~85%).
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Automatically suppress music/bass-heavy content that isn't voice-like.
    /// </summary>
    public bool AutoSuppressMusic { get; set; } = false;

    /// <summary>
    /// Whether to apply voice detection during real-time capture.
    /// When true, audio is processed through the voice detector in addition to standard VAD.
    /// </summary>
    public bool ApplyDuringCapture { get; set; } = false;

    /// <summary>
    /// Minimum confidence boost to add when voice is detected.
    /// Helps prioritize voice segments during capture decisions.
    /// </summary>
    public float VoiceDetectionBoost { get; set; } = 0f;
}