using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using BindFlags = SharpDX.Direct3D11.BindFlags;
using BufferDescription = SharpDX.Direct3D11.BufferDescription;
using ComputeShader = SharpDX.Direct3D11.ComputeShader;
using CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags;
using D3DBuffer = SharpDX.Direct3D11.Buffer;
using D3DFactory = SharpDX.DXGI.Factory1;
using Device = SharpDX.Direct3D11.Device;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;
using DeviceCreationFlags = SharpDX.Direct3D11.DeviceCreationFlags;
using DriverType = SharpDX.Direct3D.DriverType;
using Format = SharpDX.DXGI.Format;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using MapMode = SharpDX.Direct3D11.MapMode;
using ResourceOptionFlags = SharpDX.Direct3D11.ResourceOptionFlags;
using ResourceUsage = SharpDX.Direct3D11.ResourceUsage;
using Texture2D = SharpDX.Direct3D11.Texture2D;
using Texture2DDescription = SharpDX.Direct3D11.Texture2DDescription;
using UnorderedAccessView = SharpDX.Direct3D11.UnorderedAccessView;

namespace TuringMorphogenesis3D.Gpu;

/// <summary>Константный буфер. Должен точно совпадать с cbuffer Params в HLSL (48 байт).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RDParams
{
    public int Width;
    public int Height;
    public float FeedRate;
    public float KillRate;
    public float DiffusionU;
    public float DiffusionV;
    public float DeltaTime;
    public float GradientMode;
    public float RangeLo;
    public float RangeHi;
    public float SchemeF;
    public float Padding2;
}

public sealed class GpuSolver : IDisposable
{
    private readonly Device _device;
    private readonly DeviceContext _context;
    private readonly ComputeShader _shader;
    private readonly ComputeShader _colorShader;

    private D3DBuffer _bufferA = null!;
    private D3DBuffer _bufferB = null!;
    private UnorderedAccessView _uavA = null!;
    private UnorderedAccessView _uavB = null!;

    private Texture2D _colorTex = null!;
    private UnorderedAccessView _colorUav = null!;

    private D3DBuffer _stagingBuffer = null!;
    private D3DBuffer _constBuffer = null!;

    private GpuTextureBridge? _bridge;

    private bool _useA = true;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public long StepCount { get; private set; }
    public string AdapterName { get; }

    public System.Windows.Interop.D3DImage? BridgeImage => _bridge?.Image;
    public bool BridgeActive => _bridge != null;

    private const int Float2Size = 8;

    public GpuSolver(int width, int height)
    {
        Width = width;
        Height = height;
        int n = width * height;
        int bufferSize = n * Float2Size;

        _device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _device.ImmediateContext;

        try
        {
            using var factory = new D3DFactory();
            using var adapter = factory.GetAdapter1(0);
            AdapterName = adapter.Description1.Description;
        }
        catch
        {
            AdapterName = "DirectX 11 GPU";
        }

        string hlslPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Shaders", "RDCompute.hlsl");
        if (!File.Exists(hlslPath))
            throw new FileNotFoundException($"Shader not found: {hlslPath}");

        string hlsl = File.ReadAllText(hlslPath, Encoding.UTF8);

        var sim = ShaderBytecode.Compile(hlsl, "CSMain", "cs_5_0");
        if (sim.HasErrors)
            throw new InvalidOperationException($"HLSL CSMain failed:\n{sim.Message}");
        _shader = new ComputeShader(_device, sim.Bytecode);

        var col = ShaderBytecode.Compile(hlsl, "CSColorize", "cs_5_0");
        if (col.HasErrors)
            throw new InvalidOperationException($"HLSL CSColorize failed:\n{col.Message}");
        _colorShader = new ComputeShader(_device, col.Bytecode);

        _bufferA = CreateStructuredBuffer(bufferSize);
        _bufferB = CreateStructuredBuffer(bufferSize);
        _uavA = new UnorderedAccessView(_device, _bufferA);
        _uavB = new UnorderedAccessView(_device, _bufferB);

        // RGBA8-текстура для колоризации (B8G8R8A8 = аналог D3D9 A8R8G8B8)
        _colorTex = new Texture2D(_device, new Texture2DDescription
        {
            Width = Width,
            Height = Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });
        _colorUav = new UnorderedAccessView(_device, _colorTex);

        var stagingDesc = new BufferDescription
        {
            SizeInBytes = bufferSize,
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
            StructureByteStride = 0
        };
        _stagingBuffer = new D3DBuffer(_device, stagingDesc);

        var cbDesc = new BufferDescription
        {
            SizeInBytes = Utilities.SizeOf<RDParams>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
            StructureByteStride = 0
        };
        _constBuffer = new D3DBuffer(_device, cbDesc);
    }

    /// <summary>Попытка включить zero-readback рендер в WPF. Только UI-поток.</summary>
    public bool TryInitBridge(IntPtr hwnd)
    {
        try
        {
            _bridge?.Dispose();
            _bridge = new GpuTextureBridge(_device, Width, Height, hwnd);
            return true;
        }
        catch
        {
            _bridge = null;
            return false;
        }
    }

    private D3DBuffer CreateStructuredBuffer(int sizeInBytes)
    {
        var desc = new BufferDescription
        {
            SizeInBytes = sizeInBytes,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = Float2Size
        };
        return new D3DBuffer(_device, desc);
    }

    public void Initialize(double[] u, double[] v)
    {
        int n = Width * Height;
        var data = new Vector2[n];
        for (int i = 0; i < n; i++)
            data[i] = new Vector2((float)u[i], (float)v[i]);

        UploadToBuffer(_bufferA, data);
        UploadToBuffer(_bufferB, data);

        _useA = true;
        StepCount = 0;
    }

    private void UploadToBuffer(D3DBuffer buffer, Vector2[] data)
    {
        var box = _context.MapSubresource(buffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            unsafe
            {
                fixed (Vector2* src = data)
                {
                    int bytes = data.Length * Float2Size;
                    System.Buffer.MemoryCopy(src, (void*)box.DataPointer, bytes, bytes);
                }
            }
        }
        finally
        {
            _context.UnmapSubresource(buffer, 0);
        }
    }

    private void WriteParams(RDParams p)
    {
        var mapped = _context.MapSubresource(_constBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try { Marshal.StructureToPtr(p, mapped.DataPointer, false); }
        finally { _context.UnmapSubresource(_constBuffer, 0); }
    }

    public void Step(int iterations, double feed, double kill,
                     double du, double dv, int gradientMode)
    {
        for (int i = 0; i < iterations; i++)
        {
            var srcUav = _useA ? _uavA : _uavB;
            var dstUav = _useA ? _uavB : _uavA;

            WriteParams(new RDParams
            {
                Width = Width,
                Height = Height,
                FeedRate = (float)feed,
                KillRate = (float)kill,
                DiffusionU = (float)du,
                DiffusionV = (float)dv,
                DeltaTime = 1.0f,
                GradientMode = gradientMode
            });

            _context.ComputeShader.Set(_shader);
            _context.ComputeShader.SetConstantBuffer(0, _constBuffer);
            _context.OutputMerger.SetUnorderedAccessView(0, srcUav);
            _context.OutputMerger.SetUnorderedAccessView(1, dstUav);

            _context.Dispatch((Width + 15) / 16, (Height + 15) / 16, 1);

            _context.OutputMerger.SetUnorderedAccessView(0, null!);
            _context.OutputMerger.SetUnorderedAccessView(1, null!);
            _context.ComputeShader.Set(null!);

            _useA = !_useA;
            StepCount++;
        }
    }

    /// <summary>
    /// Колоризация на GPU + копирование в shared-текстуру + показ в WPF.
    /// Вызывать только из UI-потока.
    /// </summary>
    public void RenderToTexture(float rangeLo, float rangeHi, int scheme)
    {
        if (_bridge == null) return;

        var srcUav = _useA ? _uavA : _uavB;

        WriteParams(new RDParams
        {
            Width = Width,
            Height = Height,
            RangeLo = rangeLo,
            RangeHi = rangeHi,
            SchemeF = scheme
        });

        _context.ComputeShader.Set(_colorShader);
        _context.ComputeShader.SetConstantBuffer(0, _constBuffer);
        _context.OutputMerger.SetUnorderedAccessView(0, srcUav);
        _context.OutputMerger.SetUnorderedAccessView(2, _colorUav);

        _context.Dispatch((Width + 15) / 16, (Height + 15) / 16, 1);

        _context.OutputMerger.SetUnorderedAccessView(0, null!);
        _context.OutputMerger.SetUnorderedAccessView(2, null!);
        _context.ComputeShader.Set(null!);

        _context.CopyResource(_colorTex, _bridge.SharedTex11);
        _context.Flush();

        _bridge.Present();
    }

    public void CopyVTo(float[] dest)
    {
        var activeBuffer = _useA ? _bufferA : _bufferB;
        _context.CopyResource(activeBuffer, _stagingBuffer);

        var mapped = _context.MapSubresource(_stagingBuffer, 0, MapMode.Read, MapFlags.None);
        try
        {
            unsafe
            {
                float* src = (float*)mapped.DataPointer;
                int n = Width * Height;
                for (int i = 0; i < n; i++)
                    dest[i] = src[i * 2 + 1];
            }
        }
        finally
        {
            _context.UnmapSubresource(_stagingBuffer, 0);
        }
    }

    public void Modify(int cx, int cy, int radius, double amount, int tool)
    {
        var activeBuffer = _useA ? _bufferA : _bufferB;
        _context.CopyResource(activeBuffer, _stagingBuffer);

        int n = Width * Height;
        var data = new Vector2[n];

        var mapped = _context.MapSubresource(_stagingBuffer, 0, MapMode.Read, MapFlags.None);
        try
        {
            unsafe
            {
                float* src = (float*)mapped.DataPointer;
                for (int i = 0; i < n; i++)
                    data[i] = new Vector2(src[i * 2], src[i * 2 + 1]);
            }
        }
        finally
        {
            _context.UnmapSubresource(_stagingBuffer, 0);
        }

        float a = (float)amount;

        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int px = ((cx + dx) % Width + Width) % Width;
                int py = ((cy + dy) % Height + Height) % Height;
                int i = py * Width + px;

                switch (tool)
                {
                    case 0:
                        data[i].Y = Math.Min(1.0f, data[i].Y + a);
                        data[i].X = Math.Max(0.0f, data[i].X - a * 0.5f);
                        break;
                    case 1:
                        data[i].Y = Math.Max(0.0f, data[i].Y - a);
                        data[i].X = Math.Min(1.0f, data[i].X + a * 0.5f);
                        break;
                    case 3:
                        data[i].Y = a;
                        data[i].X = 0.5f;
                        break;
                    case 2:
                        int xl = px == 0 ? Width - 1 : px - 1;
                        int xr = px == Width - 1 ? 0 : px + 1;
                        int yu = py == 0 ? Height - 1 : py - 1;
                        int yd = py == Height - 1 ? 0 : py + 1;
                        float av = (data[py * Width + xl].Y + data[py * Width + xr].Y +
                                    data[yu * Width + px].Y + data[yd * Width + px].Y) * 0.25f;
                        float au = (data[py * Width + xl].X + data[py * Width + xr].X +
                                    data[yu * Width + px].X + data[yd * Width + px].X) * 0.25f;
                        data[i].Y += (av - data[i].Y) * a;
                        data[i].X += (au - data[i].X) * a;
                        break;
                }
            }

        UploadToBuffer(_bufferA, data);
        UploadToBuffer(_bufferB, data);
    }

    public void Paint(int cx, int cy, int radius, double amount) =>
        Modify(cx, cy, radius, amount, 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bridge?.Dispose();
        _colorUav?.Dispose();
        _colorTex?.Dispose();
        _uavA?.Dispose();
        _uavB?.Dispose();
        _bufferA?.Dispose();
        _bufferB?.Dispose();
        _stagingBuffer?.Dispose();
        _constBuffer?.Dispose();
        _shader?.Dispose();
        _colorShader?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}