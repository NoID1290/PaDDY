using System;

namespace PaDDY.Models
{
    public class RecordingEntry
    {
        /// <summary>Primary key in recordings.dat (UUID, hex).</summary>
        public string RecordingId { get; set; } = string.Empty;

        /// <summary>Path to the materialised temp file for this session. Used by playback and editor.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Human-readable display name (e.g. "Recording_20260426_143052.wav").</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// User-visible short name: uses DisplayName when available, falls back to FilePath stem.
        /// </summary>
        public string FileName =>
            string.IsNullOrEmpty(DisplayName)
                ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
                : System.IO.Path.GetFileNameWithoutExtension(DisplayName);

        public TimeSpan Duration { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsFavorite { get; set; } = false;

        /// <summary>Manual sort position within its panel/page (lower = earlier).</summary>
        public long SortOrder { get; set; } = 0;

        public string DurationLabel =>
            Duration.TotalSeconds < 60
                ? $"{Duration.TotalSeconds:0.0}s"
                : $"{(int)Duration.TotalMinutes}m {Duration.Seconds:00}s";
    }
}
