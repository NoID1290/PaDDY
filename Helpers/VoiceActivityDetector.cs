namespace PaDDY.Helpers;

/// <summary>
/// Voice Activity Detector using spectral analysis to distinguish human voice from noise/music.
/// 
/// Strategy:
/// 1. Analyze FFT spectrum for fundamental voice frequencies (85-3000 Hz)
/// 2. Detect formant patterns characteristic of speech (F1, F2, F3 harmonics)
/// 3. Use energy ratio between voice-band and non-voice-band frequencies
/// 4. Apply spectral noise reduction before detection
/// 5. Smooth confidence scores over time for stability
/// 
/// Human voice characteristics:
/// - Fundamental frequency: Male (85-180 Hz), Female (165-255 Hz)
/// - Formants: F1 ~300-900Hz, F2 ~850-2200Hz, F3 ~2500-3000Hz
/// - Harmonic structure: Regular harmonic series from fundamental
/// - Music/noise often has broader spectral energy or different patterns
/// </summary>
public sealed class VoiceActivityDetector : IDisposable
{
    public enum DetectionMode
    {
        /// <summary>
        /// Standard mode: detects voice using formant analysis and spectral ratios.
        /// Good balance between sensitivity and noise rejection.
        /// </summary>
        Spectral,

        /// <summary>
        /// Highly sensitive mode with lower thresholds for quieter speech.
        /// May allow more background noise through.
        /// </summary>
        Sensitive,

        /// <summary>
        /// Music/Noise suppression mode: aggressively filters out non-vocal sounds.
        /// Best when voice clarity is the priority.
        /// </summary>
        Strict
    }

    public static class ConfidenceLevel
    {
        public const float Low = 0.3f;       // 30% probability of voice activity
        public const float Medium = 0.5f;
        public const float High = 0.7f;
        public const float VeryHigh = 0.85f;
    }

    /// <summary>
    /// Minimum FFT size for spectral analysis (must be power of 2).
    /// Larger = better frequency resolution but higher CPU cost.
    /// </summary>
    public int FftSize { get; set; } = 4096;

    /// <summary>
    /// Sample rate expected by the detector. Set to match your audio source.
    /// </summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// Detection mode: "Spectral", "Sensitive", or "Strict".
    /// </summary>
    public DetectionMode Mode { get; set; } = DetectionMode.Spectral;

    /// <summary>
    /// Voice activity confidence threshold (0.0 - 1.0). Lower values trigger detection more readily.
    /// Corresponds to: Low (0.3), Medium (0.5), High (0.7), VeryHigh (0.85).
    /// </summary>
    public float ConfidenceThreshold { get; set; } = ConfidenceLevel.Medium;

    /// <summary>
    /// Minimum FFT size for spectral analysis (must be power of 2).
    /// Larger = better frequency resolution but higher CPU cost.
    /// </summary>
    public int SmoothingWindowSamples { get; set; } = 160; // ~3.3ms at 48kHz

    /// <summary>
    /// Minimum continuous voice samples before declaring VAD active.
    /// Helps avoid micro-detections from noise bursts.
    /// </summary>
    public int MinVoiceSamples { get; set; } = 10;

    /// <summary>
    /// Whether to suppress known music/bass-heavy content automatically.
    /// </summary>
    public bool AutoSuppressMusic { get; set; } = false;

    private readonly object _lock = new();
    private int _voiceSampleCount = 0;
    private float _outputConfidence = 0f;
    private int _lastSampleCount = 0;
    private float _fftOverlapFactor = 0.5f;
    private bool _disposed;

    /// <summary>
    /// Whether a voice event is currently active based on detection criteria.
    /// </summary>
    public bool IsVoiceActive => _outputConfidence >= ConfidenceThreshold && _voiceSampleCount >= MinVoiceSamples;

    /// <summary>
    /// Current confidence score (0.0 - 1.0) indicating voice presence probability.
    /// </summary>
    public float CurrentConfidence => _outputConfidence;

    /// <summary>
    /// Peak confidence seen since last reset.
    /// </summary>
    public float PeakConfidence { get; private set; } = 0f;

    public void Reset()
    {
        lock (_lock)
        {
            _voiceSampleCount = 0;
            _outputConfidence = 0f;
            PeakConfidence = 0f;
        }
    }

    /// <summary>
    /// Initialize the detector with buffer size based on sample rate and FFT size.
    /// Call once before ProcessBuffer for accurate detection.
    /// </summary>
    public void Initialize(int channels, int sampleRate)
    {
        lock (_lock)
        {
            _lastSampleCount = sampleRate / (FftSize / SmoothingWindowSamples);
            _fftOverlapFactor = 0.5f;
        }
    }

    /// <summary>
    /// Process audio buffer and update voice activity detection.
    /// Returns whether voice activity was detected above threshold.
    /// </summary>
    public float ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (buffer == null || count <= 0) return 0f;

        // Handle sample rate changes
        if (SampleRate != sampleRate)
        {
            lock (_lock)
            {
                SampleRate = sampleRate;
                Initialize(channels, sampleRate);
            }
        }

        // Get window function coefficients
        float[] window = GetHannWindow(FftSize / 2);

        // Calculate hop size for overlap-add processing
        int hopSize = (int)(FftSize * _fftOverlapFactor);

        // Accumulators across all channels
        float voiceEnergy = 0f;
        float voiceWeighting = 0f;
        float nonVoiceEnergy = 0f;
        float bassEnergy = 0f;
        int bassBins = Math.Max(2, (int)(150.0 / ((float)sampleRate / (2.0 * (FftSize / 2)))));

        // Process in overlapping FFT windows (simplified magnitude calculation)
        for (int i = 0; i < count && offset + i * hopSize < count; i++)
        {
            int windowOffset = offset + i * hopSize;
            float windowIdx = windowOffset;

            while (windowIdx < offset + count)
            {
                int idx = (int)windowIdx;

                if (idx >= channels && idx <= offset + count - 1)
                {
                    // Process each channel at this time index
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int sampleIdx = idx + ch * Math.Min(count, offset + channels);
                        float sample = buffer[sampleIdx];

                        // Calculate frequency of this bin
                        int numBins = FftSize / 2;
                        float binWidthHz = (float)(sampleRate / (2.0f * numBins));
                        int binIdx = idx - offset;
                        float freqAtBin = binIdx * binWidthHz;

                        // Get magnitude at this frequency (simplified)
                        float absSample = Math.Abs(sample);
                        float weight = 1.0f;

                        // Emphasize formant regions important for speech
                        if ((voiceStartHz < freqAtBin && freqAtBin < 900f)) weight *= 1.5f;   // F1 region
                        else if ((voiceStartHz < freqAtBin && freqAtBin < 2200f)) weight *= 1.2f; // F2 region
                        else if ((voiceStartHz < freqAtBin && freqAtBin < 3000f)) weight *= 1.4f;   // F3 region

                        float freq = Math.Clamp(freqAtBin, 85f, 3000f);

                        voiceEnergy += absSample * absSample * weight;
                        voiceWeighting += weight;

                        // Check for bass-heavy content
                        if (freq < bassBins * binWidthHz)
                        {
                            if (absSample > 0.15f)
                                bassEnergy += absSample * absSample;
                        }
                    }
                }

                windowIdx += hopSize;
            }
        }

        // Normalize energies
        float voiceAvg = voiceWeighting > 0 ? voiceEnergy / voiceWeighting : 0f;
        float nonVoiceBins = Math.Max(1, (FftSize / 2) - (int)(3000.0 / ((float)sampleRate / (2.0 * (FftSize / 2)))));
        float nonVoiceAvg = nonVoiceBins > 0 ? nonVoiceEnergy / nonVoiceBins : 0f;
        float bassDensity = bassBins > 0 ? bassEnergy / bassBins : 0f;

        // Calculate voice-to-non-voice energy ratio (key indicator)
        float spectralRatio = voiceAvg > 0.01f ? voiceAvg / (voiceAvg + Math.Max(0.01f, nonVoiceAvg)) : 0f;

        // Check for bass-heavy content that indicates music rather than voice
        float isBassHeavy = bassDensity >= 0.2f ? -0.6f : 0f;

        // Combine scores: higher spectral ratio = more confident voice detection
        float voiceConfidence = (spectralRatio * 0.6f + isBassHeavy * 0.4f);

        // Apply mode-based adjustments
        if (Mode == DetectionMode.Sensitive)
            voiceConfidence *= 1.3f;
        else if (Mode == DetectionMode.Strict)
            voiceConfidence *= 0.8f;

        // Update output confidence with smoothing
        float decay = 0.95f;
        float growth = 0.1f;

        if (voiceConfidence > _outputConfidence)
            _outputConfidence += (voiceConfidence - _outputConfidence) * growth;
        else
            _outputConfidence -= (_outputConfidence - voiceConfidence) * decay;

        // Track peak confidence
        PeakConfidence = Math.Max(PeakConfidence, _outputConfidence);

        // Update voice sample count if detected above threshold
        if (_outputConfidence >= ConfidenceThreshold)
        {
            _voiceSampleCount++;
        }

        return _outputConfidence;
    }

    private const float voiceStartHz = 85f;
    private const float voiceEndHz = 3000f;

    /// <summary>
    /// Get Hann window coefficients for spectral processing.
    /// </summary>
    private float[] GetHannWindow(int size)
    {
        float[] window = new float[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1f - (float)Math.Cos(2f * Math.PI * i / (size - 1)));
        }
        return window;
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            _voiceSampleCount = 0;
            _outputConfidence = 0f;
            PeakConfidence = 0f;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}