using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FortuneWheel.ViewModels;

/// <summary>Конвертер: true → жёлтый фон, false → прозрачный.</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(255, 235, 59)); // жёлтый
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
