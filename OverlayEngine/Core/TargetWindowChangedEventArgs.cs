using NoIDSoftwork.OverlayEngine.Models;

namespace NoIDSoftwork.OverlayEngine.Core;

public sealed class TargetWindowChangedEventArgs : EventArgs
{
    public TargetWindowChangedEventArgs(TargetWindowInfo previous, TargetWindowInfo current)
    {
        Previous = previous;
        Current = current;
    }

    public TargetWindowInfo Previous { get; }
    public TargetWindowInfo Current { get; }
}
