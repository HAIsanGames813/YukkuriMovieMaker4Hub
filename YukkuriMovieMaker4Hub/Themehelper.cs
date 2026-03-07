using System.Windows;
using System.Windows.Media;

namespace YukkuriMovieMaker4Hub
{
    /// <summary>
    /// ダイアログを開く際にApplication.Current.Resourcesの現在のテーマ値をWindow.Resourcesに同期するヘルパー。
    /// 各ダイアログのコンストラクタ（InitializeComponent の直後）で Sync(this) を呼ぶ。
    /// </summary>
    public static class ThemeHelper
    {
        private static readonly string[] BrushKeys =
        {
            "ThemeBrush", "PanelBrush", "ItemBrush",
            "TextBrush",  "SubTextBrush", "AccentBrush", "BorderBrush"
        };

        /// <summary>
        /// Application.Current.Resources の現在値を、ウィンドウ独自の Window.Resources に同期する。
        /// Window.Resources は Application.Resources より優先されるため、この同期が必須。
        /// </summary>
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
        }
    }
}