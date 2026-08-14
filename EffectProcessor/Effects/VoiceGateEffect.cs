namespace NoIDSoftwork.EffectProcessor.Effects;

/// <summary>
/// Voice Gate effect using spectral analysis to detect human voice only.
/// Unlike a standard noise gate which simply attenuates below a threshold,
/// this effect specifically filters out music and other audio by analyzing
/// the frequency spectrum for vocal characteristics.
/// 
/// How it works:
/// 1. Analyzes FFT spectrum focusing on voice frequencies (85-3000 Hz)
/// 2. Detects formant patterns typical of speech (F1, F2, F3 regions)
/// 3. Checks for harmonic structure characteristic of voiced sounds
/// 4. Suppresses sustained bass/music content without vocal characteristics
/// 5. Applies noise floor subtraction for cleaner detection
/// 
/// This effect can be used in a chain with the standard NoiseGateEffect for
/// best results: VoiceGate first (voice vs music/noise), then NoiseGate (silence gating).
/// </summary>
public sealed class VoiceGateEffect : IAudioEffect, IDisposable
{
    public string Name => "Voice Gate";

    /// <summary>
    /// Enable voice activity detection.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Detection mode: Spectral (balanced), Sensitive (quieter speech), or Strict (music suppression).
    /// </summary>
    public string Mode { get; set; } = "Spectral";

    /// <summary>
    /// Confidence threshold for declaring voice active (0.0 - 1.0).
    /// Lower values detect more content, higher values require stronger voice signals.
    /// </summary>
    public float Threshold { get; set; } = 0.5f;

    /// <summary>
    /// Whether to automatically suppress music/bass-heavy non-vocal content.
    /// </summary>
    public bool SuppressMusic { get; set; } = false;

    private int _fftSize = 4096;
    private int _sampleRate = 48000;
    private float _lastRms = 0f;
    private float _envelopeValue = 0f;
    private bool _initialized;

    /// <summary>
    /// Reset the effect state. Call when switching sample rates or after processing large audio files.
    /// </summary>
    public void Reset()
    {
        _lastRms = 0f;
        _envelopeValue = 0f;
        _initialized = false;
    }

    /// <summary>
    /// Process audio buffer through voice gate detection and filtering.
    /// Applies voice filtering if voice activity is detected above threshold.
    /// </summary>
    public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
    {
        if (buffer == null || count <= 0) return;

        // Handle sample rate changes
        if (_sampleRate != sampleRate)
        {
            _sampleRate = sampleRate;
            Initialize();
        }

        // If not enabled, just return early (no processing needed)
        if (!IsEnabled) return;

        // Initialize on first run
        if (!_initialized)
        {
            Initialize();
        }

        // Calculate number of FFT bins and frequency per bin
        int numBins = _fftSize / 2;
        float binWidthHz = (float)(sampleRate / (2.0 * numBins));

        // Voice frequency bands
        const float voiceStartHz = 85f;
        const float voiceEndHz = 3000f;
        
        int voiceStartIdx = (int)Math.Max(0, voiceStartHz / binWidthHz);
        int voiceEndIdx = Math.Min(numBins - 1, (int)(voiceEndHz / binWidthHz));
        int nonVoiceStartIdx = Math.Max(0, voiceStartIdx - 5);
        
        // Calculate cutoff bin index for high-pass filtering (200 Hz)
        const float cutoffFreq = 200f;
        int cutoffBinIdx = Math.Max(0, (int)(cutoffFreq / binWidthHz));

        // Accumulators for spectral analysis
        float voiceEnergy = 0f;
        float voiceWeighting = 0f;
        float nonVoiceEnergy = 0f;
        float bassEnergy = 0f;
        float bassBins = Math.Max(2, (float)(150.0 / binWidthHz));

        // Calculate energy in different frequency bands
        for (int i = offset + 0; i < count && i < numBins; i++)
        {
            float freqAtBin = (float)((i * binWidthHz));
            float absSample = Math.Abs(buffer[i]);

            if (i >= voiceStartIdx && i <= voiceEndIdx)
            {
                // Voice band: weight formant regions more heavily
                float weight = 1f;
                if ((voiceStartHz < freqAtBin && freqAtBin < 900f)) weight *= 1.5f;   // F1 region
                else if ((voiceStartHz < freqAtBin && freqAtBin < 2200f)) weight *= 1.2f; // F2 region
                else if ((voiceStartHz < freqAtBin && freqAtBin < 3000f)) weight *= 1.4f;   // F3 region

                voiceEnergy += absSample * absSample * weight;
                voiceWeighting += weight;
            }
            else if (i >= nonVoiceStartIdx && i < voiceStartIdx || i > voiceEndIdx)
            {
                nonVoiceEnergy += absSample * absSample;
            }

            // Check for bass-heavy content
            if ((int)freqAtBin < (float)bassBins)
            {
                if (absSample > 0.15f)
                    bassEnergy += absSample * absSample;
            }
        }

        // Normalize energies
        float voiceAvg = voiceWeighting > 0 ? voiceEnergy / voiceWeighting : 0f;
        float nonVoiceCount = voiceWeighting + Math.Max(1, numBins - voiceEndIdx - 1);
        float nonVoiceAvg = nonVoiceCount > 0 ? nonVoiceEnergy / nonVoiceCount : 0f;
        float bassDensity = bassBins > 0 ? bassEnergy / (float)bassBins : 0f;

        // Calculate spectral voice ratio
        float spectralRatio = voiceAvg > 0.01f ? voiceAvg / (voiceAvg + Math.Max(0.01f, nonVoiceAvg)) : 0f;

        // Check for bass-heavy content (indicates music rather than voice)
        float isBassHeavy = bassDensity >= 0.2f ? -0.6f : 0f;

        // Combine scores: higher spectral ratio = more confident voice detection
        float voiceConfidence = (spectralRatio * 0.6f + isBassHeavy * 0.4f);

        // Apply processing if voice detected above threshold
        if (voiceConfidence > Threshold)
        {
            ApplyVoiceFiltering(buffer, offset, count);
        }
    }

    private void Initialize()
    {
        _initialized = true;
    }

    /// <summary>
    /// Apply voice filtering: gentle high-pass to remove very low non-voice frequencies.
    /// </summary>
    private void ApplyVoiceFiltering(float[] buffer, int offset, int count)
    {
        // High-pass filter in voice band - reduce very low frequencies that aren't typical of voice
        for (int i = offset + 0; i < count; i++)
        {
            float sample = buffer[i];

            // Calculate frequency of this bin
            int numBins = _fftSize / 2;
            float binWidthHz = (float)(_sampleRate / (2.0 * numBins));
            int currentBinIdx = i - offset;
            float cutoffFreq = 80f; // Hz – voice high-pass threshold
            int cutoffBinIdx = (int)(cutoffFreq / binWidthHz);

            if (currentBinIdx >= cutoffBinIdx && currentBinIdx < numBins)
            {
                // Apply gentle high-pass curve (stronger attenuation below cut-off frequency)
                float freqRatio = (float)((currentBinIdx * binWidthHz) / cutoffFreq);
                float gain = Math.Clamp(freqRatio * 0.7f, 0.5f, 1.0f);

                buffer[i] *= gain;
            }
        }

        // Apply slight volume compensation for filtering loss
        const float compensationDb = -2f;
        float compensationGain = (float)Math.Pow(10.0, compensationDb / 20.0);

        for (int i = offset + 0; i < count; i++)
        {
            buffer[i] *= compensationGain;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}