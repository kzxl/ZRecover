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

public class HealthToDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecoveryHealth health)
        {
            return health switch
            {
                RecoveryHealth.Excellent => "Clusters intact. 100% data integrity expected with no sector collisions.",
                RecoveryHealth.Good => "Minor metadata loss. File payload is largely or fully recoverable.",
                RecoveryHealth.Poor => "Partial cluster overwrite detected. Some corruption likely.",
                RecoveryHealth.Overwritten => "Underlying sectors were overwritten by new writes. Recovery unlikely.",
                _ => "Unknown health assessment."
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class HexDumpConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        byte[]? data = value as byte[];
        if (data == null || data.Length == 0)
        {
            return "(No header preview bytes captured for this candidate)";
        }

        int maxBytes = Math.Min(data.Length, 256);
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < maxBytes; i += 16)
        {
            sb.Append($"{i:X8}  ");

            // Hex representation
            for (int j = 0; j < 16; j++)
            {
                if (i + j < maxBytes)
                {
                    sb.Append($"{data[i + j]:X2} ");
                }
                else
                {
                    sb.Append("   ");
                }
                if (j == 7) sb.Append(' ');
            }

            sb.Append(" |");

            // ASCII representation
            for (int j = 0; j < 16; j++)
            {
                if (i + j < maxBytes)
                {
                    byte b = data[i + j];
                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            sb.AppendLine("|");
        }

        if (data.Length > maxBytes)
        {
            sb.AppendLine($"... ({data.Length - maxBytes} additional bytes omitted from preview) ...");
        }

        return sb.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class DriveUsagePercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveVolumeInfo drive && drive.TotalBytes > 0)
        {
            long used = drive.TotalBytes - drive.FreeBytes;
            return Math.Clamp((double)used / drive.TotalBytes * 100.0, 0.0, 100.0);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToSafetyBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSafe = value is true;
        return isSafe 
            ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))  // Success green
            : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // Danger pink/red
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToSafetyBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSafe = value is true;
        return isSafe 
            ? new SolidColorBrush(Color.FromRgb(0x13, 0x28, 0x1F))  // Deep subtle green card
            : new SolidColorBrush(Color.FromRgb(0x31, 0x15, 0x1E)); // Deep subtle red card
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToSafetyBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSafe = value is true;
        return isSafe 
            ? new SolidColorBrush(Color.FromRgb(0x23, 0x54, 0x3A))
            : new SolidColorBrush(Color.FromRgb(0x61, 0x22, 0x31));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class CategoryToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileCategory cat)
        {
            return cat switch
            {
                FileCategory.Documents => "📄",
                FileCategory.Pictures => "🖼️",
                FileCategory.Videos => "🎬",
                FileCategory.Audio => "🎵",
                FileCategory.Archives => "📦",
                FileCategory.Executable => "⚙️",
                FileCategory.Databases => "🗄️",
                _ => "📁"
            };
        }
        return "📁";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

