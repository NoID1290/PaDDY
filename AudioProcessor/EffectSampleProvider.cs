using NAudio.Wave;
using NoIDSoftwork.EffectProcessor;

namespace NoIDSoftwork.AudioProcessor;

/// <summary>
/// Wraps an <see cref="ISampleProvider"/> and runs its output through an
/// <see cref="IEffectChain"/> on every Read call. The chain can be hot-swapped
/// at any time without stopping playback.
/// </summary>
public sealed class EffectSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private IEffectChain _chain;

    public EffectSampleProvider(ISampleProvider source, IEffectChain chain)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _chain  = chain  ?? throw new ArgumentNullException(nameof(chain));
    }

    /// <summary>Hot-swap the active effect chain.</summary>
    public IEffectChain Chain
    {
        get => _chain;
        set => _chain = value ?? throw new ArgumentNullException(nameof(value));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0)
            _chain.ProcessBuffer(buffer, offset, read,
                _source.WaveFormat.Channels, _source.WaveFormat.SampleRate);
        return read;
    }
}
