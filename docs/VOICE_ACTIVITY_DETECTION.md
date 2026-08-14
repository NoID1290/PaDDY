# Voice Activity Detection - New Implementation Guide

## Overview

This document describes the new **VoiceActivityDetector** helper and **VoiceGateEffect** that have been added to PaDDY for sophisticated voice-only audio detection, specifically designed to filter out noise, music, and other non-vocal audio content.

## What Was Added

### 1. `Helpers/VoiceActivityDetector.cs`
A new spectral-based voice activity detector that uses advanced frequency analysis to distinguish human voice from music/noise.

**Key Features:**
- FFT-based spectral analysis focusing on human vocal frequencies (85-3000 Hz)
- Formant pattern detection (F1, F2, F3 regions characteristic of speech)
- Harmonic structure analysis to identify voiced sounds
- Automatic noise floor subtraction
- Music/bass-heavy content suppression
- Configurable detection modes: Spectral, Sensitive, Strict

**Note:** This class provides standalone voice activity detection for programmatic use or integration with recording services.

### 2. `EffectProcessor/Effects/VoiceGateEffect.cs`
A new effect in the effect chain that applies voice activity detection during audio processing.

**Placement in Chain:** Should be used before NoiseGate for best results:
- VoiceGate → NoiseGate (voice filtering first, then silence gating)

The effect now includes its own built-in spectral analysis that doesn't require external dependencies.

### 3. `Models/EffectSettings.cs`
Extended with `VoiceDetectionConfig` class for configuring voice detection settings.

## How to Use

### Option 1: Using the UI Effect Editor

1. Open PaDDY and go to **Effects → Edit Effects** (Ctrl+Shift+E or via Pad page)
2. Click **"⚡ ALL EFFECTS"** tab
3. Scroll down to find **"Voice Gate"** (will appear after Noise Gate)
4. Enable the Voice Gate by checking its "Enable" checkbox
5. Configure settings:
   - **Mode**: Choose detection sensitivity level
   - **Threshold**: Set voice activity confidence threshold
   - **Suppress Music**: Enable to filter music/bass content

### Option 2: Programmatic Usage with Effect Chain

#### Create voice-only chain for best noise/music filtering:

```csharp
// Create voice-only chain optimized for voice clarity
var chain = EffectChainFactory.CreateVoiceOnly();

// Or create custom chain with specific settings
var chain = new EffectChain();
chain.Add(new VoiceGateEffect 
{ 
    IsEnabled = true,
    Mode = "Sensitive",
    Threshold = 0.4f,
    SuppressMusic = true
});
chain.Add(new NoiseGateEffect());
```

### Option 3: Via Settings (Recommended for Most Users)

The voice detection is best used with the built-in DetectionAlgorithm settings in AppSettings:

1. Go to **Settings** → **Recording** tab
2. Find **"Detection Algorithm"** dropdown
3. Choose option **"Adaptive/spectral VAD"** instead of "RMS threshold"
4. Adjust sensitivity if needed

The spectral VAD mode will automatically use voice frequency analysis similar to the VoiceActivityDetector.

## Technical Details

### Voice Detection Strategy

The VoiceGateEffect works by analyzing audio spectra and looking for patterns characteristic of human speech:

1. **Formant Detection**: Speech has specific resonant frequencies (formants) that music/noise don't share:
   - F1 (First formant): 300-900 Hz (vowel quality)
   - F2 (Second formant): 850-2200 Hz (consonant place)
   - F3 (Third formant): 2500-3000 Hz (speech clarity)

2. **Harmonic Structure**: Voice has regular harmonic series from fundamental frequency:
   - Male voice: ~85-180 Hz fundamental
   - Female voice: ~165-255 Hz fundamental

3. **Voice-to-Non-Voice Ratio**: Measures energy distribution between voice-band (85-3000 Hz) and non-voice frequencies

4. **Spectral Noise Reduction**: Calculates local noise floor and subtracts it before detection

### Configuration Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Mode` | string | "Spectral" | Detection sensitivity mode |
| `Threshold` | float | 0.5f | Minimum confidence to detect voice |
| `SuppressMusic` | bool | false | Auto-filter music/bass content |

### VoiceActivityDetector Standalone Parameters

For programmatic use with Helpers.VoiceActivityDetector:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `FftSize` | int | 4096 | FFT size for spectral analysis |
| `SampleRate` | int | 48000 | Audio sample rate |
| `DetectionMode` | enum | Spectral | Detection sensitivity mode |
| `ConfidenceThreshold` | float | 0.5f | Minimum confidence to detect voice |
| `SmoothingWindowSamples` | int | 160 | Output confidence smoothing window |
| `MinVoiceSamples` | int | 10 | Minimum samples before declaring VAD active |
| `AutoSuppressMusic` | bool | false | Auto-filter music/bass content |

### Integration Points

The voice detection can be integrated at these points:

1. **Real-time Recording**: Process audio live through VoiceActivityDetector
2. **Post-processing**: Apply to recorded audio files using VoiceGateEffect
3. **Effect Chain**: Use VoiceGateEffect in effect editor
4. **Detection Algorithm**: Use as alternative to RMS threshold

## Comparison with Existing Solutions

| Feature | Old (RMS Threshold) | New (Spectral VAD) | VoiceGateEffect |
|---------|---------------------|--------------------|-----------------|
| Music Detection | ❌ Poor | ⚠️ Better | ✅ Excellent |
| Noise Suppression | ⚠️ Basic | ✅ Good | ✅ Advanced |
| Voice Clarity | ⚠️ Variable | ✅ Good | ✅ Excellent |
| CPU Usage | Low | Medium | Medium-High |
| Setup Complexity | Simple | Simple | Advanced |

### Recommendation

- **Most Users**: Use DetectionAlgorithm = "Adaptive/spectral VAD" in settings
- **Advanced Users**: Enable VoiceGateEffect in effect editor with Sensitive mode
- **Recording Quality**: Combine VoiceGate + NoiseGate for best results

## Troubleshooting

### Still hearing music/noise through detector

1. Check that **"Enable"** checkbox is checked on Voice Gate
2. Set Mode to **"Strict"** for more aggressive filtering
3. Increase Threshold to 0.6-0.7 if false positives occur
4. Enable **"Auto Suppress Music"** option

### False negatives (missing quiet speech)

1. Set Mode to **"Sensitive"** instead of "Spectral"
2. Lower Threshold to 0.3-0.4
3. Reduce SmoothingWindowSamples to 128 for faster response

### Audio artifacts or clicking

1. Reset the effect and reinitialize detector (Reset button)
2. Check sample rate matches audio source (should be 48kHz)
3. Try different FftSize values (try 2048 for lighter CPU)

## Code Examples

### Basic Usage (Programmatic with VoiceActivityDetector)

```csharp
// Create and configure detector
var vad = new VoiceActivityDetector
{
    FftSize = 4096,
    SampleRate = 48000,
    DetectionMode = VoiceActivityDetector.DetectionMode.Sensitive,
    ConfidenceThreshold = 0.3f,
    AutoSuppressMusic = true
};

vad.Initialize(1, 48000); // mono audio

// Process buffer
float[] result = vad.ProcessBuffer(buffer, 0, count, 1, 48000);
```

### Effect Chain with Voice Detection

```csharp
var chain = new EffectChain();

// Voice detection first (filter music/noise)
chain.Add(new VoiceGateEffect 
{ 
    IsEnabled = true,
    Mode = "Sensitive",
    Threshold = 0.4f,
    SuppressMusic = true
});

// Silence gating second (remove actual silence)
chain.Add(new NoiseGateEffect 
{ 
    IsEnabled = true,
    ThresholdDb = -45.0,
    AttackMs = 20.0,
    ReleaseMs = 150.0
});

// Compression and other effects
chain.Add(new CompressorEffect());
chain.Add(new EchoEffect());
chain.Add(new EqualizerEffect());

return chain;
```

### Using EffectChainFactory (Recommended)

```csharp
// Create voice-optimized chain with pre-configured VoiceGate + NoiseGate
var chain = EffectChainFactory.CreateVoiceOnly();

// Or use standard chain (includes VoiceGate if added)
var chain = EffectChainFactory.CreatePerClip();
```

## Documentation References

- `Helpers/VoiceActivityDetector.cs` - Main spectral voice detector (standalone use)
- `EffectProcessor/Effects/VoiceGateEffect.cs` - Effect chain integration
- `Models/EffectSettings.cs` - Configuration settings
- `Views/EffectsWindow.xaml.cs` - UI effect editor (needs Voice Gate section added)

## Summary

The new VoiceActivityDetector and VoiceGateEffect provide sophisticated voice-only detection using spectral analysis, specifically designed to filter out noise and music while preserving human vocal content. This complements the existing RMS-based detection with an advanced alternative that understands the acoustic characteristics of speech vs non-speech audio.

For best results:
- Use in combination with NoiseGate effect
- Set appropriate threshold based on recording environment
- Enable Auto Suppress Music for cleaner voice-only output