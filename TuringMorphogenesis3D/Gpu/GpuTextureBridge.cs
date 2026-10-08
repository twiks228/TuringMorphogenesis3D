using System;
using System.Windows;
using System.Windows.Interop;
using SharpDX.Direct3D11;
using D3D9 = SharpDX.Direct3D9;
using D3D11Device = SharpDX.Direct3D11.Device;
using D3D11Texture = SharpDX.Direct3D11.Texture2D;

namespace TuringMorphogenesis3D.Gpu;

/// <summary>
/// Мост D3D11 → WPF: D3D9-текстура (RenderTarget, shared) открывается
/// в D3D11 через shared handle. Compute пишет в свою RGBA8-текстуру,
/// CopyResource копирует в shared, D3DImage показывает её в WPF.
/// Ноль readback для визуала.
/// </summary>
public sealed class GpuTextureBridge : IDisposable
{
    private readonly D3D9.Direct3DEx _d3d9;
    private readonly D3D9.DeviceEx _dev9;
    private readonly D3D9.Texture _tex9;
    private readonly D3D9.Surface _surf9;

    public D3D11Texture SharedTex11 { get; }
    public D3DImage Image { get; }
    public int Width { get; }
    public int Height { get; }

    public GpuTextureBridge(D3D11Device device11, int width, int height, IntPtr hwnd)
    {
        Width = width;
        Height = height;

        _d3d9 = new D3D9.Direct3DEx();

        var pp = new D3D9.PresentParameters
        {
            Windowed = true,
            DeviceWindowHandle = hwnd,          // ← в SharpDX имя именно такое
            SwapEffect = D3D9.SwapEffect.Discard,
            PresentationInterval = D3D9.PresentInterval.Default,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferFormat = D3D9.Format.A8R8G8B8
        };

        _dev9 = new D3D9.DeviceEx(
            _d3d9, 0, D3D9.DeviceType.Hardware, hwnd,
            D3D9.CreateFlags.HardwareVertexProcessing |
            D3D9.CreateFlags.Multithreaded |
            D3D9.CreateFlags.FpuPreserve,
            pp);

        var shared = IntPtr.Zero;
        _tex9 = new D3D9.Texture(
            _dev9, width, height, 1,
            D3D9.Usage.RenderTarget,
            D3D9.Format.A8R8G8B8,
            D3D9.Pool.Default,
            ref shared);

        if (shared == IntPtr.Zero)
            throw new InvalidOperationException("D3D9 shared handle not available");

        _surf9 = _tex9.GetSurfaceLevel(0);

        SharedTex11 = device11.OpenSharedResource<D3D11Texture>(shared);

        Image = new D3DImage();
        Image.Lock();
        Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surf9.NativePointer);
        Image.Unlock();
    }

    /// <summary>Только из UI-потока.</summary>
    public void Present()
    {
        Image.Lock();
        Image.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
        Image.Unlock();
    }

    public void Dispose()
    {
        _surf9?.Dispose();
        _tex9?.Dispose();
        SharedTex11?.Dispose();
        _dev9?.Dispose();
        _d3d9?.Dispose();
    }
}