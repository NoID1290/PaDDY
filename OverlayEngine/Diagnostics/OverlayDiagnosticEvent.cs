namespace NoIDSoftwork.OverlayEngine.Diagnostics;

public sealed class OverlayDiagnosticEvent
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public OverlayDiagnosticLevel Level { get; init; } = OverlayDiagnosticLevel.Info;
    public string Category { get; init; } = "overlay";
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
