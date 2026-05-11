using System;
using System.IO;
using System.Linq;

namespace PaDDY.Services
{
    public static class RecordingNameGenerator
    {
        public static string BuildDisplayName(AppSettings settings, DateTime createdAt, string codec)
        {
            string safeCodec = string.IsNullOrWhiteSpace(codec) ? "wav" : codec.Trim().TrimStart('.');

            if (settings.UseFocusedAppForPadTitle &&
                AudioSessionHelper.TryGetFocusedApplicationLabel(out var focusedLabel))
            {
                string appName = SanitizeFileNameSegment(focusedLabel);
                if (!string.IsNullOrWhiteSpace(appName))
                    return EnsureExtension(appName, safeCodec);
            }

            string template = string.IsNullOrWhiteSpace(settings.DefaultPadTitleTemplate)
                ? "Recording {timestamp}"
                : settings.DefaultPadTitleTemplate;

            string timestamp = createdAt.ToString("yyyy-MM-dd HH-mm-ss");
            string baseName = template
                .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase)
                .Replace("{app}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{codec}", safeCodec, StringComparison.OrdinalIgnoreCase)
                .Trim();

            baseName = SanitizeFileNameSegment(baseName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = $"Recording {timestamp}";

            return EnsureExtension(baseName, safeCodec);
        }

        private static string EnsureExtension(string name, string codec)
        {
            if (Path.HasExtension(name))
                return name;
            return $"{name}.{codec}";
        }

        private static string SanitizeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Where(ch => !invalid.Contains(ch))
                .ToArray())
                .Trim();

            // Keep names manageable in UI and for filesystem operations.
            return cleaned.Length <= 96 ? cleaned : cleaned[..96].Trim();
        }
    }
}
