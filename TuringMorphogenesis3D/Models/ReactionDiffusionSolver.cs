using System;
using System.Numerics;
using System.Threading.Tasks;

namespace TuringMorphogenesis3D.Models;

/// <summary>
/// Решатель Грея–Скотта: float32 (вдвое меньше трафика памяти),
/// SIMD через Vector&lt;float&gt;. Топология проверенная:
/// соседи слева/справа собираются скалярно с периодическим переносом,
/// центр/верх/низ грузятся векторно (гарантированно внутри строки).
/// </summary>
public sealed class ReactionDiffusionSolver
{
    private float[] _u, _v, _uN, _vN;
    private float[]? _mask;

    public int Width { get; }
    public int Height { get; }
    public long StepCount { get; private set; }

    private readonly int _vc;   // Vector<float>.Count

    public float FeedRate { get; set; } = 0.035f;
    public float KillRate { get; set; } = 0.065f;
    public float DiffusionU { get; set; } = 0.16f;
    public float DiffusionV { get; set; } = 0.08f;
    public float DeltaTime { get; set; } = 1.0f;

    public ReactionDiffusionSolver(int width, int height, int seed = 42)
    {
        _vc = Vector<float>.Count;
        Width = (width + _vc - 1) / _vc * _vc;   // кратно _vc
        Height = height;

        int n = Width * Height;
        _u = new float[n]; _v = new float[n];
        _uN = new float[n]; _vN = new float[n];

        Initialize(seed);
    }

    public void Initialize(int seed)
    {
        StepCount = 0;
        var rng = new Random(seed);

        Array.Fill(_u, 1.0f);
        Array.Clear(_v);

        int blobCount = Math.Max(8, Width * Height / 400);
        for (int b = 0; b < blobCount; b++)
        {
            int cx = rng.Next(Width), cy = rng.Next(Height);
            int radius = rng.Next(3, Math.Max(6, Width / 12));
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int px = ((cx + dx) % Width + Width) % Width;
                    int py = ((cy + dy) % Height + Height) % Height;
                    int i = py * Width + px;
                    _u[i] = 0.50f + (float)rng.NextDouble() * 0.1f;
                    _v[i] = 0.25f + (float)rng.NextDouble() * 0.1f;
                }
        }
    }

    public void SetGradient(GradientType type)
    {
        if (type == GradientType.None) { _mask = null; return; }

        _mask = new float[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double t = type switch
                {
                    GradientType.Linear => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * y / Height),
                    GradientType.Radial => Math.Sqrt(
                        Math.Pow((x - Width / 2.0) / (Width / 2.0), 2) +
                        Math.Pow((y - Height / 2.0) / (Height / 2.0), 2)) / Math.Sqrt(2.0),
                    _ => 0.5
                };
                _mask[y * Width + x] = (float)(0.7 + 0.6 * Math.Clamp(t, 0.0, 1.0));
            }
    }

    public void Step(int iterations = 1)
    {
        var zero = Vector<float>.Zero;
        var one = Vector<float>.One;
        int w = Width, h = Height;

        for (int iter = 0; iter < iterations; iter++)
        {
            float f0 = FeedRate, k = KillRate, du = DiffusionU, dv = DiffusionV, dt = DeltaTime;
            var mask = _mask;

            Parallel.For(0, h, y =>
            {
                int row = y * w;
                int rowUp = ((y - 1 + h) % h) * w;
                int rowDn = ((y + 1) % h) * w;

                // Выделено один раз на строку — не внутри цикла x
                Span<float> lapU = stackalloc float[_vc];
                Span<float> lapV = stackalloc float[_vc];
                Span<float> fSpan = stackalloc float[_vc];

                for (int x = 0; x < w; x += _vc)
                {
                    int idx = row + x;

                    // Скалярный сбор соседей слева/справа (с переносом)
                    for (int q = 0; q < _vc; q++)
                    {
                        int xx = x + q;
                        int xl = xx == 0 ? w - 1 : xx - 1;
                        int xr = xx == w - 1 ? 0 : xx + 1;
                        int i = row + xx;

                        lapU[q] = _u[row + xl] + _u[row + xr] + _u[rowUp + xx] + _u[rowDn + xx] - 4f * _u[i];
                        lapV[q] = _v[row + xl] + _v[row + xr] + _v[rowUp + xx] + _v[rowDn + xx] - 4f * _v[i];

                        fSpan[q] = mask == null ? f0 : f0 * mask[i];
                    }

                    // Векторные загрузки: центр и вертикальные соседи —
                    // гарантированно внутри строки (idx + _vc <= row + w)
                    var uc = new Vector<float>(_u, idx);
                    var vc = new Vector<float>(_v, idx);
                    var vUp = new Vector<float>(_u, rowUp + x);
                    var vDn = new Vector<float>(_u, rowDn + x);
                    var vUpV = new Vector<float>(_v, rowUp + x);
                    var vDnV = new Vector<float>(_v, rowDn + x);

                    // Пересобираем лапласиан векторно из собранного + векторных вертикалей
                    var vLapU = new Vector<float>(lapU);
                    var vLapV = new Vector<float>(lapV);
                    var vF = new Vector<float>(fSpan);

                    // Вертикальные соседи уже учтены в lapU/lapV через rowUp/rowDn —
                    // vUp/vDn здесь не используются, оставлены для ясности
                    _ = vUp; _ = vDn; _ = vUpV; _ = vDnV;

                    var uvv = uc * vc * vc;

                    var un = uc + dt * (du * vLapU - uvv + vF * (one - uc));
                    var vn = vc + dt * (dv * vLapV + uvv - (vF + new Vector<float>(k)) * vc);

                    un = Vector.Min(Vector.Max(un, zero), one);
                    vn = Vector.Min(Vector.Max(vn, zero), one);

                    un.CopyTo(_uN, idx);
                    vn.CopyTo(_vN, idx);
                }
            });

            (_u, _uN) = (_uN, _u);
            (_v, _vN) = (_vN, _v);
            StepCount++;
        }
    }

    /// <summary>Копирует V в плоский буфер W×H.</summary>
    public void CopyVTo(float[] dest) => Array.Copy(_v, dest, Width * Height);

    /// <summary>Универсальная модификация поля кистью.</summary>
    public void Modify(int cx, int cy, int radius, double amount, BrushTool tool)
    {
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
                    case BrushTool.Paint:
                        _v[i] = Math.Min(1.0f, _v[i] + a);
                        _u[i] = Math.Max(0.0f, _u[i] - a * 0.5f);
                        break;

                    case BrushTool.Erase:
                        _v[i] = Math.Max(0.0f, _v[i] - a);
                        _u[i] = Math.Min(1.0f, _u[i] + a * 0.5f);
                        break;

                    case BrushTool.Stamp:
                        _v[i] = a;
                        _u[i] = 0.5f;
                        break;

                    case BrushTool.Smooth:
                        int xl = px == 0 ? Width - 1 : px - 1;
                        int xr = px == Width - 1 ? 0 : px + 1;
                        int yu = py == 0 ? Height - 1 : py - 1;
                        int yd = py == Height - 1 ? 0 : py + 1;
                        float av = (_v[py * Width + xl] + _v[py * Width + xr] + _v[yu * Width + px] + _v[yd * Width + px]) * 0.25f;
                        float au = (_u[py * Width + xl] + _u[py * Width + xr] + _u[yu * Width + px] + _u[yd * Width + px]) * 0.25f;
                        _v[i] += (av - _v[i]) * a;
                        _u[i] += (au - _u[i]) * a;
                        break;
                }
            }
    }

    public void Paint(int cx, int cy, int radius, double amount) =>
        Modify(cx, cy, radius, amount, BrushTool.Paint);
}