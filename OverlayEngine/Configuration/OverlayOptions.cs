namespace NoIDSoftwork.OverlayEngine.Configuration;

public sealed class OverlayOptions
{
    public bool Enabled { get; set; } = false;
    public int FrameRateCap { get; set; } = 60;
    public OverlayVisualStyle VisualStyle { get; set; } = new();
}
