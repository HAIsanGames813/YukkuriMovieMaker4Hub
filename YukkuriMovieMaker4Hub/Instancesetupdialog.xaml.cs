using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace YukkuriMovieMaker4Hub
{
    public partial class InstanceSetupDialog : Window
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string YMM4_RELEASES_API = "https://api.github.com/repos/manju-summoner/YukkuriMovieMaker4/releases";

        public bool IsNewDownload { get; }
        public string? InstanceName => NameTextBox.Text.Trim();
        public string? IconPath { get; private set; }
        public string? ResultExePath { get; private set; }

        private string? _customIconPath = null;
        private CancellationTokenSource? _cts;

        // ────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────

        public InstanceSetupDialog(bool isNewDownload, string? defaultName = null)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            IsNewDownload = isNewDownload;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");

            if (isNewDownload)
            {
                TitleText.Text = Translate.NewInstanceSetupTitle;
                OkButton.Content = Translate.DownloadAndCreate;
                EditionPanel.Visibility = Visibility.Visible;
                InstallFolderPanel.Visibility = Visibility.Visible;
                // DL前でもカスタムアイコンを自由に設定できる
                IconSourceLabel.Text = Translate.AutoSetExeIcon;
                SelectIconButton.IsEnabled = true;
                ResetIconButton.IsEnabled = false;
            }
            else
            {
                TitleText.Text = Translate.InstanceSetupTitle;
                OkButton.Content = Translate.Add;
                IconSourceLabel.Text = Translate.UseExeIcon;
            }

            if (!string.IsNullOrEmpty(defaultName))
                NameTextBox.Text = defaultName;
        }

        /// <summary>既存exe追加用：exeパスを渡してアイコンを自動設定</summary>
        public InstanceSetupDialog(string exePath, string defaultName) : this(false, defaultName)
        {
            LoadExeIcon(exePath);
        }

        // ────────────────────────────────────────
        // アイコン操作
        // ────────────────────────────────────────

        private void LoadExeIcon(string exePath)
        {
            if (!File.Exists(exePath)) return;
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return;
                var bs = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                IconPreview.Source = bs;
                _customIconPath = null;
                IconPath = null;
                IconSourceLabel.Text = Translate.UseExeIcon;
                ResetIconButton.IsEnabled = false;
            }
            catch { }
        }

        private void SelectIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Translate.SelectIconTitle,
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.ico|すべてのファイル|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dialog.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    IconPreview.Source = bmp;
                    _customIconPath = dialog.FileName;
                    IconPath = dialog.FileName;
                    IconSourceLabel.Text = System.IO.Path.GetFileName(dialog.FileName);
                    ResetIconButton.IsEnabled = true;
                }
                catch
                {
                    System.Windows.MessageBox.Show("Image load failed.",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ResetIcon_Click(object sender, RoutedEventArgs e)
        {
            _customIconPath = null;
            IconPath = null;
            ResetIconButton.IsEnabled = false;
            if (!string.IsNullOrEmpty(ResultExePath))
            {
                LoadExeIcon(ResultExePath);
                // LoadExeIcon内でIconSourceLabel.Textが更新される
            }
            else
            {
                // DL前はexeがないのでプレビューを空にしてラベルを自動設定に戻す
                IconPreview.Source = null;
                IconSourceLabel.Text = Translate.AutoSetExeIcon;
            }
        }

        // ────────────────────────────────────────
        // インストール先選択
        // ────────────────────────────────────────

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Translate.InstallFolder
            };
            if (dialog.ShowDialog() == true)
                InstallFolderTextBox.Text = dialog.FolderName;
        }

        // ────────────────────────────────────────
        // OK / Cancel
        // ────────────────────────────────────────

        private async void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                System.Windows.MessageBox.Show(Translate.InstanceNameLabel,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (IsNewDownload)
            {
                if (string.IsNullOrWhiteSpace(InstallFolderTextBox.Text))
                {
                    System.Windows.MessageBox.Show(Translate.InstallFolder,
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await DownloadAndInstallAsync();
            }
            else
            {
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
        }

        // ────────────────────────────────────────
        // ダウンロード & インストール
        // ────────────────────────────────────────

        private async Task DownloadAndInstallAsync()
        {
            OkButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            _cts = new CancellationTokenSource();

            try
            {
                bool isLite = LiteEdition.IsChecked == true;

                // ① GitHub API から最新リリースのDL URLを取得
                SetStatus(Translate.CheckingLatestVersion, 5);
                string downloadUrl = await GetLatestDownloadUrlAsync(isLite, _cts.Token);

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    System.Windows.MessageBox.Show(
                        Translate.DownloadUrlFailed,
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUi();
                    return;
                }

                string fileName = Uri.UnescapeDataString(
                    System.IO.Path.GetFileName(new Uri(downloadUrl).AbsolutePath));
                string installBase = InstallFolderTextBox.Text;
                string tempZipPath = System.IO.Path.Combine(installBase, fileName);

                // ② zipをダウンロード
                SetStatus($"{Translate.Acquiring}: {fileName}", 10);

                using (var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
                using (var res = await _http.SendAsync(req,
                    HttpCompletionOption.ResponseHeadersRead, _cts.Token))
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
                            DownloadSizeText.Text =
                                $"{read / 1024.0 / 1024:F1} MB / {total / 1024.0 / 1024:F1} MB";
                        }
                    }
                }

                // ③ インスタンス名のフォルダを作成してその中に解凍する
                SetStatus(Translate.Extracting, 75);
                string instanceFolderName = NameTextBox.Text.Trim();
                string finalDir = System.IO.Path.Combine(installBase, instanceFolderName);

                if (Directory.Exists(finalDir))
                {
                    var r = System.Windows.MessageBox.Show(
                        Translate.FolderAlreadyExists,
                        "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes)
                    {
                        File.Delete(tempZipPath);
                        ResetUi();
                        return;
                    }
                    Directory.Delete(finalDir, true);
                }

                // zip内にルートフォルダが1つある場合は中身だけ展開してfinalDirに配置
                string tempExtractDir = System.IO.Path.Combine(installBase, "__ymm4_tmp_" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                // zipのトップ直下にフォルダが1つだけある場合はその中身をfinalDirとして使う
                var topDirs = Directory.GetDirectories(tempExtractDir);
                var topFiles = Directory.GetFiles(tempExtractDir);
                string sourceDir = (topDirs.Length == 1 && topFiles.Length == 0)
                    ? topDirs[0]
                    : tempExtractDir;

                Directory.Move(sourceDir, finalDir);
                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, true);

                // ④ zip削除
                SetStatus(Translate.Cleanup, 92);
                File.Delete(tempZipPath);

                // ⑤ YukkuriMovieMaker.exe を探す
                SetStatus(Translate.Complete, 100);
                string exePath = FindExe(finalDir);
                if (string.IsNullOrEmpty(exePath))
                    exePath = FindExeInDirectory(installBase);

                if (string.IsNullOrEmpty(exePath))
                {
                    System.Windows.MessageBox.Show(
                        Translate.ExeNotFound,
                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResetUi();
                    return;
                }

                ResultExePath = exePath;

                // ⑥ アイコン処理：カスタムアイコン未設定の場合のみexeアイコンをプレビュー
                if (_customIconPath == null)
                {
                    // カスタム未設定 → exeアイコンを自動表示
                    LoadExeIcon(exePath);
                    IconSourceLabel.Text = Translate.UseExeIcon;
                }
                else
                {
                    // カスタム設定済み → そのまま維持（プレビューもIconPathも変更しない）
                    ResetIconButton.IsEnabled = true;
                }
                SelectIconButton.IsEnabled = true;

                DialogResult = true;
            }
            catch (OperationCanceledException)
            {
                SetStatus(Translate.Cancelled, 0);
                ResetUi();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"{Translate.InstallFailed}:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUi();
            }
        }

        // ────────────────────────────────────────
        // GitHub API からDL URL取得
        // ────────────────────────────────────────

        private async Task<string> GetLatestDownloadUrlAsync(bool isLite, CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, YMM4_RELEASES_API);
            using var res = await _http.SendAsync(req, token);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(token));
            var releases = doc.RootElement;

            if (releases.GetArrayLength() == 0) return string.Empty;

            // releases[0] = 最新リリース
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

        // ────────────────────────────────────────
        // ヘルパー
        // ────────────────────────────────────────

        private static string FindExe(string dir)
        {
            string direct = System.IO.Path.Combine(dir, "YukkuriMovieMaker.exe");
            if (File.Exists(direct)) return direct;
            return FindExeInDirectory(dir);
        }

        private static string FindExeInDirectory(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "YukkuriMovieMaker.exe",
                    SearchOption.AllDirectories);
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
            OkButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}