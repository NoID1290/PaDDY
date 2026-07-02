namespace NoIDSoftwork.OverlayEngine.Models;

public readonly record struct TargetWindowInfo(IntPtr Handle, uint ProcessId, WindowBounds Bounds)
{
    public static TargetWindowInfo Empty => new(IntPtr.Zero, 0, WindowBounds.Empty);
    public bool IsValid => Handle != IntPtr.Zero && ProcessId != 0;
}
