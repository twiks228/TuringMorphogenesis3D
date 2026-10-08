using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TuringMorphogenesis3D.Helpers;

public sealed class PhaseXConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? Math.Clamp(d / 85.0 * 120.0, 0.0, 114.0) : 0.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PhaseYConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? Math.Clamp(80.0 - d / 85.0 * 80.0, 0.0, 74.0) : 74.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → 340 px, false → 0 (свёрнутая панель).</summary>
public sealed class PanelWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new GridLength(340) : new GridLength(0);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}