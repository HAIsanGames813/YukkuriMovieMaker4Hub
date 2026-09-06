using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YukkuriMovieMaker4Hub
{
    /// <summary>
    /// ダイアログを開く際にApplication.Current.Resourcesの現在のテーマ値をWindow.Resourcesに同期し、
    /// タイトルバーのダーク/ライトモード同期を行うヘルパー。
    /// </summary>
    public static class ThemeHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static bool IsCurrentDarkTheme
        {
            get
            {
                var appRes = Application.Current?.Resources;
                if (appRes != null && appRes["ThemeBrush"] is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                    return brightness < 128;
                }
                return false;
            }
        }

        private static readonly string[] BrushKeys =
        {
            "ThemeBrush", "PanelBrush", "ItemBrush",
            "TextBrush",  "SubTextBrush", "AccentBrush", "BorderBrush"
        };

        public static void ApplyTitleBarTheme(Window window, bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                int useDarkMode = isDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        public static void Sync(Window window)
        {
            var appRes = Application.Current?.Resources;
            if (appRes == null) return;

            // フォント
            if (appRes["AppFont"] is FontFamily font && window.Resources.Contains("AppFont"))
                window.Resources["AppFont"] = font;

            // ブラシ
            foreach (var key in BrushKeys)
                if (appRes[key] is SolidColorBrush brush && window.Resources.Contains(key))
                    window.Resources[key] = brush;

            ApplyTitleBarTheme(window, IsCurrentDarkTheme);
        }
    }
}