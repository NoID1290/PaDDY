namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Waveshaping distortion / overdrive. Applies a hyperbolic-tangent soft-clip
/// shaped by <see cref="Drive"/>, blended with the dry signal via <see cref="Mix"/>
/// and scaled by <see cref="OutputLevel"/>.
/// </summary>
public sealed class DistortionEffect : IAudioEffect
{
    public string Name => "Distortion";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Pre-gain drive amount 1–50 (default 8). Higher = more clipping.</summary>
    public double Drive { get; set; } = 8.0;

    /// <summary>Wet/dry mix 0–1 (0 = dry, 1 = fully distorted, default 0.6).</summary>
    public double Mix { get; set; } = 0.6;

    /// <summary>Output level 0–1 applied after shaping (default 0.8).</summary>
    public double OutputLevel { get; set; } = 0.8;

    public void Reset() { /* stateless */ }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (count <= 0) return;

        float drive = (float)Math.Clamp(Drive, 1.0, 50.0);
        float wet = (float)Math.Clamp(Mix, 0.0, 1.0);
        float dry = 1.0f - wet;
        float level = (float)Math.Clamp(OutputLevel, 0.0, 1.0);

        // Normalise so the shaper's output stays near unity at the chosen drive.
        float norm = 1.0f / MathF.Tanh(drive);

        int end = offset + count;
        for (int i = offset; i < end; i++)
        {
            float input = buffer[i];
            float shaped = MathF.Tanh(input * drive) * norm;
            buffer[i] = (dry * input + wet * shaped) * level;
        }
    }
}
