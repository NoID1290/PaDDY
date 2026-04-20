using System;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Streams incoming raw PCM bytes directly into an encoded output file.
    /// Implementations: WaveFileRecorder, Mp3Recorder, OpusRecorder, VorbisRecorder.
    /// </summary>
    public interface IStreamingRecorder : IDisposable
    {
        bool IsRecording { get; }
        string? CurrentFilePath { get; }

        void BeginRecording(string filePath, WaveFormat format);
        void AppendSamples(byte[] buffer, int offset, int count);

        /// <summary>Finalises and closes the file. Returns the recorded duration.</summary>
        TimeSpan Finish();
    }
}
