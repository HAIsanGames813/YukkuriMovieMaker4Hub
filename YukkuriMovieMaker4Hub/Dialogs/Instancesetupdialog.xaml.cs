using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string? IconPath => _iconInfo.IconPath;
        public string? ResultExePath { get; private set; }
        // 引き継ぎ対象ファイルリスト（ファイル名のみ）
        public List<string> InheritedSettingFiles { get; private set; } = new List<string>();

        /// <summary>アイコン設定を一時保持するダミーInstanceInfo</summary>
        private readonly InstanceInfo _iconInfo = new InstanceInfo();
        private CancellationTokenSource? _cts;
        // 新規DL時の初期インストール先：Hub exeと同階層の instance フォルダ
        private static string DefaultInstallBase =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instance");

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
                // DL前でもアイコン設定可能（exeなし状態でも開く）
                if (IconEditorButton != null) IconEditorButton.IsEnabled = true;
            }
            else
            {
                TitleText.Text = Translate.InstanceSetupTitle;
                OkButton.Content = Translate.Add;
                if (IconEditorButton != null) IconEditorButton.IsEnabled = true;
            }

            if (!string.IsNullOrEmpty(defaultName))
                NameTextBox.Text = defaultName;

            // 新規DL時：インストール先の初期値を instance フォルダに設定
            if (isNewDownload)
            {
                string defaultBase = DefaultInstallBase;
                if (!Directory.Exists(defaultBase))
                    try { Directory.CreateDirectory(defaultBase); } catch { }
                InstallFolderTextBox.Text = defaultBase;
            }
        }

        /// <summary>既存exe追加用：exeパスを渡してアイコンを自動設定</summary>
        public InstanceSetupDialog(string exePath, string defaultName) : this(false, defaultName)
        {
            _iconInfo.ExePath = exePath;
            if (IconEditorButton != null) IconEditorButton.IsEnabled = true;
            RefreshIconPreview();
        }

        // ────────────────────────────────────────
        // アイコン設定
        // ────────────────────────────────────────

        private void OpenIconEditor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new IconEditorDialog(_iconInfo) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _iconInfo.IconPath = dlg.ResultIconPath;
            _iconInfo.IconScale = dlg.ResultScale;
            _iconInfo.IconOffsetX = dlg.ResultOffsetX;
            _iconInfo.IconOffsetY = dlg.ResultOffsetY;
            _iconInfo.IconBgType = dlg.ResultBgType;
            _iconInfo.IconBgColor1 = dlg.ResultBgColor1;
            _iconInfo.IconBgColor2 = dlg.ResultBgColor2;
            _iconInfo.IconBgGradientAngle = dlg.ResultBgGradientAngle;
            _iconInfo.IconBgImagePath = dlg.ResultBgImagePath;
            RefreshIconPreview();
        }

        /// <summary>アイコン設定を対象 InstanceInfo にコピーする</summary>
        public void CopyIconSettingsTo(InstanceInfo target)
        {
            target.IconPath = _iconInfo.IconPath;
            target.IconScale = _iconInfo.IconScale;
            target.IconOffsetX = _iconInfo.IconOffsetX;
            target.IconOffsetY = _iconInfo.IconOffsetY;
            target.IconBgType = _iconInfo.IconBgType;
            target.IconBgColor1 = _iconInfo.IconBgColor1;
            target.IconBgColor2 = _iconInfo.IconBgColor2;
            target.IconBgGradientAngle = _iconInfo.IconBgGradientAngle;
            target.IconBgImagePath = _iconInfo.IconBgImagePath;
        }

        /// <summary>小プレビューを更新する</summary>
        private void RefreshIconPreview()
        {
            // 背景
            if (IconPreviewBg != null)
            {
                switch (_iconInfo.IconBgType)
                {
                    case "Solid":
                        try
                        {
                            IconPreviewBg.Fill = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_iconInfo.IconBgColor1));
                        }
                        catch { }
                        break;
                    case "Gradient":
                        try
                        {
                            var c1 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_iconInfo.IconBgColor1);
                            var c2 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_iconInfo.IconBgColor2);
                            IconPreviewBg.Fill = new System.Windows.Media.LinearGradientBrush(c1, c2, _iconInfo.IconBgGradientAngle);
                        }
                        catch { }
                        break;
                    default:
                        IconPreviewBg.Fill = System.Windows.Media.Brushes.Transparent;
                        break;
                }
            }
            // 画像
            if (IconPreview == null) return;
            IconPreview.Source = _iconInfo.IconImage;
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

                // ⑥ アイコン設定ボタンを有効化
                _iconInfo.ExePath = exePath;
                if (IconEditorButton != null) IconEditorButton.IsEnabled = true;
                RefreshIconPreview();

                // ⑦ 設定引き継ぎ：一度起動してsettingsフォルダを生成してからダイアログを出す
                SetStatus(Translate.InheritSettingsLaunching, 100);
                await LaunchOnceAndWaitAsync(exePath);
                ShowInheritDialog(exePath);

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

        // ────────────────────────────────────────
        // 設定引き継ぎ
        // ────────────────────────────────────────

        /// <summary>初回起動してsettingsフォルダを生成させ、終了を待つ</summary>
        private async Task LaunchOnceAndWaitAsync(string exePath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return;
                // 最大15秒待ってsettingsフォルダが生成されるのを確認
                string settingsDir = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "user", "setting");
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(500);
                    if (Directory.Exists(settingsDir) && Directory.GetDirectories(settingsDir).Length > 0) break;
                }
                // YMM4を閉じる
                try { proc.CloseMainWindow(); proc.WaitForExit(3000); } catch { }
                try { if (!proc.HasExited) proc.Kill(); } catch { }
            }
            catch { }
        }

        private void ShowInheritDialog(string exePath)
        {
            try
            {
                string settingsDir = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "user", "setting");
                var dlg = new SettingsInheritDialog(settingsDir) { Owner = this };
                if (dlg.ShowDialog() == true)
                    InheritedSettingFiles = dlg.SelectedFiles;
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