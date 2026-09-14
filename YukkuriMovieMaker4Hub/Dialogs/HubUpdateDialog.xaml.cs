using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public partial class HubUpdateDialog : Window
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string REPO_OWNER = "HAIsanGames813";
        private const string REPO_NAME = "YukkuriMovieMaker4Hub";

        public string CurrentVersion { get; }
        public string LatestTag { get; }
        public string? DownloadUrl { get; }
        public string? FileName { get; }

        public bool DoNotShowAgain => DoNotShowAgainCheckBox.IsChecked == true;
        public bool ExecuteUpdate { get; private set; } = false;

        public HubUpdateDialog(string currentVersion, string latestTag, string? downloadUrl, string? fileName, bool canExecuteUpdate = true)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);

            CurrentVersion = currentVersion;
            LatestTag = latestTag;
            DownloadUrl = downloadUrl;
            FileName = fileName;

            CurrentVersionText.Text = $"v{currentVersion}";
            LatestVersionText.Text = latestTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? latestTag : $"v{latestTag}";

            if (!canExecuteUpdate)
            {
                DialogTitleText.Text = Translate.DevelopmentBuildIsNewer;
                UpdateButton.Visibility = Visibility.Collapsed;
            }

            if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
            }

            Loaded += async (s, e) => await LoadReadmeAsync();
        }

        private async Task LoadReadmeAsync()
        {
            string markdown = string.Empty;
            string baseBranch = "master";

            try
            {
                // ① raw.githubusercontent から README.md を取得
                string masterUrl = $"https://raw.githubusercontent.com/{REPO_OWNER}/{REPO_NAME}/master/README.md";
                using var res = await _http.GetAsync(masterUrl);
                if (res.IsSuccessStatusCode)
                {
                    markdown = await res.Content.ReadAsStringAsync();
                    baseBranch = "master";
                }
                else
                {
                    string mainUrl = $"https://raw.githubusercontent.com/{REPO_OWNER}/{REPO_NAME}/main/README.md";
                    using var res2 = await _http.GetAsync(mainUrl);
                    if (res2.IsSuccessStatusCode)
                    {
                        markdown = await res2.Content.ReadAsStringAsync();
                        baseBranch = "main";
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                markdown = $"# YukkuriMovieMaker4Hub\n\n{Translate.HubReadmeFetchFailed}";
            }

            string rawBase = $"https://raw.githubusercontent.com/{REPO_OWNER}/{REPO_NAME}/{baseBranch}/";
            string html = ConvertMarkdownToHtml(markdown, rawBase, LatestTag);

            StatusText.Visibility = Visibility.Collapsed;
            ReadmeBrowser.NavigateToString(html);
        }

        private string ConvertMarkdownToHtml(string markdown, string rawBase, string title)
        {
            bool isDark = ThemeHelper.IsCurrentDarkTheme;
            string bgColor = isDark ? "#1E1E1E" : "#FFFFFF";
            string textColor = isDark ? "#E0E0E0" : "#24292F";
            string headingBorder = isDark ? "#3E3E42" : "#D0D7DE";
            string linkColor = isDark ? "#58A6FF" : "#0969DA";
            string codeBg = isDark ? "#2D2D30" : "#F6F8FA";
            string codeBorder = isDark ? "#3E3E42" : "#D0D7DE";
            string blockquoteColor = isDark ? "#8B949E" : "#57606A";
            string blockquoteBorder = isDark ? "#3E3E42" : "#D0D7DE";
            string scrollTrack = isDark ? "#1E1E1E" : "#F0F0F0";
            string scrollThumb = isDark ? "#555555" : "#B0B0B0";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<meta http-equiv='X-UA-Compatible' content='IE=edge'/>");
            sb.AppendLine($"<title>{System.Web.HttpUtility.HtmlEncode(title)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine($"html {{ color-scheme: {(isDark ? "dark" : "light")}; }}");
            // WPF WebBrowser (MSHTML) のスクロールバーは旧来のCSSプロパティでテーマ色を指定する。
            sb.AppendLine($"html {{ scrollbar-face-color: {scrollThumb}; scrollbar-track-color: {scrollTrack}; scrollbar-arrow-color: {textColor}; scrollbar-highlight-color: {scrollThumb}; scrollbar-shadow-color: {scrollTrack}; }}");
            sb.AppendLine($"body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif; font-size: 14px; line-height: 1.6; background-color: {bgColor}; color: {textColor}; margin: 0; padding: 16px; word-wrap: break-word; }}");
            sb.AppendLine($"h1, h2 {{ border-bottom: 1px solid {headingBorder}; padding-bottom: .3em; margin-top: 24px; margin-bottom: 16px; font-weight: 600; line-height: 1.25; }}");
            sb.AppendLine("h1 { font-size: 2em; }");
            sb.AppendLine("h2 { font-size: 1.5em; }");
            sb.AppendLine("h3 { font-size: 1.25em; font-weight: 600; margin-top: 20px; margin-bottom: 12px; }");
            sb.AppendLine($"a {{ color: {linkColor}; text-decoration: none; }}");
            sb.AppendLine("a:hover { text-decoration: underline; }");
            sb.AppendLine($"code {{ font-family: Consolas, 'Liberation Mono', Menlo, monospace; font-size: 85%; background-color: {codeBg}; border-radius: 4px; padding: .2em .4em; }}");
            sb.AppendLine($"pre {{ font-family: Consolas, monospace; padding: 14px; overflow: auto; font-size: 85%; line-height: 1.45; background-color: {codeBg}; border: 1px solid {codeBorder}; border-radius: 6px; }}");
            sb.AppendLine("pre code { padding: 0; background-color: transparent; }");
            sb.AppendLine($"blockquote {{ margin: 0 0 16px; padding: 0 1em; color: {blockquoteColor}; border-left: .25em solid {blockquoteBorder}; }}");
            sb.AppendLine("ul, ol { padding-left: 2em; margin-bottom: 14px; }");
            sb.AppendLine("li { margin-top: .25em; }");
            sb.AppendLine("hr { height: .25em; padding: 0; margin: 24px 0; background-color: #D0D7DE; border: 0; }");
            // 固定高さは上書きし、width="75%" のようなGitHub READMEの指定はそのまま尊重する。
            sb.AppendLine("img { max-width: 100%; height: auto !important; box-sizing: content-box; background-color: transparent; }");
            sb.AppendLine("</style></head><body>");

            // コードブロック ``` 処理
            var codeBlocks = new System.Collections.Generic.List<string>();
            markdown = Regex.Replace(markdown, @"```([a-zA-Z0-9_-]*)\r?\n([\s\S]*?)```", m =>
            {
                int idx = codeBlocks.Count;
                string lang = m.Groups[1].Value;
                string code = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                codeBlocks.Add($"<pre><code class=\"language-{lang}\">{code}</code></pre>");
                return $"%%CODEBLOCK_{idx}%%";
            });

            // 見出し
            markdown = Regex.Replace(markdown, @"^### (.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
            markdown = Regex.Replace(markdown, @"^## (.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
            markdown = Regex.Replace(markdown, @"^# (.+)$", "<h1>$1</h1>", RegexOptions.Multiline);

            // 旧GitHub記法: <img src="[URL](URL)" width="75%"> を通常のimg要素へ正規化する。
            markdown = Regex.Replace(markdown, "(<img\\b[^>]*?\\bsrc\\s*=\\s*\"\\[)[^\\]]+(\\]\\(([^)]+)\\)\")", m =>
                m.Groups[1].Value.Substring(0, m.Groups[1].Value.LastIndexOf('"') + 1) + m.Groups[3].Value + "\"",
                RegexOptions.IgnoreCase);

            // 画像: ![alt](url) -> 相対パス補正
            markdown = Regex.Replace(markdown, @"!\[([^\]]*)\]\(([^)]+)\)", m =>
            {
                string alt = m.Groups[1].Value;
                string url = m.Groups[2].Value.Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("//"))
                {
                    url = rawBase + url.TrimStart('/', '.');
                }
                return $"<p><img src=\"{url}\" alt=\"{System.Web.HttpUtility.HtmlEncode(alt)}\" /></p>";
            });

            // リンク: [text](url)
            markdown = Regex.Replace(markdown, @"\[([^\]]+)\]\(([^)]+)\)", m =>
            {
                string label = m.Groups[1].Value;
                string url = m.Groups[2].Value.Trim();
                return $"<a href=\"{url}\" target=\"_blank\">{label}</a>";
            });

            // 太字
            markdown = Regex.Replace(markdown, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
            markdown = Regex.Replace(markdown, @"__([^_]+)__", "<strong>$1</strong>");

            // 斜体
            markdown = Regex.Replace(markdown, @"\*([^*]+)\*", "<em>$1</em>");

            // インラインコード
            markdown = Regex.Replace(markdown, @"`([^`]+)`", "<code>$1</code>");

            // リスト
            markdown = Regex.Replace(markdown, @"^[*-] (.+)$", "<li>$1</li>", RegexOptions.Multiline);

            // HTMLタグ直接のimgタグ補正
            markdown = Regex.Replace(markdown, @"<img\s+([^>]*?)src=""([^""]+)""([^>]*?)>", m =>
            {
                string pre = m.Groups[1].Value;
                string src = m.Groups[2].Value;
                string post = m.Groups[3].Value;
                if (!src.StartsWith("http://") && !src.StartsWith("https://") && !src.StartsWith("//"))
                {
                    src = rawBase + src.TrimStart('/', '.');
                }
                return $"<img {pre}src=\"{src}\"{post}>";
            }, RegexOptions.IgnoreCase);

            // 改行
            markdown = markdown.Replace("\r\n", "\n").Replace("\n", "<br/>\n");

            // コードブロック復元
            for (int i = 0; i < codeBlocks.Count; i++)
            {
                markdown = markdown.Replace($"%%CODEBLOCK_{i}%%", codeBlocks[i]);
            }

            sb.Append(markdown);
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            ExecuteUpdate = true;
            DialogResult = true;
        }
    }
}
