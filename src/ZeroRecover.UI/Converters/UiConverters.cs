using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZeroRecover.Core.Models;

namespace ZeroRecover.UI.Converters;

public class BytesToHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class HealthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecoveryHealth health)
        {
            return health switch
            {
                RecoveryHealth.Excellent => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)), // #A6E3A1 Green
                RecoveryHealth.Good => new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8)),      // #818CF8 Indigo
                RecoveryHealth.Poor => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)),      // #F9E2AF Yellow
                RecoveryHealth.Overwritten => new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)), // #F38BA8 Red
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
