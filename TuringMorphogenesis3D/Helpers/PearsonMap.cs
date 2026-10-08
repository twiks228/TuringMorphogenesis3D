using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>
/// Карта жизнеспособности Грея–Скотта в пространстве (f, k):
/// для каждой точки прогоняется короткая мини-симуляция и меряется
/// дисперсия V. Живые зоны светятся, мёртвые — тёмные.
/// Строится асинхронно один раз при старте.
/// </summary>
public static class PearsonMap
{
    public static async Task<WriteableBitmap> BuildAsync(int mw = 96, int mh = 64)
    {
        return await Task.Run(() =>
        {
            var px = new byte[mw * mh * 4];

            Parallel.For(0, mh, j =>
            {
                double k = 0.025 + (0.075 - 0.025) * j / (mh - 1);
                for (int i = 0; i < mw; i++)
                {
                    double f = 0.008 + (0.085 - 0.008) * i / (mw - 1);
                    float act = SimActivity(f, k);

                    // мёртвая зона — тёмно-синяя, живая — бирюза→фиолет по активности
                    byte r = (byte)(16 + act * 150);
                    byte g = (byte)(16 + act * 200);
                    byte b = (byte)(28 + act * 220);

                    int o = (j * mw + i) * 4;
                    px[o] = b;
                    px[o + 1] = g;
                    px[o + 2] = r;
                    px[o + 3] = 255;
                }
            });

            var bmp = new WriteableBitmap(mw, mh, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, mw, mh), px, mw * 4, 0);
            bmp.Freeze();  
            return bmp;
        });
    }

    private static float SimActivity(double f, double k)
    {
        const int W = 24, H = 24;
        var u = new float[W * H];
        var v = new float[W * H];
        var un = new float[W * H];
        var vn = new float[W * H];

        Array.Fill(u, 1.0f);
        var rng = new Random(7);
        for (int b = 0; b < 5; b++)
        {
            int cx = rng.Next(W), cy = rng.Next(H);
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = ((cx + dx) % W + W) % W;
                    int y = ((cy + dy) % H + H) % H;
                    u[y * W + x] = 0.5f;
                    v[y * W + x] = 0.25f;
                }
        }

        float ff = (float)f, kk = (float)k;
        const float du = 0.16f, dv = 0.08f;

        for (int s = 0; s < 220; s++)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int xl = x == 0 ? W - 1 : x - 1;
                    int xr = x == W - 1 ? 0 : x + 1;
                    int yu = y == 0 ? H - 1 : y - 1;
                    int yd = y == H - 1 ? 0 : y + 1;

                    float uc = u[i], vc = v[i];
                    float lu = u[y * W + xl] + u[y * W + xr] + u[yu * W + x] + u[yd * W + x] - 4 * uc;
                    float lv = v[y * W + xl] + v[y * W + xr] + v[yu * W + x] + v[yd * W + x] - 4 * vc;
                    float uvv = uc * vc * vc;

                    un[i] = Math.Clamp(uc + du * lu - uvv + ff * (1 - uc), 0, 1);
                    vn[i] = Math.Clamp(vc + dv * lv + uvv - (ff + kk) * vc, 0, 1);
                }
            (u, un) = (un, u);
            (v, vn) = (vn, v);
        }

        double mean = 0;
        for (int i = 0; i < v.Length; i++) mean += v[i];
        mean /= v.Length;

        double var = 0;
        for (int i = 0; i < v.Length; i++)
        {
            double d = v[i] - mean;
            var += d * d;
        }
        var /= v.Length;

        return (float)Math.Clamp(var * 400.0, 0.0, 1.0);
    }
}