// Helpers/SliderFillConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>
/// Calculates the width of the filled portion of a custom slider track.
/// 
/// WPF's built-in slider doesn't let you easily style the "filled" part
/// (the colored section from the left edge to the thumb).
/// So we do it ourselves: bind the filled border's Width to this converter,
/// which takes the slider's Value, Min, Max, and ActualWidth,
/// and returns how many pixels should be filled.
/// 
/// It's a bit of a hack, but it makes our sliders look gorgeous.
/// </summary>
public class SliderFillConverter : IMultiValueConverter
{
    public static SliderFillConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType,
        object parameter, CultureInfo culture)
    {
        try
        {
            if (values.Length < 4) return 0.0;

            double value = System.Convert.ToDouble(values[0]);
            double min = System.Convert.ToDouble(values[1]);
            double max = System.Convert.ToDouble(values[2]);
            double width = System.Convert.ToDouble(values[3]);

            if (max - min <= 0) return 0.0;

            double fraction = (value - min) / (max - min);
            double thumbOffset = 12.0; // approximate thumb margin

            return Math.Max(0, fraction * (width - thumbOffset * 2) + thumbOffset);
        }
        catch
        {
            return 0.0;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}