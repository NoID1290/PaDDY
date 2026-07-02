using NoIDSoftwork.OverlayEngine.Configuration;
using NoIDSoftwork.OverlayEngine.Models;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;

namespace NoIDSoftwork.OverlayEngine.Rendering;

internal sealed class D3D11D2DRenderer : IDisposable
{
    private readonly object _sync = new();

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDXGISwapChain1? _swapChain;

    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1Bitmap1? _targetBitmap;
    private ID2D1SolidColorBrush? _primaryBrush;
    private ID2D1SolidColorBrush? _accentBrush;

    private IDWriteFactory? _dwriteFactory;
    private IDWriteTextFormat? _textFormat;

    private OverlayVisualStyle _style = new();
    private bool _initialized;

    private static readonly SwapChainDescription1 PrimarySwapChainDescription = new()
    {
        Format = Format.B8G8R8A8_UNorm,
        BufferUsage = Usage.RenderTargetOutput,
        BufferCount = 2,
        SampleDescription = new SampleDescription(1, 0),
        SwapEffect = SwapEffect.FlipDiscard,
        AlphaMode = AlphaMode.Ignore,
        Scaling = Scaling.Stretch
    };

    private static readonly SwapChainDescription1 FallbackSwapChainDescription = new()
    {
        Format = Format.B8G8R8A8_UNorm,
        BufferUsage = Usage.RenderTargetOutput,
        BufferCount = 2,
        SampleDescription = new SampleDescription(1, 0),
        SwapEffect = SwapEffect.Discard,
        AlphaMode = AlphaMode.Ignore,
        Scaling = Scaling.Stretch
    };

    public void Initialize(IntPtr hwnd, int width, int height, OverlayVisualStyle style)
    {
        lock (_sync)
        {
            _style = style;
            CreateDeviceResources(hwnd, Math.Max(64, width), Math.Max(64, height));
            _initialized = true;
        }
    }

    public void Resize(int width, int height)
    {
        lock (_sync)
        {
            if (!_initialized || _swapChain == null || _d2dContext == null)
            {
                return;
            }

            _targetBitmap?.Dispose();
            _targetBitmap = null;

            _swapChain.ResizeBuffers(2u, (uint)Math.Max(64, width), (uint)Math.Max(64, height), Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            RecreateTargetBitmap();
        }
    }

    public void UpdateStyle(OverlayVisualStyle style)
    {
        lock (_sync)
        {
            _style = style;
            if (_d2dContext == null)
            {
                return;
            }

            _primaryBrush?.Dispose();
            _accentBrush?.Dispose();
            _textFormat?.Dispose();

            _primaryBrush = _d2dContext.CreateSolidColorBrush(ParseColor(style.PrimaryColorHex, new Color4(1f, 1f, 1f, 1f)));
            _accentBrush = _d2dContext.CreateSolidColorBrush(ParseColor(style.AccentColorHex, new Color4(0.3f, 0.8f, 0.3f, 1f)));
            _textFormat = _dwriteFactory!.CreateTextFormat(style.FontFamily, style.FontSize);
        }
    }

    public void Render(OverlayFrame frame, WindowBounds bounds)
    {
        lock (_sync)
        {
            if (!_initialized || _d2dContext == null || _swapChain == null || _textFormat == null || _primaryBrush == null || _accentBrush == null)
            {
                return;
            }

            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));

            float x = _style.MarginX;
            float y = _style.MarginY;
            float right = Math.Max(x + 10, bounds.Width - _style.MarginX);

            if (!string.IsNullOrWhiteSpace(frame.Title))
            {
                _d2dContext.DrawText(frame.Title, _textFormat, new Rect(x, y, right, y + (_style.FontSize * 2f)), _accentBrush);
                y += _style.FontSize + 10f;
            }

            foreach (string line in frame.Lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                _d2dContext.DrawText(line, _textFormat, new Rect(x, y, right, y + (_style.FontSize * 1.6f)), _primaryBrush);
                y += _style.FontSize + 6f;
            }

            if (!string.IsNullOrWhiteSpace(frame.IconPath))
            {
                var iconRect = new Rect(Math.Max(x, right - 56), _style.MarginY, Math.Max(x + 24, right - 24), _style.MarginY + 32);
                _d2dContext.FillRectangle(iconRect, _accentBrush);
            }

            _d2dContext.EndDraw();
            _swapChain.Present(1, PresentFlags.None);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _primaryBrush?.Dispose();
            _accentBrush?.Dispose();
            _targetBitmap?.Dispose();
            _textFormat?.Dispose();
            _dwriteFactory?.Dispose();

            _d2dContext?.Dispose();
            _d2dDevice?.Dispose();
            _d2dFactory?.Dispose();

            _swapChain?.Dispose();
            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();

            _primaryBrush = null;
            _accentBrush = null;
            _targetBitmap = null;
            _textFormat = null;
            _dwriteFactory = null;
            _d2dContext = null;
            _d2dDevice = null;
            _d2dFactory = null;
            _swapChain = null;
            _d3dContext = null;
            _d3dDevice = null;

            _initialized = false;
        }
    }

    private void CreateDeviceResources(IntPtr hwnd, int width, int height)
    {
        Dispose();

        D3D11CreateDevice(
            adapter: default,
            driverType: DriverType.Hardware,
            flags: DeviceCreationFlags.BgraSupport,
            featureLevels: new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
            out _d3dDevice,
            out _d3dContext);

        using IDXGIDevice dxgiDevice = _d3dDevice!.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        _swapChain = CreateSwapChainWithFallback(factory, hwnd, width, height);

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        RecreateTargetBitmap();

        _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _primaryBrush = _d2dContext.CreateSolidColorBrush(ParseColor(_style.PrimaryColorHex, new Color4(1f, 1f, 1f, 1f)));
        _accentBrush = _d2dContext.CreateSolidColorBrush(ParseColor(_style.AccentColorHex, new Color4(0.3f, 0.8f, 0.3f, 1f)));
        _textFormat = _dwriteFactory.CreateTextFormat(_style.FontFamily, _style.FontSize);
    }

    private IDXGISwapChain1 CreateSwapChainWithFallback(IDXGIFactory2 factory, IntPtr hwnd, int width, int height)
    {
        SwapChainDescription1 primary = PrimarySwapChainDescription;
        primary.Width = (uint)width;
        primary.Height = (uint)height;

        try
        {
            return factory.CreateSwapChainForHwnd(_d3dDevice!, hwnd, primary);
        }
        catch (Exception)
        {
            SwapChainDescription1 fallback = FallbackSwapChainDescription;
            fallback.Width = (uint)width;
            fallback.Height = (uint)height;
            return factory.CreateSwapChainForHwnd(_d3dDevice!, hwnd, fallback);
        }
    }

    private void RecreateTargetBitmap()
    {
        if (_swapChain == null || _d2dContext == null)
        {
            return;
        }

        using IDXGISurface surface = _swapChain.GetBuffer<IDXGISurface>(0);
        var props = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            dpiX: 96.0f,
            dpiY: 96.0f,
            bitmapOptions: BitmapOptions.Target | BitmapOptions.CannotDraw,
            colorContext: null);

        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(surface, props);
        _d2dContext.Target = _targetBitmap;
        _d2dContext.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Cleartype;
    }

    private static Color4 ParseColor(string hex, Color4 fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        string trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length == 6)
        {
            trimmed = "FF" + trimmed;
        }

        if (trimmed.Length != 8 || !uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
        {
            return fallback;
        }

        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color4(r, g, b, a);
    }
}
