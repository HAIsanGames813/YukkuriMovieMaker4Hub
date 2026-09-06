using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YukkuriMovieMaker4Hub
{
    public partial class InstanceSettingsDialog : Window
    {
        private readonly InstanceInfo _instance;

        public InstanceSettingsDialog(InstanceInfo instance)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            _instance = instance;

            NameTextBox.Text = instance.Name;
            ExePathTextBox.Text = instance.ExePath;

            UpdateIconPreview();
        }

        // ────────────────────────────────────────
        // アイコンプレビュー（小）
        // ────────────────────────────────────────

        private void UpdateIconPreview()
        {
            // 背景
            if (IconPreviewBg != null)
            {
                var info = _instance;
                switch (info.IconBgType)
                {
                    case "Solid":
                        try { IconPreviewBg.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.IconBgColor1)); } catch { }
                        break;
                    case "Gradient":
                        try
                        {
                            var c1 = (Color)ColorConverter.ConvertFromString(info.IconBgColor1);
                            var c2 = (Color)ColorConverter.ConvertFromString(info.IconBgColor2);
                            IconPreviewBg.Fill = new LinearGradientBrush(c1, c2, 45);
                        }
                        catch { }
                        break;
                    default:
                        IconPreviewBg.Fill = Brushes.Transparent;
                        break;
                }
            }

            // スケール・オフセット反映
            if (PreviewScaleXform != null)
            {
                PreviewScaleXform.ScaleX = _instance.IconScale;
                PreviewScaleXform.ScaleY = _instance.IconScale;
            }
            if (PreviewTranslateXform != null)
            {
                PreviewTranslateXform.X = _instance.IconOffsetX;
                PreviewTranslateXform.Y = _instance.IconOffsetY;
            }
            // 画像
            if (IconPreviewSmall == null) return;
            if (!string.IsNullOrEmpty(_instance.IconPath) && File.Exists(_instance.IconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(_instance.IconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    IconPreviewSmall.Source = bmp;
                    return;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(_instance.ExePath) && File.Exists(_instance.ExePath))
            {
                try
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(_instance.ExePath);
                    if (icon != null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        IconPreviewSmall.Source = bs;
                    }
                }
                catch { }
            }
        }

        // ────────────────────────────────────────
        // アイコン設定ダイアログを開く
        // ────────────────────────────────────────

        private void OpenIconEditor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new IconEditorDialog(_instance) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            // 結果を InstanceInfo に反映
            _instance.IconPath = dlg.ResultIconPath;
            _instance.IconScale = dlg.ResultScale;
            _instance.IconOffsetX = dlg.ResultOffsetX;
            _instance.IconOffsetY = dlg.ResultOffsetY;
            _instance.IconBgType = dlg.ResultBgType;
            _instance.IconBgColor1 = dlg.ResultBgColor1;
            _instance.IconBgColor2 = dlg.ResultBgColor2;
            _instance.IconBgGradientAngle = dlg.ResultBgGradientAngle;
            _instance.IconBgImagePath = dlg.ResultBgImagePath;

            UpdateIconPreview();
        }

        // ────────────────────────────────────────
        // 参照パス変更
        // ────────────────────────────────────────

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe",
                Title = Translate.SelectExeTitle
            };
            if (dialog.ShowDialog() == true)
            {
                ExePathTextBox.Text = dialog.FileName;
                UpdateIconPreview();
            }
        }

        // ────────────────────────────────────────
        // 設定の再引き継ぎ
        // ────────────────────────────────────────

        private void ReInherit_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            var instances = mainWindow.Instances
                .Where(i => i.IsRealInstance && i.ExePath != _instance.ExePath)
                .ToList();

            if (instances.Count == 0)
            {
                MessageBox.Show(Translate.NoOtherInstance, "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SettingsInheritDialog(instances) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedFiles.Count > 0)
            {
                CopySettingsFiles(dlg.SelectedFiles, dlg.SourceExePath, ExePathTextBox.Text);
                MessageBox.Show(string.Format(Translate.InheritComplete, dlg.SelectedFiles.Count),
                    Translate.Complete, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopySettingsFiles(System.Collections.Generic.List<string> files, string srcExePath, string dstExePath)
        {
            try
            {
                string srcBase = GetLatestVersionDir(Path.Combine(Path.GetDirectoryName(srcExePath) ?? "", "user", "setting"));
                string dstBase = GetLatestVersionDir(Path.Combine(Path.GetDirectoryName(dstExePath) ?? "", "user", "setting"));
                if (!Directory.Exists(dstBase)) Directory.CreateDirectory(dstBase);
                foreach (var fn in files)
                {
                    string src = Path.Combine(srcBase, fn);
                    string dst = Path.Combine(dstBase, fn);
                    if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Translate.InheritFailed}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetLatestVersionDir(string settingsDir)
        {
            if (!Directory.Exists(settingsDir)) return settingsDir;
            var dirs = new DirectoryInfo(settingsDir).GetDirectories().OrderByDescending(d => d.LastWriteTime).ToArray();
            return dirs.Length > 0 ? dirs[0].FullName : settingsDir;
        }

        // ────────────────────────────────────────
        // 適用 / キャンセル
        // ────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text)) _instance.Name = NameTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(ExePathTextBox.Text)) _instance.ExePath = ExePathTextBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}