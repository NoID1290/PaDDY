namespace NoIDSoftwork.OverlayEngine.Models;

public sealed class OverlayFrame
{
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<string> Lines { get; set; } = Array.Empty<string>();
    public string? IconPath { get; set; }
}
