using NoIDSoftwork.OverlayEngine.Configuration;
using NoIDSoftwork.OverlayEngine.Diagnostics;
using NoIDSoftwork.OverlayEngine.Models;
using NoIDSoftwork.OverlayEngine.Windowing;

namespace NoIDSoftwork.OverlayEngine.Core;

public sealed class OverlayEngine : IOverlayEngine
{
    private readonly object _sync = new();
    private readonly OverlayDiagnosticsStream _diagnostics = new();
    private TargetWindowTracker? _tracker;
    private OverlayWindowHost? _host;
    private OverlayFrame _frame = new();
    private bool _disposed;

    public OverlayEngineState State { get; private set; } = OverlayEngineState.Created;

    public OverlayOptions Options { get; private set; } = new();

    public TargetWindowInfo TargetWindow => _tracker?.Current ?? TargetWindowInfo.Empty;

    public event EventHandler<TargetWindowChangedEventArgs>? TargetWindowChanged;
    public event EventHandler<OverlayDiagnosticEvent>? DiagnosticEvent;

    public OverlayEngine()
    {
        _diagnostics.EventLogged += (_, e) => DiagnosticEvent?.Invoke(this, e);
    }

    public void Initialize(OverlayOptions? options = null)
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("OverlayEngine supports Windows only.");
            }

            if (State is OverlayEngineState.Initialized or OverlayEngineState.Running or OverlayEngineState.Hidden)
            {
                if (options != null)
                {
                    Options = options;
                }

                return;
            }

            Options = options ?? new OverlayOptions();
            _tracker = new TargetWindowTracker();
            _tracker.TargetWindowChanged += OnTargetWindowChanged;
            _host = new OverlayWindowHost(_diagnostics);
            _host.Start(Options);
            _host.SetVisible(false);

            _diagnostics.Log(OverlayDiagnosticLevel.Info, "engine", "Overlay engine initialized.");
            State = OverlayEngineState.Initialized;
        }
    }

    public void UpdateOptions(OverlayOptions options)
    {
        ThrowIfDisposed();

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        lock (_sync)
        {
            EnsureInitialized();
            Options = options;
            _host?.UpdateOptions(options);

            if (!options.Enabled)
            {
                _host?.SetVisible(false);
            }
            else if (State == OverlayEngineState.Running)
            {
                _host?.SetVisible(true);
            }

            _diagnostics.Log(OverlayDiagnosticLevel.Info, "engine", $"Overlay options updated (Enabled={options.Enabled}, FPS={options.FrameRateCap}, Opacity={options.VisualStyle.Opacity:0.00}).");
        }
    }

    public bool AttachToProcess(uint processId)
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            EnsureInitialized();
            bool attached = _tracker!.Attach(processId);
            if (attached)
            {
                _host?.UpdateBounds(_tracker.Current.Bounds);
                _host?.UpdateFrame(_frame);
                _host?.SetVisible(Options.Enabled);
                State = Options.Enabled ? OverlayEngineState.Running : OverlayEngineState.Hidden;
                _diagnostics.Log(OverlayDiagnosticLevel.Info, "tracking", $"Attached overlay to process {processId}.");
            }
            else
            {
                State = OverlayEngineState.Initialized;
                _diagnostics.Log(OverlayDiagnosticLevel.Warning, "tracking", $"Attach failed for process {processId}. No main window was found.");
            }

            return attached;
        }
    }

    public void Detach()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (State is OverlayEngineState.Created or OverlayEngineState.Disposed)
            {
                return;
            }

            _tracker?.Detach();
            _host?.SetVisible(false);
            State = OverlayEngineState.Initialized;
            _diagnostics.Log(OverlayDiagnosticLevel.Info, "tracking", "Overlay detached from target process.");
        }
    }

    public void Show()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            EnsureInitialized();
            if (!Options.Enabled)
            {
                _diagnostics.Log(OverlayDiagnosticLevel.Trace, "engine", "Show ignored because overlay is disabled in options.");
                return;
            }

            if (State is OverlayEngineState.Hidden or OverlayEngineState.Initialized)
            {
                _host?.SetVisible(true);
                State = OverlayEngineState.Running;
                _diagnostics.Log(OverlayDiagnosticLevel.Trace, "engine", "Overlay shown.");
            }
        }
    }

    public void Hide()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            EnsureInitialized();
            if (State is OverlayEngineState.Running or OverlayEngineState.Hidden)
            {
                _host?.SetVisible(false);
                State = OverlayEngineState.Hidden;
                _diagnostics.Log(OverlayDiagnosticLevel.Trace, "engine", "Overlay hidden.");
            }
        }
    }

    public void UpdateFrame(OverlayFrame frame)
    {
        ThrowIfDisposed();

        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        lock (_sync)
        {
            EnsureInitialized();
            _frame = frame;
            _host?.UpdateFrame(frame);
        }
    }

    public IReadOnlyList<OverlayDiagnosticEvent> GetRecentDiagnostics(int maxCount = 100)
    {
        return _diagnostics.GetRecent(maxCount);
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

            if (_tracker != null)
            {
                _tracker.TargetWindowChanged -= OnTargetWindowChanged;
                _tracker.Dispose();
                _tracker = null;
            }

            if (_host != null)
            {
                _host.Dispose();
                _host = null;
            }

            State = OverlayEngineState.Disposed;
        }
    }

    private void OnTargetWindowChanged(object? sender, TargetWindowChangedEventArgs e)
    {
        _host?.UpdateBounds(e.Current.Bounds);
        if (!e.Current.IsValid)
        {
            _host?.SetVisible(false);
            if (State != OverlayEngineState.Disposed)
            {
                State = OverlayEngineState.Hidden;
            }

            _diagnostics.Log(OverlayDiagnosticLevel.Warning, "tracking", "Target window is no longer valid; overlay hidden.");
        }
        else
        {
            _diagnostics.Log(OverlayDiagnosticLevel.Trace, "tracking", $"Target moved to ({e.Current.Bounds.X},{e.Current.Bounds.Y}) {e.Current.Bounds.Width}x{e.Current.Bounds.Height}.");
        }

        TargetWindowChanged?.Invoke(this, e);
    }

    private void EnsureInitialized()
    {
        if (State == OverlayEngineState.Created)
        {
            throw new InvalidOperationException("OverlayEngine must be initialized before use.");
        }

        if (State == OverlayEngineState.Disposed)
        {
            throw new ObjectDisposedException(nameof(OverlayEngine));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OverlayEngine));
        }
    }
}
