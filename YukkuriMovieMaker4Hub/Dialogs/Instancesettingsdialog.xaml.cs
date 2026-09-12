using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;

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
            IconPathTextBox.Text = instance.IconPath ?? string.Empty;
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe|すべての実行ファイル (*.exe)|*.exe",
                Title = "YukkuriMovieMaker.exe を選択"
            };
            if (dialog.ShowDialog() == true)
            {
                ExePathTextBox.Text = dialog.FileName;
            }
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|すべてのファイル (*.*)|*.*",
                Title = "アイコン画像を選択"
            };
            if (dialog.ShowDialog() == true)
            {
                IconPathTextBox.Text = dialog.FileName;
            }
        }

        private void InheritSettings_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            var instances = mainWindow.Instances
                .Where(i => i.IsRealInstance && i.ExePath != _instance.ExePath)
                .ToList();

            if (instances.Count == 0)
            {
                MessageBox.Show("他に引き継ぎ可能なインスタンスが見つかりません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SettingsInheritDialog(instances) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedFiles.Count > 0)
            {
                CopySettingsFiles(dlg.SelectedFiles, dlg.SourceExePath, ExePathTextBox.Text);
                MessageBox.Show($"{dlg.SelectedFiles.Count} 件の設定ファイルを引き継ぎました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show($"設定の引き継ぎに失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetLatestVersionDir(string settingsDir)
        {
            if (!Directory.Exists(settingsDir)) return settingsDir;
            var dirs = new DirectoryInfo(settingsDir).GetDirectories().OrderByDescending(d => d.LastWriteTime).ToArray();
            return dirs.Length > 0 ? dirs[0].FullName : settingsDir;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                _instance.Name = NameTextBox.Text.Trim();
            }

            if (!string.IsNullOrWhiteSpace(ExePathTextBox.Text))
            {
                _instance.ExePath = ExePathTextBox.Text.Trim();
            }

            string iconPath = IconPathTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                _instance.IconPath = iconPath;
            }
            else
            {
                _instance.IconPath = null;
            }
            _instance.IconBgType = "None";

            DialogResult = true;
        }
    }
}
