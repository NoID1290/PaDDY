using NoIDSoftwork.OverlayEngine.Interop;
using NoIDSoftwork.OverlayEngine.Models;

namespace NoIDSoftwork.OverlayEngine.Core;

internal sealed class TargetWindowTracker : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _timer;
    private uint _processId;
    private TargetWindowInfo _current = TargetWindowInfo.Empty;
    private bool _disposed;

    public TargetWindowTracker()
    {
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<TargetWindowChangedEventArgs>? TargetWindowChanged;

    public TargetWindowInfo Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool Attach(uint processId)
    {
        if (processId == 0)
        {
            return false;
        }

        ThrowIfDisposed();

        lock (_sync)
        {
            _processId = processId;
            _current = ResolveTargetWindow(processId);
            _timer.Change(0, 100);
            return _current.IsValid;
        }
    }

    public void Detach()
    {
        ThrowIfDisposed();

        TargetWindowInfo previous;
        lock (_sync)
        {
            previous = _current;
            _processId = 0;
            _current = TargetWindowInfo.Empty;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (previous.IsValid)
        {
            TargetWindowChanged?.Invoke(this, new TargetWindowChangedEventArgs(previous, TargetWindowInfo.Empty));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
            _current = TargetWindowInfo.Empty;
            _processId = 0;
        }
    }

    private void OnTick(object? state)
    {
        uint processId;
        TargetWindowInfo previous;

        lock (_sync)
        {
            if (_disposed || _processId == 0)
            {
                return;
            }

            processId = _processId;
            previous = _current;
        }

        TargetWindowInfo next = ResolveTargetWindow(processId);
        if (next.Equals(previous))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _current = next;
        }

        TargetWindowChanged?.Invoke(this, new TargetWindowChangedEventArgs(previous, next));
    }

    private static TargetWindowInfo ResolveTargetWindow(uint processId)
    {
        IntPtr hwnd = FindMainWindow(processId);
        if (hwnd == IntPtr.Zero)
        {
            return TargetWindowInfo.Empty;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return TargetWindowInfo.Empty;
        }

        int width = Math.Max(0, rect.Right - rect.Left);
        int height = Math.Max(0, rect.Bottom - rect.Top);
        WindowBounds bounds = new(rect.Left, rect.Top, width, height);
        return new TargetWindowInfo(hwnd, processId, bounds);
    }

    private static IntPtr FindMainWindow(uint processId)
    {
        IntPtr result = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero)
            {
                return true;
            }

            result = hWnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TargetWindowTracker));
        }
    }
}
