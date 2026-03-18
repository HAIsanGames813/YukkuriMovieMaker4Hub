using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace YukkuriMovieMaker4Hub
{
    /// <summary>
    /// InstanceInfo の背景ブラシを返す IValueConverter。
    /// Binding の Value に InstanceInfo.IconBgBrush (= this) を渡すと背景ブラシを返す。
    /// </summary>
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

        /// <summary>
        /// 中心対称の LinearGradientBrush を生成する。
        /// StartPoint/EndPoint を中心(0.5,0.5)から角度方向に対称に配置することで、
        /// どの角度でも1色目→2色目が正しくグラデーションされる。
        /// </summary>
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

    /// <summary>bool → Visibility 変換（SettingsInheritDialog の IsRecommended 用）</summary>
    public class BooleanToVisibilityStaticConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityStaticConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}