using System;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
using Vortice.Direct3D;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Implements a WPF D3DImage source that is backed by a DirectX 11 shared texture.
    /// This allows rendering directly to a GPU texture using DX11/D2D and displaying it in WPF
    /// without CPU-GPU readback overhead.
    /// </summary>
    public class DX11ImageSource : D3DImage, IDisposable
    {
        private ID3D11Device? _d3d11Device;
        private ID3D11DeviceContext? _d3d11Context;
        private ID3D11Texture2D? _d3d11Texture;

        private IDirect3D9Ex? _d3d9Ex;
        private IDirect3DDevice9Ex? _d3d9Device;
        private IDirect3DTexture9? _d3d9Texture;
        private IDirect3DSurface9? _d3d9Surface;

        private IntPtr _sharedHandle = IntPtr.Zero;
        private int _width;
        private int _height;

        public ID3D11Device? D3D11Device => _d3d11Device;
        public ID3D11DeviceContext? D3D11Context => _d3d11Context;
        public ID3D11Texture2D? RenderTarget => _d3d11Texture;

        public event Action<ID3D11Texture2D, ID3D11DeviceContext>? RenderFrame;

        public DX11ImageSource()
        {
            InitializeD3D();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        private void InitializeD3D()
        {
            // Initialize D3D11 Device
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out _d3d11Device,
                out _d3d11Context).CheckError();

            // Initialize D3D9Ex for WPF Interop
            D3D9.Direct3DCreate9Ex(out _d3d9Ex).CheckError();

            var presentParams = new Vortice.Direct3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                BackBufferFormat = Vortice.Direct3D9.Format.Unknown,
                BackBufferCount = 1,
                PresentationInterval = PresentInterval.Default
            };

            IntPtr desktopWindow = GetDesktopWindow();

            _d3d9Device = _d3d9Ex!.CreateDeviceEx(
                0, // AdapterDefault
                DeviceType.Hardware,
                desktopWindow,
                CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
                presentParams);
        }

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            if (width == _width && height == _height && _d3d11Texture != null) return;

            CleanupResources();

            _width = width;
            _height = height;

            // 1. Create a DX11 shared texture
            var desc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.Shared
            };

            _d3d11Texture = _d3d11Device!.CreateTexture2D(desc);

            // Get shared resource handle
            using (var dxgiResource = _d3d11Texture.QueryInterface<IDXGIResource>())
            {
                _sharedHandle = dxgiResource.SharedHandle;
            }

            // 2. Open the shared texture in D3D9Ex
            ref IntPtr sharedHandleRef = ref _sharedHandle;
            _d3d9Texture = _d3d9Device!.CreateTexture(
                (uint)width,
                (uint)height,
                1,
                Vortice.Direct3D9.Usage.RenderTarget,
                Vortice.Direct3D9.Format.A8R8G8B8,
                Pool.Default,
                ref sharedHandleRef);

            _d3d9Surface = _d3d9Texture.GetSurfaceLevel(0);

            // 3. Set back buffer on D3DImage
            Lock();
            SetBackBuffer(D3DResourceType.IDirect3DSurface9, _d3d9Surface.NativePointer, true);
            Unlock();
        }

        public void Invalidate()
        {
            if (_d3d11Texture == null || _d3d11Context == null || _d3d9Surface == null) return;

            // Trigger actual rendering on the D3D11 texture
            RenderFrame?.Invoke(_d3d11Texture, _d3d11Context);

            // Flush DX11 to submit all commands to GPU before DX9 reads it
            _d3d11Context.Flush();

            // Notify WPF D3DImage of updates
            Lock();
            AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            Unlock();
        }

        private void CleanupResources()
        {
            Lock();
            SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            Unlock();

            _d3d9Surface?.Dispose();
            _d3d9Surface = null;

            _d3d9Texture?.Dispose();
            _d3d9Texture = null;

            _d3d11Texture?.Dispose();
            _d3d11Texture = null;

            _sharedHandle = IntPtr.Zero;
        }

        public void Dispose()
        {
            CleanupResources();

            _d3d9Device?.Dispose();
            _d3d9Device = null;

            _d3d9Ex?.Dispose();
            _d3d9Ex = null;

            _d3d11Context?.Dispose();
            _d3d11Context = null;

            _d3d11Device?.Dispose();
            _d3d11Device = null;
        }
    }
}
