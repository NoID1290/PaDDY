using System.Runtime.InteropServices;
using NoIDSoftwork.OverlayEngine.Configuration;
using NoIDSoftwork.OverlayEngine.Diagnostics;
using NoIDSoftwork.OverlayEngine.Interop;
using NoIDSoftwork.OverlayEngine.Models;
using NoIDSoftwork.OverlayEngine.Rendering;

namespace NoIDSoftwork.OverlayEngine.Windowing;

internal sealed class OverlayWindowHost : IDisposable
{
    private readonly object _sync = new();
    private readonly OverlayDiagnosticsStream _diagnostics;
    private readonly D3D11D2DRenderer _renderer = new();
    private readonly string _className = $"PaDDYOverlayWindowClass_{Guid.NewGuid():N}";

    private Thread? _thread;
    private IntPtr _hwnd;
    private uint _threadId;
    private bool _running;
    private bool _visible;
    private OverlayFrame _frame = new();
    private WindowBounds _bounds = WindowBounds.Empty;
    private OverlayOptions _options = new();

    private NativeMethods.WndProc? _wndProc;

    public OverlayWindowHost(OverlayDiagnosticsStream diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void Start(OverlayOptions options)
    {
        lock (_sync)
        {
            if (_running)
            {
                _options = options;
                return;
            }

            _options = options;
            _running = true;
            _thread = new Thread(WindowThreadMain)
            {
                Name = "OverlayWindowHost",
                IsBackground = true
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            else if (_threadId != 0)
            {
                NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            }

            thread = _thread;
        }

        if (thread != null && !thread.Join(1500))
        {
            _diagnostics.Log(OverlayDiagnosticLevel.Warning, "host", "Overlay host thread did not stop cleanly within timeout.");
        }

        lock (_sync)
        {
            _thread = null;
            _threadId = 0;
            _hwnd = IntPtr.Zero;
            _visible = false;
        }
    }

    public void SetVisible(bool visible)
    {
        lock (_sync)
        {
            _visible = visible;
        }
    }

    public void UpdateBounds(WindowBounds bounds)
    {
        lock (_sync)
        {
            _bounds = bounds;
        }
    }

    public void UpdateFrame(OverlayFrame frame)
    {
        lock (_sync)
        {
            _frame = frame;
        }
    }

    public void UpdateOptions(OverlayOptions options)
    {
        IntPtr hwnd;
        lock (_sync)
        {
            _options = options;
            _renderer.UpdateStyle(options.VisualStyle);
            hwnd = _hwnd;
        }

        // Re-apply window opacity when the visual style changes.
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, OpacityToByte(options.VisualStyle.Opacity), NativeMethods.LWA_ALPHA);
        }
    }

    public void Dispose()
    {
        Stop();
        _renderer.Dispose();
    }

    private void WindowThreadMain()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _wndProc = WndProc;

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = _wndProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = NativeMethods.GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = _className,
                hIconSm = IntPtr.Zero
            };

            ushort atom = NativeMethods.RegisterClassEx(ref wc);
            if (atom == 0)
            {
                _diagnostics.Log(OverlayDiagnosticLevel.Error, "host", "Failed to register overlay window class.");
                return;
            }

            IntPtr hwnd = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                _className,
                "PaDDY Overlay",
                NativeMethods.WS_POPUP,
                0,
                0,
                64,
                64,
                IntPtr.Zero,
                IntPtr.Zero,
                wc.hInstance,
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                _diagnostics.Log(OverlayDiagnosticLevel.Error, "host", "Failed to create overlay window.");
                NativeMethods.UnregisterClass(_className, wc.hInstance);
                return;
            }

            lock (_sync)
            {
                _hwnd = hwnd;
            }

            // Apply per-window opacity so the game remains visible beneath the overlay.
            // SetLayeredWindowAttributes(LWA_ALPHA) is the correct transparency mechanism
            // for HWND swap chains; DwmExtendFrameIntoClientArea / AlphaMode.Premultiplied
            // require CreateSwapChainForComposition (DirectComposition) instead.
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, OpacityToByte(_options.VisualStyle.Opacity), NativeMethods.LWA_ALPHA);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.UpdateWindow(hwnd);

            bool rendererReady = false;
            try
            {
                _renderer.Initialize(hwnd, 64, 64, _options.VisualStyle);
                rendererReady = true;
                _diagnostics.Log(OverlayDiagnosticLevel.Info, "host", "Overlay window host started.");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(OverlayDiagnosticLevel.Error, "host", "Renderer initialization failed. Overlay rendering is disabled for this session.", ex);
            }

            DateTime lastFrame = DateTime.UtcNow;

            while (_running)
            {
                while (NativeMethods.PeekMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
                {
                    if (msg.message == NativeMethods.WM_QUIT)
                    {
                        _running = false;
                        break;
                    }

                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }

                if (!_running)
                {
                    break;
                }

                WindowBounds bounds;
                OverlayFrame frame;
                bool visible;
                OverlayOptions options;

                lock (_sync)
                {
                    bounds = _bounds;
                    frame = _frame;
                    visible = _visible;
                    options = _options;
                }

                int width = Math.Max(64, bounds.Width);
                int height = Math.Max(64, bounds.Height);

                if (rendererReady && visible && bounds.Width > 0 && bounds.Height > 0)
                {
                    NativeMethods.SetWindowPos(
                        hwnd,
                        NativeMethods.HWND_TOPMOST,
                        bounds.X,
                        bounds.Y,
                        width,
                        height,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                    _renderer.Resize(width, height);
                    _renderer.Render(frame, new WindowBounds(0, 0, width, height));
                }
                else
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    NativeMethods.SetWindowPos(
                        hwnd,
                        NativeMethods.HWND_TOPMOST,
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_HIDEWINDOW | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                }

                int fps = Math.Clamp(options.FrameRateCap, 15, 240);
                int frameDelay = Math.Max(1, 1000 / fps);
                int elapsed = (int)(DateTime.UtcNow - lastFrame).TotalMilliseconds;
                if (elapsed < frameDelay)
                {
                    Thread.Sleep(frameDelay - elapsed);
                }

                lastFrame = DateTime.UtcNow;
            }

            _renderer.Dispose();
            NativeMethods.DestroyWindow(hwnd);
            NativeMethods.UnregisterClass(_className, wc.hInstance);
            _diagnostics.Log(OverlayDiagnosticLevel.Info, "host", "Overlay window host stopped.");
        }
        catch (Exception ex)
        {
            _diagnostics.Log(OverlayDiagnosticLevel.Error, "host", "Overlay window host crashed.", ex);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return msg switch
        {
            NativeMethods.WM_NCHITTEST => new IntPtr(NativeMethods.HTTRANSPARENT),
            _ => NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam)
        };
    }

    /// <summary>Converts a 0.0–1.0 opacity value to a 20–255 byte for SetLayeredWindowAttributes.</summary>
    private static byte OpacityToByte(double opacity) =>
        (byte)Math.Clamp((int)(opacity * 255), 20, 255);
}
