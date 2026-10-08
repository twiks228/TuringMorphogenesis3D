using System;
using System.Collections.Generic;
using System.Windows.Media;
using TuringMorphogenesis3D.Models;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>
/// Превращает число 0..1 в цвет через многоточечные градиенты (5 опорных цветов
/// на схему — заметно богаче, чем 3). Для скорости строится LUT из 256 цветов.
/// </summary>
public static class ColorMapper
{
    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static readonly Dictionary<ColorScheme, Color[]> Stops = new()
    {
        [ColorScheme.Ocean] = new[] { C(3, 8, 32), C(8, 50, 120), C(0, 150, 205), C(110, 225, 240), C(242, 253, 255) },
        [ColorScheme.Fire] = new[] { C(8, 0, 0), C(100, 8, 0), C(215, 55, 0), C(255, 170, 20), C(255, 250, 205) },
        [ColorScheme.Jungle] = new[] { C(3, 16, 5), C(8, 70, 28), C(35, 150, 55), C(150, 220, 90), C(238, 255, 195) },
        [ColorScheme.Grayscale] = new[] { C(6, 6, 6), C(65, 65, 65), C(135, 135, 135), C(205, 205, 205), C(252, 252, 252) },
        [ColorScheme.Neon] = new[] { C(6, 0, 24), C(60, 15, 190), C(255, 0, 175), C(255, 110, 120), C(0, 255, 245) },
    };

    public static Color Map(double value, ColorScheme scheme)
    {
        value = Math.Clamp(value, 0.0, 1.0);
        var stops = Stops.TryGetValue(scheme, out var s) ? s : Stops[ColorScheme.Grayscale];

        double pos = value * (stops.Length - 1);
        int i = Math.Min((int)pos, stops.Length - 2);
        return Lerp(stops[i], stops[i + 1], pos - i);
    }

    /// <summary>256 цветов в формате Bgra32 (0xAARRGGBB как int).</summary>
    public static int[] BuildLut(ColorScheme scheme)
    {
        var lut = new int[256];
        for (int i = 0; i < 256; i++)
        {
            var c = Map(i / 255.0, scheme);
            lut[i] = unchecked((int)0xFF000000) | (c.R << 16) | (c.G << 8) | c.B;
        }
        return lut;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}