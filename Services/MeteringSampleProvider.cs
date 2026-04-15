using System;
using System.Diagnostics;
using NAudio.Wave;

namespace PaDDY.Services
{
    /// <summary>
    /// Wraps an ISampleProvider to compute L/R RMS levels per read block.
    /// Fires <see cref="RmsLevelChanged"/> with normalised 0-100 values.
    /// Throttles events to fire at most every ~30ms for smooth UI updates.
    /// </summary>
    public sealed class PlaybackMeterProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private const long FireIntervalMs = 15;

        // Accumulated RMS state between fires
        private double _sumL, _sumR;
        private int _samplesL, _samplesR;
        private long _lastFireMs;

        /// <summary>Fired with (left, right) normalised 0-100 values.</summary>
        public event Action<double, double>? RmsLevelChanged;

        public PlaybackMeterProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _lastFireMs = _sw.ElapsedMilliseconds;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read <= 0) return read;

            bool isStereo = _channels >= 2;

            for (int i = 0; i < read; i++)
            {
                float s = buffer[offset + i];
                if (isStereo)
                {
                    if (i % 2 == 0) { _sumL += s * s; _samplesL++; }
                    else { _sumR += s * s; _samplesR++; }
                }
                else { _sumL += s * s; _samplesL++; }
            }

            long now = _sw.ElapsedMilliseconds;
            if (now - _lastFireMs >= FireIntervalMs)
            {
                double rmsL = _samplesL > 0 ? Math.Sqrt(_sumL / _samplesL) : 0;
                double rmsR = isStereo ? (_samplesR > 0 ? Math.Sqrt(_sumR / _samplesR) : 0) : rmsL;

                double normL = Math.Min(100.0, rmsL * 500.0);
                double normR = Math.Min(100.0, rmsR * 500.0);

                _sumL = 0; _sumR = 0;
                _samplesL = 0; _samplesR = 0;
                _lastFireMs = now;

                RmsLevelChanged?.Invoke(normL, normR);
            }

            return read;
        }
    }
}
