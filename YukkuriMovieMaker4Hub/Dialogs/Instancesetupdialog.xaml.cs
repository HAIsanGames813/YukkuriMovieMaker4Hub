using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public partial class InstanceSetupDialog : Window
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string YMM4_RELEASES_API = "https://api.github.com/repos/manju-summoner/YukkuriMovieMaker4/releases";

        public bool IsNewDownload => ModeComboBox.SelectedIndex == 0;
        public string InstanceName => string.IsNullOrWhiteSpace(NameTextBox.Text) || NameTextBox.Text == "名前"
            ? "YukkuriMovieMaker4"
            : NameTextBox.Text.Trim();

        public string? ResultExePath { get; private set; }
        public List<string> InheritedSettingFiles { get; private set; } = new List<string>();

        private CancellationTokenSource? _cts;

        private static string DefaultInstallBase =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instance");

        public InstanceSetupDialog(string? defaultName = null)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);

            if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
            }

            if (!string.IsNullOrEmpty(defaultName))
            {
                NameTextBox.Text = defaultName;
            }

            try
            {
                if (!Directory.Exists(DefaultInstallBase))
                    Directory.CreateDirectory(DefaultInstallBase);
            }
            catch { }
        }

        public InstanceSetupDialog(string exePath, string defaultName) : this(defaultName)
        {
            ModeComboBox.SelectedIndex = 1; // 既存追加
            ExePathTextBox.Text = exePath;
        }

        private void ModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (EditionLabel == null || ExePathLabel == null) return;

            bool isNew = ModeComboBox.SelectedIndex == 0;
            EditionLabel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            EditionComboBox.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            ExePathLabel.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
            ExePathPanel.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

            DownloadButton.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            AddButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
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
                if (NameTextBox.Text == "名前" || string.IsNullOrWhiteSpace(NameTextBox.Text))
                {
                    string dirName = Path.GetFileName(Path.GetDirectoryName(dialog.FileName) ?? "") ?? "";
                    if (!string.IsNullOrEmpty(dirName))
                        NameTextBox.Text = dirName;
                }
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

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string exePath = ExePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                MessageBox.Show("有効な YukkuriMovieMaker.exe のパスを指定してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultExePath = exePath;
            DialogResult = true;
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            await DownloadAndInstallAsync();
        }

        private async Task DownloadAndInstallAsync()
        {
            DownloadButton.IsEnabled = false;
            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadStatusText.Visibility = Visibility.Visible;
            _cts = new CancellationTokenSource();

            try
            {
                bool isLite = EditionComboBox.SelectedIndex == 1;

                SetStatus("最新バージョンの情報を確認中...", 5);
                string downloadUrl = await GetLatestDownloadUrlAsync(isLite, _cts.Token);

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    MessageBox.Show("ダウンロードURLの取得に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUi();
                    return;
                }

                string fileName = Uri.UnescapeDataString(Path.GetFileName(new Uri(downloadUrl).AbsolutePath));
                string installBase = DefaultInstallBase;
                string tempZipPath = Path.Combine(installBase, fileName);

                SetStatus($"ダウンロード中: {fileName}", 10);

                using (var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
                using (var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _cts.Token))
                {
                    res.EnsureSuccessStatusCode();
                    long total = res.Content.Headers.ContentLength ?? 0;

                    using var src = await res.Content.ReadAsStreamAsync(_cts.Token);
                    using var dst = File.Create(tempZipPath);

                    var buf = new byte[81920];
                    long read = 0;
                    int cnt;
                    while ((cnt = await src.ReadAsync(buf, 0, buf.Length, _cts.Token)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, cnt, _cts.Token);
                        read += cnt;
                        if (total > 0)
                        {
                            DownloadProgressBar.Value = 10 + (double)read / total * 60;
                            DownloadStatusText.Text = $"{read / 1024.0 / 1024:F1} MB / {total / 1024.0 / 1024:F1} MB";
                        }
                    }
                }

                SetStatus("ファイルを展開中...", 75);
                string instanceFolderName = InstanceName;
                string finalDir = Path.Combine(installBase, instanceFolderName);

                if (Directory.Exists(finalDir))
                {
                    var r = MessageBox.Show($"フォルダ「{instanceFolderName}」は既に存在します。上書きしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes)
                    {
                        File.Delete(tempZipPath);
                        ResetUi();
                        return;
                    }
                    Directory.Delete(finalDir, true);
                }

                string tempExtractDir = Path.Combine(installBase, "__ymm4_tmp_" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                var topDirs = Directory.GetDirectories(tempExtractDir);
                var topFiles = Directory.GetFiles(tempExtractDir);
                string sourceDir = (topDirs.Length == 1 && topFiles.Length == 0) ? topDirs[0] : tempExtractDir;

                Directory.Move(sourceDir, finalDir);
                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, true);

                SetStatus("クリーンアップ中...", 92);
                File.Delete(tempZipPath);

                SetStatus("完了", 100);
                string exePath = FindExe(finalDir);
                if (string.IsNullOrEmpty(exePath))
                    exePath = FindExeInDirectory(installBase);

                if (string.IsNullOrEmpty(exePath))
                {
                    MessageBox.Show("YukkuriMovieMaker.exe が見つかりませんでした。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResetUi();
                    return;
                }

                ResultExePath = exePath;

                // 初回起動＆引き継ぎダイアログ
                SetStatus("初期設定中...", 100);
                await LaunchOnceAndWaitAsync(exePath);
                ShowInheritDialog(exePath);

                DialogResult = true;
            }
            catch (OperationCanceledException)
            {
                SetStatus("キャンセルされました", 0);
                ResetUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"インストールに失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUi();
            }
        }

        private async Task<string> GetLatestDownloadUrlAsync(bool isLite, CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, YMM4_RELEASES_API);
            using var res = await _http.SendAsync(req, token);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(token));
            var releases = doc.RootElement;

            if (releases.GetArrayLength() == 0) return string.Empty;

            var latest = releases[0];
            if (!latest.TryGetProperty("assets", out var assets)) return string.Empty;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";

                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                bool hasLiteTag = name.Contains("_Lite", StringComparison.OrdinalIgnoreCase);

                if (isLite && hasLiteTag) return url;
                if (!isLite && !hasLiteTag) return url;
            }

            return string.Empty;
        }

        public void CopyIconSettingsTo(InstanceInfo target)
        {
            string iconPath = IconPathTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                target.IconPath = iconPath;
            }
            else
            {
                target.IconPath = null;
            }
            target.IconBgType = "None";
        }

        private static string FindExe(string dir)
        {
            string direct = Path.Combine(dir, "YukkuriMovieMaker.exe");
            if (File.Exists(direct)) return direct;
            return FindExeInDirectory(dir);
        }

        private static string FindExeInDirectory(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "YukkuriMovieMaker.exe", SearchOption.AllDirectories);
                return files.Length > 0 ? files[0] : string.Empty;
            }
            catch { return string.Empty; }
        }

        private void SetStatus(string text, double progress)
        {
            DownloadStatusText.Text = text;
            DownloadProgressBar.Value = progress;
        }

        private void ResetUi()
        {
            DownloadButton.IsEnabled = true;
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadStatusText.Visibility = Visibility.Collapsed;
        }

        private async Task LaunchOnceAndWaitAsync(string exePath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return;
                string settingsDir = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "user", "setting");
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(500);
                    if (Directory.Exists(settingsDir) && Directory.GetDirectories(settingsDir).Length > 0) break;
                }
                try { proc.CloseMainWindow(); proc.WaitForExit(3000); } catch { }
                try { if (!proc.HasExited) proc.Kill(); } catch { }
            }
            catch { }
        }

        private void ShowInheritDialog(string exePath)
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var instances = mainWindow?.Instances
                    .Where(i => i.IsRealInstance && i.ExePath != exePath)
                    .ToList();

                if (instances != null && instances.Count > 0)
                {
                    var dlg = new SettingsInheritDialog(instances) { Owner = this };
                    if (dlg.ShowDialog() == true)
                        InheritedSettingFiles = dlg.SelectedFiles;
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
