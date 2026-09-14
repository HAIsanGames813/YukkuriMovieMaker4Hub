using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;


namespace YukkuriMovieMaker4Hub
{
    public partial class VersionSelectDialog : Window
    {
        private readonly PluginCatalogItem _plugin;
        private readonly MainWindow _owner;
        private GitHubReleaseDetail? _selectedRelease;

        public VersionSelectDialog(PluginCatalogItem plugin, MainWindow owner)
        {
            _plugin = plugin;
            _owner = owner;
            InitializeComponent();
            ThemeHelper.Sync(this);
            DataContext = this;
            LoadReleases();
        }

        public string DialogTitle => string.Format(Translate.VersionReleaseSelectTitle, _plugin.Owner?.ToUpper(), _plugin.Repo?.ToUpper());
        public PluginCatalogItem Plugin => _plugin;

        private void LoadReleases()
        {
            if (_plugin.Releases == null) return;
            foreach (var release in _plugin.Releases)
            {
                var btn = new Button
                {
                    Content = release.TagName,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(12, 6, 12, 6),
                    Tag = release
                };
                btn.Click += SelectTag_Click;
                VersionTagsPanel.Children.Add(btn);
            }
            if (_plugin.Releases.Count > 0)
            {
                UpdateSelectedRelease(_plugin.Releases[0]);
            }
        }

        private void SelectTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GitHubReleaseDetail release)
            {
                UpdateSelectedRelease(release);
            }
        }

        private void UpdateSelectedRelease(GitHubReleaseDetail release)
        {
            _selectedRelease = release;
            foreach (UIElement child in VersionTagsPanel.Children)
            {
                if (child is Button btn)
                {
                    if (btn.Tag == release)
                    {
                        btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
                        btn.Foreground = System.Windows.Media.Brushes.White;
                    }
                    else
                    {
                        btn.ClearValue(Button.BackgroundProperty);
                        btn.ClearValue(Button.ForegroundProperty);
                    }
                }
            }

            ReleaseNameText.Text = release.TagName; // Or appropriate property
            TagText.Text = release.TagName;
            DateText.Text = release.PublishedAt.ToString("yyyy/MM/dd HH:mm");
            
            LoadReleaseNotes(release);
            LoadAssets(release);
        }

        private async void LoadReleaseNotes(GitHubReleaseDetail release)
        {
            if (string.IsNullOrEmpty(_plugin.Owner) || string.IsNullOrEmpty(_plugin.Repo)) return;
            try
            {
                var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
                var url = $"https://api.github.com/repos/{_plugin.Owner}/{_plugin.Repo}/releases/tags/{release.TagName}";
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode) return;
                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                string? body = doc.RootElement.GetProperty("body").GetString();
                if (body != null)
                {
                    string html = ConvertMarkdownToHtml(body);
                    Dispatcher.Invoke(() => { if (ReleaseBrowser != null) ReleaseBrowser.NavigateToString(html); });
                }
            }
            catch { }
        }

        private static string ConvertMarkdownToHtml(string markdown)
        {
            // GitHub の旧来の添付画像形式では、src が Markdown リンクになっていることがある。
            // そのままではブラウザが画像 URL として解釈できないため、実 URL に正規化する。
            markdown = System.Text.RegularExpressions.Regex.Replace(
                markdown,
                @"(?<prefix><img\b[^>]*?\bsrc\s*=\s*(?<quote>[""']))\[(?:[^\]]*)\]\((?<url>https?://[^)\s]+)\)(?<suffix>\k<quote>)",
                match => $"{match.Groups["prefix"].Value}{match.Groups["url"].Value}{match.Groups["suffix"].Value}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
            sb.AppendLine("body{font-family:Segoe UI,sans-serif;font-size:13px;margin:8px;}");
            sb.AppendLine("pre{background:#f4f4f4;padding:8px;border-radius:4px;overflow-x:auto;}");
            sb.AppendLine("code{background:#f4f4f4;padding:1px 4px;border-radius:2px;}");
            sb.AppendLine("img{max-width:100%;height:auto !important;}");
            sb.AppendLine("</style></head><body>");
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                string l = line;
                if (l.StartsWith("### ")) sb.AppendLine($"<h3>{System.Web.HttpUtility.HtmlEncode(l.Substring(4))}</h3>");
                else if (l.StartsWith("## ")) sb.AppendLine($"<h2>{System.Web.HttpUtility.HtmlEncode(l.Substring(3))}</h2>");
                else if (l.StartsWith("# ")) sb.AppendLine($"<h1>{System.Web.HttpUtility.HtmlEncode(l.Substring(2))}</h1>");
                // GitHub がサニタイズ済みで返す、配置用 p 要素と画像だけは HTML として通す。
                // これ以外の HTML は従来どおり文字列として表示し、意図しないスクリプト実行を防ぐ。
                else if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*</?p(?:\s+align\s*=\s*[""'][^""']*[""'])?\s*>\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(l, "^\\s*<img\\b[^>]*>\\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    sb.AppendLine(l);
                else sb.AppendLine($"<p>{System.Web.HttpUtility.HtmlEncode(l)}</p>");
            }
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void LoadAssets(GitHubReleaseDetail release)
        {
            AssetsCountText.Text = Translate.DistributionAssets;
            // Wrap in a list of 1 to fit the ItemTemplate binding expected (just showing BrowserDownloadUrl/FileName if available directly on GitHubReleaseDetail, otherwise just the detail itself as asset)
            AssetList.ItemsSource = new[] { release };
        }

        private void DownloadAsset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GitHubReleaseDetail asset)
            {
                _plugin.SelectedVersion = asset;
                _owner?.ExecuteDirectDownloadAsync(_plugin);
                Close();
            }
        }

        private void DownloadSourceZip_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRelease != null)
            {
                _plugin.EnsureGitHubOwnerRepo();
                if (!string.IsNullOrEmpty(_plugin.Owner) && !string.IsNullOrEmpty(_plugin.Repo))
                {
                    var url = $"https://github.com/{_plugin.Owner}/{_plugin.Repo}/archive/refs/tags/{_selectedRelease.TagName}.zip";
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                }
            }
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            _plugin.EnsureGitHubOwnerRepo();
            string? url = _plugin.GitHubReleasesUrl ?? _plugin.GitHubUrl ?? _plugin.BestSiteUrl;
            if (!string.IsNullOrEmpty(url))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRelease != null)
            {
                _plugin.SelectedVersion = _selectedRelease;
                _owner?.ExecuteDirectDownloadAsync(_plugin);
            }
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
