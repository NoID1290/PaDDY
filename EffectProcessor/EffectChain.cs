namespace NoIDSoftwork.EffectProcessor;

/// <summary>
/// Default implementation of <see cref="IEffectChain"/>.
/// </summary>
public sealed class EffectChain : IEffectChain
{
    private readonly List<IAudioEffect> _effects = new();

    public IReadOnlyList<IAudioEffect> Effects => _effects;

    public void Add(IAudioEffect effect) => _effects.Add(effect);

    public void Remove(IAudioEffect effect) => _effects.Remove(effect);

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        // Capture reference once to be safe against concurrent Add/Remove
        var effects = _effects;
        foreach (var effect in effects)
        {
            if (effect.IsEnabled)
                effect.ProcessBuffer(buffer, offset, count, channels, sampleRate);
        }
    }

    public void Reset()
    {
        foreach (var effect in _effects)
            effect.Reset();
    }
}
