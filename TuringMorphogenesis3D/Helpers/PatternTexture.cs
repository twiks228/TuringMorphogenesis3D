using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TuringMorphogenesis3D.Models;

namespace TuringMorphogenesis3D.Helpers;

public sealed class PatternTexture
{
    private readonly int[] _pixels;
    private int[] _lut;
    private ColorScheme _scheme;
    private double _lo, _hi;
    private bool _hasRange;

    public int Width { get; }
    public int Height { get; }
    public WriteableBitmap Bitmap { get; }
    public float[] Normalized { get; }

    public ColorScheme Scheme
    {
        get => _scheme;
        set { _scheme = value; _lut = ColorMapper.BuildLut(value); }
    }

    public PatternTexture(int width, int height)
    {
        Width = width; Height = height;
        _pixels = new int[width * height];
        Normalized = new float[width * height];
        _lut = ColorMapper.BuildLut(ColorScheme.Ocean);
        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    }

    public void ResetRange() => _hasRange = false;

    public void Update(float[] v)
    {
        double min = double.MaxValue, max = double.MinValue;
        int n = v.Length;

        for (int i = 0; i < n; i++)
        {
            double x = v[i];
            if (x < min) min = x;
            if (x > max) max = x;
        }

        if (!_hasRange) { _lo = min; _hi = max; _hasRange = true; }
        else
        {
            _lo += (min - _lo) * 0.2;
            _hi += (max - _hi) * 0.2;
        }

        double range = Math.Max(_hi - _lo, 0.08);
        double invRange = 1.0 / range;

        var lut = _lut; var pix = _pixels; var norm = Normalized;

        for (int i = 0; i < n; i++)
        {
            double t = (v[i] - _lo) * invRange;
            t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
            t = t * t * (3.0 - 2.0 * t);
            t = t * t * (3.0 - 2.0 * t);

            norm[i] = (float)t;
            pix[i] = lut[(int)(t * 255.0)];
        }

        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), pix, Width * 4, 0);
    }
}