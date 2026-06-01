namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Schroeder reverberator: four parallel comb filters feeding two series
/// all-pass filters, per channel. Produces a smooth tail controlled by
/// <see cref="RoomSize"/> (feedback) and <see cref="Damping"/> (high-frequency
/// absorption), blended with the dry signal via <see cref="Mix"/>.
/// </summary>
public sealed class ReverbEffect : IAudioEffect
{
    public string Name => "Reverb";
    public bool IsEnabled { get; set; } = false;

    /// <summary>Tail length 0–1 (comb feedback, default 0.5).</summary>
    public double RoomSize { get; set; } = 0.5;

    /// <summary>High-frequency damping 0–1 (default 0.5).</summary>
    public double Damping { get; set; } = 0.5;

    /// <summary>Wet/dry mix 0–1 (0 = dry, 1 = fully wet, default 0.3).</summary>
    public double Mix { get; set; } = 0.3;

    // Comb/all-pass delay lengths (samples) tuned for ~44.1k, scaled per rate.
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };

    private Channel[] _channels = [];
    private int _lastSampleRate;
    private int _lastChannels;

    public void Reset()
    {
        foreach (var c in _channels) c.Clear();
    }

    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (channels < 1 || count <= 0) return;

        if (_lastSampleRate != sampleRate || _lastChannels != channels)
        {
            _lastSampleRate = sampleRate;
            _lastChannels = channels;
            double scale = sampleRate / 44100.0;
            _channels = new Channel[channels];
            for (int ch = 0; ch < channels; ch++)
                _channels[ch] = new Channel(scale, ch);
        }

        float feedback = 0.7f + 0.28f * (float)Math.Clamp(RoomSize, 0.0, 1.0);
        float damp = (float)Math.Clamp(Damping, 0.0, 1.0);
        float wet = (float)Math.Clamp(Mix, 0.0, 1.0);
        float dry = 1.0f - wet;

        int frames = count / channels;
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = offset + f * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                float input = buffer[baseIdx + ch];
                float reverb = _channels[ch].Process(input, feedback, damp);
                buffer[baseIdx + ch] = dry * input + wet * reverb;
            }
        }
    }

    private sealed class Channel
    {
        private readonly Comb[] _combs;
        private readonly Allpass[] _allpasses;

        public Channel(double scale, int channelIndex)
        {
            // Slight per-channel stereo spread.
            int spread = channelIndex * 23;
            _combs = new Comb[CombTuning.Length];
            for (int i = 0; i < CombTuning.Length; i++)
                _combs[i] = new Comb(Math.Max(1, (int)(CombTuning[i] * scale) + spread));

            _allpasses = new Allpass[AllpassTuning.Length];
            for (int i = 0; i < AllpassTuning.Length; i++)
                _allpasses[i] = new Allpass(Math.Max(1, (int)(AllpassTuning[i] * scale) + spread));
        }

        public float Process(float input, float feedback, float damp)
        {
            float outp = 0f;
            for (int i = 0; i < _combs.Length; i++)
                outp += _combs[i].Process(input, feedback, damp);
            outp /= _combs.Length;

            for (int i = 0; i < _allpasses.Length; i++)
                outp = _allpasses[i].Process(outp);
            return outp;
        }

        public void Clear()
        {
            foreach (var c in _combs) c.Clear();
            foreach (var a in _allpasses) a.Clear();
        }
    }

    private sealed class Comb
    {
        private readonly float[] _buf;
        private int _pos;
        private float _store;

        public Comb(int size) => _buf = new float[size];

        public float Process(float input, float feedback, float damp)
        {
            float outp = _buf[_pos];
            _store = outp * (1f - damp) + _store * damp;
            _buf[_pos] = input + _store * feedback;
            if (++_pos >= _buf.Length) _pos = 0;
            return outp;
        }

        public void Clear()
        {
            Array.Clear(_buf, 0, _buf.Length);
            _store = 0f;
        }
    }

    private sealed class Allpass
    {
        private readonly float[] _buf;
        private int _pos;
        private const float Feedback = 0.5f;

        public Allpass(int size) => _buf = new float[size];

        public float Process(float input)
        {
            float bufout = _buf[_pos];
            float outp = -input + bufout;
            _buf[_pos] = input + bufout * Feedback;
            if (++_pos >= _buf.Length) _pos = 0;
            return outp;
        }

        public void Clear() => Array.Clear(_buf, 0, _buf.Length);
    }
}
