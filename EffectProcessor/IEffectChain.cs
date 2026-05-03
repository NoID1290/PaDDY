namespace NoIDSoftwork.EffectProcessor;

/// <summary>
/// An ordered chain of <see cref="IAudioEffect"/> instances applied sequentially.
/// </summary>
public interface IEffectChain
{
    /// <summary>Ordered list of effects in this chain.</summary>
    IReadOnlyList<IAudioEffect> Effects { get; }

    /// <summary>Append an effect to the end of the chain.</summary>
    void Add(IAudioEffect effect);

    /// <summary>Remove an effect from the chain.</summary>
    void Remove(IAudioEffect effect);

    /// <summary>
    /// Run all enabled effects in order on the given buffer segment.
    /// </summary>
    void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate);

    /// <summary>Reset all effects in the chain.</summary>
    void Reset();
}
