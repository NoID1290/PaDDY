using System;

namespace Paddy.Services
{
    public static class StreamingRecorderFactory
    {
        /// <summary>
        /// Returns the file extension (without dot) for a given codec key.
        /// </summary>
        public static string ExtensionFor(string codec) => codec.ToLowerInvariant() switch
        {
            "mp3"  => "mp3",
            "opus" => "opus",
            "ogg"  => "ogg",
            _      => "wav"
        };

        /// <summary>
        /// Creates the appropriate <see cref="IStreamingRecorder"/> for the given codec.
        /// </summary>
        public static IStreamingRecorder Create(string codec) => codec.ToLowerInvariant() switch
        {
            "mp3"  => new Mp3Recorder(),
            "opus" => new OpusRecorder(),
            "ogg"  => new VorbisRecorder(),
            _      => new WaveFileRecorder()
        };

        /// <summary>
        /// Creates a recorder matched to the file extension of the given path.
        /// </summary>
        public static IStreamingRecorder CreateForFile(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            return Create(ext);
        }
    }
}
