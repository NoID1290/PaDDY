namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Applies a linear fade-in at the start and/or fade-out at the end of a clip.
/// <para>
/// This effect is <b>per-clip only</b> — do not add it to a global chain.
/// </para>
/// <para>
/// Fade-out requires <see cref="TotalFrames"/> to be set by the caller before
/// processing (e.g. total PCM frames of the clip). Set to -1 (default) to skip
/// fade-out (unknown length, e.g. live capture).
/// </para>
/// </summary>
public sealed class FadeEffect : IAudioEffect
{
    public string Name => "Fade In / Fade Out";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Fade-in duration in milliseconds (0 = no fade-in, default 500).</summary>
    public double FadeInDurationMs { get; set; } = 500.0;

    /// <summary>Fade-out duration in milliseconds (0 = no fade-out, default 500).</summary>
    public double FadeOutDurationMs { get; set; } = 500.0;

    /// <summary>
    /// Total PCM frame count of the clip (one frame = one sample per channel).
    /// Set before playback or export. -1 = unknown, fade-out is skipped.
    /// </summary>
    public long TotalFrames { get; set; } = -1;

    private long _framePosition;

    public void Reset()
    {
        _framePosition = 0;
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        int frames = count / channels;
        double fadeInFrames  = sampleRate * FadeInDurationMs  / 1000.0;
        double fadeOutFrames = TotalFrames > 0
            ? sampleRate * FadeOutDurationMs / 1000.0
            : 0.0;

        for (int f = 0; f < frames; f++)
        {
            long pos = _framePosition + f;
            float gain = 1.0f;

            // Fade-in ramp
            if (FadeInDurationMs > 0 && fadeInFrames > 0 && pos < (long)fadeInFrames)
                gain = Math.Min(1.0f, (float)(pos / fadeInFrames));

            // Fade-out ramp (only when TotalFrames is known)
            if (FadeOutDurationMs > 0 && TotalFrames > 0 && fadeOutFrames > 0)
            {
                long fromEnd = TotalFrames - pos;
                if (fromEnd < (long)fadeOutFrames)
                    gain = Math.Min(gain, Math.Max(0.0f, (float)(fromEnd / fadeOutFrames)));
            }

            int idx = offset + f * channels;
            for (int ch = 0; ch < channels; ch++)
                buffer[idx + ch] *= gain;
        }

        _framePosition += frames;
    }
}
