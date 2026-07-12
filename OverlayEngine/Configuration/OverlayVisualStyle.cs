namespace NoIDSoftwork.OverlayEngine.Configuration;

public sealed class OverlayVisualStyle
{
    public double Opacity { get; set; } = 0.9;
    public int MarginX { get; set; } = 24;
    public int MarginY { get; set; } = 24;
    public string PrimaryColorHex { get; set; } = "#FFFFFFFF";
    public string AccentColorHex { get; set; } = "#FF4CAF50";
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 2f; // 18 was default
}
