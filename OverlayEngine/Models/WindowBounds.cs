namespace NoIDSoftwork.OverlayEngine.Models;

public readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    public static WindowBounds Empty => new(0, 0, 0, 0);
}
