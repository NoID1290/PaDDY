using System;

namespace PaDDY.Models
{
    public class RecordingEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileNameWithoutExtension(FilePath);
        public TimeSpan Duration { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsFavorite { get; set; } = false;

        public string DurationLabel =>
            Duration.TotalSeconds < 60
                ? $"{Duration.TotalSeconds:0.0}s"
                : $"{(int)Duration.TotalMinutes}m {Duration.Seconds:00}s";
    }
}
