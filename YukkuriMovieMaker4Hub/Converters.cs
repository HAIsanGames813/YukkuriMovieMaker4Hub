using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace YukkuriMovieMaker4Hub
{
    public class InstanceInfoBgConverter : IValueConverter
    {
        public static readonly InstanceInfoBgConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not InstanceInfo info) return Brushes.Transparent;
            switch (info.IconBgType)
            {
                case "Solid":
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(info.IconBgColor1);
                        return new SolidColorBrush(c);
                    }
                    catch { return Brushes.Transparent; }
                case "Gradient":
                    try
                    {
                        var c1 = (Color)ColorConverter.ConvertFromString(info.IconBgColor1);
                        var c2 = (Color)ColorConverter.ConvertFromString(info.IconBgColor2);
                        return MakeCenteredGradient(c1, c2, info.IconBgGradientAngle);
                    }
                    catch { return Brushes.Transparent; }
                default:
                    return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static LinearGradientBrush MakeCenteredGradient(Color c1, Color c2, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad) * 0.5;
            double dy = Math.Sin(rad) * 0.5;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 - dx, 0.5 - dy),
                EndPoint = new Point(0.5 + dx, 0.5 + dy),
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            brush.GradientStops.Add(new GradientStop(c1, 0.0));
            brush.GradientStops.Add(new GradientStop(c2, 1.0));
            brush.Freeze();
            return brush;
        }
    }

    public class BooleanToVisibilityStaticConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityStaticConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Translate.Enable : Translate.Disable;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Brushes.LightGreen : Brushes.Gray;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class IsNotNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InstalledVersionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s) && s != "-")
                return string.Format(Translate.InstalledVersion, s);
            return string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>色文字列(#RRGGBB等)をBrushに変換するコンバーター</summary>
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(s);
                    return new SolidColorBrush(color);
                }
                catch { }
            }
            return Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>boolを反転するコンバーター</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)false;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)false;
    }

    /// <summary>パスの末尾ディレクトリ名のみを表示するコンバーター（プライバシー配慮）</summary>
    public class MaskedPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                try { return System.IO.Path.GetFileName(s.TrimEnd('\\', '/')); }
                catch { }
            }
            return value ?? string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}