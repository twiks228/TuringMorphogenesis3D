// Helpers/TextureBuilder.cs
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using TuringMorphogenesis3D.Models;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>
/// Turns the raw simulation data into a pretty bitmap texture.
/// 
/// The solver gives us a flat array of concentration values (doubles from 0 to 1).
/// This class maps each value to a color using the selected color scheme,
/// packs them into a WriteableBitmap, and hands it off to WPF for rendering.
/// 
/// WriteableBitmap is the fastest way to do per-pixel updates in WPF
/// without dropping down to DirectX. It lets us write raw pixel bytes
/// directly into GPU-accessible memory.
/// 
/// The resulting bitmap gets stretched over the 3D mesh via texture coordinates,
/// so the pattern follows the surface of the sphere, torus, etc.
/// </summary>
public static class TextureBuilder
{
    /// <summary>
    /// Creates a new bitmap texture from simulation data.
    /// 
    /// Each pixel corresponds to one cell in the simulation grid.
    /// The color is determined by the concentration of chemical V
    /// at that cell, mapped through the selected color scheme.
    /// 
    /// We write pixels in BGRA format (Blue, Green, Red, Alpha)
    /// because that's what WPF's Bgra32 pixel format expects.
    /// Yes, it's backwards from what you'd expect. Welcome to graphics programming.
    /// </summary>
    /// <param name="vData">Flat array of V concentrations from the solver</param>
    /// <param name="width">Grid width (must match solver width)</param>
    /// <param name="height">Grid height (must match solver height)</param>
    /// <param name="scheme">Which color palette to use</param>
    public static WriteableBitmap Build(
        double[] vData, int width, int height, ColorScheme scheme)
    {
        var bitmap = new WriteableBitmap(
            width, height,
            96, 96,                // DPI — standard screen resolution
            PixelFormats.Bgra32,   // 4 bytes per pixel: Blue, Green, Red, Alpha
            null                   // no palette needed for direct color
        );

        // Allocate a byte array for all pixels.
        // Each pixel = 4 bytes, so total = width × height × 4.
        int bytesPerPixel = 4;
        int stride = width * bytesPerPixel;
        var pixels = new byte[height * stride];

        // We need to know the actual range of V values in this frame
        // so we can stretch the color mapping to use the full palette.
        // Without this, early simulation steps look almost invisible
        // because V values are still very small.
        double vMin = double.MaxValue;
        double vMax = double.MinValue;

        for (int i = 0; i < vData.Length; i++)
        {
            if (vData[i] < vMin) vMin = vData[i];
            if (vData[i] > vMax) vMax = vData[i];
        }

        double vRange = vMax - vMin;
        if (vRange < 1e-10)
            vRange = 1.0; // avoid division by zero in early frames

        // Fill pixel array
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                // Normalize V value to [0, 1] range for this frame
                double raw = vData[x * height + y];
                double normalized = (raw - vMin) / vRange;

                Color color = ColorMapper.Map(normalized, scheme);

                // WPF bitmap layout: row-major, BGRA byte order
                int pixelIndex = (y * width + x) * bytesPerPixel;
                pixels[pixelIndex + 0] = color.B;  // Blue
                pixels[pixelIndex + 1] = color.G;  // Green
                pixels[pixelIndex + 2] = color.R;  // Red
                pixels[pixelIndex + 3] = 255;      // Alpha (fully opaque)
            }

        // Write the pixel data into the bitmap in one shot.
        // This is much faster than setting individual pixels.
        bitmap.WritePixels(
            new Int32Rect(0, 0, width, height),
            pixels,
            stride,
            0  // offset
        );

        return bitmap;
    }

    /// <summary>
    /// Creates a DiffuseMaterial with the texture bitmap applied.
    /// This is what WPF's 3D renderer needs to paint the mesh.
    /// 
    /// The ImageBrush stretches the bitmap across the mesh surface
    /// according to each vertex's UV texture coordinates.
    /// </summary>
    public static DiffuseMaterial BuildMaterial(
        double[] vData, int width, int height, ColorScheme scheme)
    {
        var bitmap = Build(vData, width, height, scheme);

        var brush = new ImageBrush(bitmap)
        {
            // These settings ensure the texture maps correctly
            // to our UV coordinates in the [0,1] range
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };

        return new DiffuseMaterial(brush);
    }
}