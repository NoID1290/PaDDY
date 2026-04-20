using System;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Unified abstraction over audio file readers for WAV, MP3, Ogg Vorbis, and Opus.
    /// Provides seeking, sample reading, and total duration — everything AudioEditorWindow needs.
    /// </summary>
    public interface IUnifiedAudioReader : IDisposable
    {
        WaveFormat WaveFormat { get; }
        TimeSpan TotalTime { get; }

        /// <summary>Current read position. Setting this seeks the reader.</summary>
        TimeSpan CurrentTime { get; set; }

        /// <summary>For WaveOutEvent.Init — returns this reader as an IWaveProvider.</summary>
        IWaveProvider AsWaveProvider();

        /// <summary>For waveform rendering — returns this reader as an ISampleProvider.</summary>
        ISampleProvider AsSampleProvider();

        /// <summary>Reads raw PCM bytes (same as IWaveProvider.Read). Used for trim/export.</summary>
        int Read(byte[] buffer, int offset, int count);
    }
}
