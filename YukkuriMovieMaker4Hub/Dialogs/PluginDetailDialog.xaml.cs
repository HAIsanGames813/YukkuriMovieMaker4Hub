using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace YukkuriMovieMaker4Hub
{
    public partial class PluginDetailDialog : Window
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly PluginCatalogItem _plugin;
        private readonly MainWindow _owner;
        private bool _githubLoaded = false;
        private bool _boothLoaded = false;

        static PluginDetailDialog()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 YukkuriMovieMaker4Hub/1.0");
        }

        public PluginDetailDialog(PluginCatalogItem plugin, MainWindow owner)
        {
            _plugin = plugin;
            _owner = owner;
            InitializeComponent();
            ThemeHelper.Sync(this);
            DataContext = plugin;

            SuppressScriptErrors(GitHubReadmeBrowser);
            SuppressScriptErrors(BoothBrowser);

            ConfigureTabs();
            LoadLinks();
        }

        private static void SuppressScriptErrors(WebBrowser? wb)
        {
            if (wb == null) return;
            wb.Navigated += (s, e) =>
            {
                try
                {
                    dynamic? doc = wb.Document;
                    if (doc != null)
                    {
                        var parent = doc.parentWindow;
                        if (parent != null) parent.onerror = null;
                    }
                }
                catch { }
            };
        }

        private void ConfigureTabs()
        {
            _plugin.EnsureGitHubOwnerRepo();
            bool hasGh = _plugin.HasGitHub;
            bool hasBooth = _plugin.HasBooth;

            if (GitHubReadmeTab != null)
                GitHubReadmeTab.Visibility = hasGh ? Visibility.Visible : Visibility.Collapsed;

            if (BoothTab != null)
                BoothTab.Visibility = hasBooth ? Visibility.Visible : Visibility.Collapsed;

            if (VersionSelectButton != null)
                VersionSelectButton.Visibility = hasGh ? Visibility.Visible : Visibility.Collapsed;

            // デフォルトで GitHub があれば GitHub を自動読み込み準備、BOOTH があれば BOOTH 読み込み準備
            if (hasGh)
            {
                _ = LoadGitHubReadmeAsync();
            }
            if (hasBooth)
            {
                _ = LoadBoothDescriptionAsync();
            }
        }

        private void DetailTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DetailTabControl?.SelectedItem == GitHubReadmeTab && !_githubLoaded)
            {
                _ = LoadGitHubReadmeAsync();
            }
            else if (DetailTabControl?.SelectedItem == BoothTab && !_boothLoaded)
            {
                _ = LoadBoothDescriptionAsync();
            }
        }

        private void LoadLinks()
        {
            var links = _plugin.AllLinks;
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };
            if (links != null)
            {
                foreach (var link in links)
                {
                    try
                    {
                        var uri = new Uri(link.Url);
                        string siteName = GetSiteName(uri.Host);
                        var btn = new Button
                        {
                            Margin = new Thickness(0, 0, 8, 6),
                            Padding = new Thickness(10, 4, 10, 4),
                            Tag = link.Url
                        };
                        var sp = new StackPanel { Orientation = Orientation.Horizontal };
                        sp.Children.Add(new System.Windows.Shapes.Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M14,3V5H17.59L7.76,14.83L9.17,16.24L19,6.41V10H21V3M19,19H5V5H12V3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V12H19V19Z"),
                            Width = 12, Height = 12, Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin = new Thickness(0, 0, 6, 0),
                            Fill = System.Windows.SystemColors.ControlTextBrush
                        });
                        sp.Children.Add(new TextBlock { Text = string.Format(Translate.OpenSiteNamed, siteName), VerticalAlignment = VerticalAlignment.Center });
                        btn.Content = sp;
                        btn.Click += (s, ev) =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)((Button)s).Tag) { UseShellExecute = true }); }
                            catch { }
                        };
                        panel.Children.Add(btn);
                    }
                    catch { }
                }
            }
            if (LinksList != null) LinksList.Content = panel;
        }

        private static string GetSiteName(string host)
        {
            host = host.ToLowerInvariant();
            if (host.Contains("github.com")) return "GitHub";
            if (host.Contains("booth.pm")) return "BOOTH";
            if (host.Contains("twitter.com") || host.Contains("x.com")) return "X (Twitter)";
            if (host.Contains("youtube.com") || host.Contains("youtu.be")) return "YouTube";
            if (host.Contains("nicovideo.jp")) return Translate.Niconico;
            if (host.Contains("ymm4-info.net")) return Translate.InformationSite;
            if (host.Contains("bowlroll.net")) return "BowlRoll";
            if (host.Contains("drive.google.com")) return "Google Drive";
            if (host.Contains("dropbox.com")) return "Dropbox";
            if (host.StartsWith("www.")) host = host.Substring(4);
            return host;
        }

        // =========================================================
        // GitHub README.md 取得 & Markdownレンダリング
        // =========================================================
        private async Task LoadGitHubReadmeAsync()
        {
            if (_githubLoaded) return;
            _githubLoaded = true;

            _plugin.EnsureGitHubOwnerRepo();
            string owner = _plugin.Owner;
            string repo = _plugin.Repo;

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                Dispatcher.Invoke(() =>
                {
                    if (GitHubStatusText != null) GitHubStatusText.Text = Translate.RepositoryNotFound;
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (GitHubStatusText != null) GitHubStatusText.Text = string.Format(Translate.ReadmeLoading, owner, repo);
            });

            string? markdown = null;

            // 1. Raw HEAD から取得試行 (APIレート制限を回避)
            try
            {
                var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/README.md";
                var res = await _http.GetAsync(rawUrl);
                if (res.IsSuccessStatusCode)
                {
                    markdown = await res.Content.ReadAsStringAsync();
                }
                else
                {
                    // master/main も試行
                    var mainUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/README.md";
                    var resMain = await _http.GetAsync(mainUrl);
                    if (resMain.IsSuccessStatusCode) markdown = await resMain.Content.ReadAsStringAsync();
                }
            }
            catch { }

            // 2. API経由試行
            if (string.IsNullOrEmpty(markdown))
            {
                try
                {
                    var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/readme";
                    var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    req.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub/1.0");
                    var res = await _http.SendAsync(req);
                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("content", out var cProp))
                        {
                            var content = cProp.GetString();
                            var enc = doc.RootElement.TryGetProperty("encoding", out var eProp) ? eProp.GetString() : "base64";
                            if (!string.IsNullOrEmpty(content))
                            {
                                markdown = enc == "base64"
                                    ? Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "").Replace("\r", "")))
                                    : content;
                            }
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(markdown))
            {
                Dispatcher.Invoke(() =>
                {
                    if (GitHubStatusText != null) GitHubStatusText.Text = Translate.ReadmeNotFound;
                });
                return;
            }

            string html = ConvertGitHubMarkdownToHtml(markdown, owner, repo);

            Dispatcher.Invoke(() =>
            {
                if (GitHubStatusText != null) GitHubStatusText.Text = $"GitHub: {owner}/{repo}";
                if (GitHubReadmeBrowser != null) GitHubReadmeBrowser.NavigateToString(html);
            });
        }

        // =========================================================
        // BOOTH 商品説明文 取得
        // =========================================================
        private async Task LoadBoothDescriptionAsync()
        {
            if (_boothLoaded) return;
            _boothLoaded = true;

            string? boothUrl = _plugin.BoothUrl;
            if (string.IsNullOrEmpty(boothUrl))
            {
                Dispatcher.Invoke(() =>
                {
                    if (BoothStatusText != null) BoothStatusText.Text = Translate.BoothUrlNotFound;
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (BoothStatusText != null) BoothStatusText.Text = string.Format(Translate.BoothDescriptionLoading, boothUrl);
            });

            try
            {
                var html = await _http.GetStringAsync(boothUrl);
                string descriptionHtml = ExtractBoothDescription(html, boothUrl);

                Dispatcher.Invoke(() =>
                {
                    if (BoothStatusText != null) BoothStatusText.Text = $"BOOTH: {boothUrl}";
                    if (BoothBrowser != null) BoothBrowser.NavigateToString(descriptionHtml);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (BoothStatusText != null) BoothStatusText.Text = string.Format(Translate.BoothDescriptionLoadFailed, ex.Message);
                });
            }
        }

        private static string ExtractBoothDescription(string pageHtml, string boothUrl)
        {
            // typography は出品者プロフィールにも使われるため、商品説明専用の要素だけを対象にする。
            // また説明本文には入れ子の div が含まれるので、最初の </div> で切らず対応する閉じタグまで取得する。
            string? bodyContent = ExtractBoothElementByClass(
                pageHtml,
                "js-item-description",
                "item-description__content",
                "js-item-description-content",
                "item-description");

            if (string.IsNullOrWhiteSpace(bodyContent))
            {
                // og:description からフォールバック
                var ogMatch = Regex.Match(pageHtml, @"<meta[^>]*property=""og:description""[^>]*content=""([^""]*)""", RegexOptions.IgnoreCase);
                if (ogMatch.Success)
                {
                    bodyContent = $"<p>{System.Web.HttpUtility.HtmlEncode(ogMatch.Groups[1].Value).Replace("\n", "<br/>")}</p>";
                }
                else
                {
                    bodyContent = $"<p>{System.Web.HttpUtility.HtmlEncode(Translate.BoothDescriptionUnavailable)}</p>";
                }
            }

            // BOOTH 内の相対画像URLを絶対URLに補正
            bodyContent = Regex.Replace(bodyContent, @"src=""(//[^""]+)""", "src=\"https:$1\"", RegexOptions.IgnoreCase);
            bodyContent = Regex.Replace(bodyContent, @"src=""(/[^""]+)""", "src=\"https://booth.pm$1\"", RegexOptions.IgnoreCase);

            return BuildStandardHtml(bodyContent, Translate.BoothDescription, preserveLineBreaks: true);
        }

        private static string? ExtractBoothElementByClass(string pageHtml, params string[] classNames)
        {
            foreach (var className in classNames)
            {
                var start = Regex.Match(
                    pageHtml,
                    $@"<div\b[^>]*\bclass\s*=\s*([""'])[^>]*\b{Regex.Escape(className)}\b[^>]*\1[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!start.Success) continue;

                var tags = Regex.Matches(pageHtml.Substring(start.Index), @"</?div\b[^>]*>", RegexOptions.IgnoreCase);
                int depth = 0;
                foreach (Match tag in tags)
                {
                    if (tag.Value.StartsWith("</", StringComparison.Ordinal))
                    {
                        if (--depth == 0)
                        {
                            int contentStart = start.Index + start.Length;
                            int closingTagIndex = start.Index + tag.Index;
                            return pageHtml.Substring(contentStart, closingTagIndex - contentStart);
                        }
                    }
                    else
                    {
                        depth++;
                    }
                }
            }

            return null;
        }

        // =========================================================
        // Markdown → HTML レンダラー (GitHub画像補正つき)
        // =========================================================
        private static string ConvertGitHubMarkdownToHtml(string markdown, string owner, string repo)
        {
            string rawBase = $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/";

            var sb = new StringBuilder();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inCodeBlock = false;
            string codeLang = "";
            var codeBuffer = new StringBuilder();

            bool inTable = false;
            var tableBuffer = new StringBuilder();

            foreach (var rawLine in lines)
            {
                string line = rawLine;

                // コードブロック (```)
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        inCodeBlock = false;
                        sb.AppendLine($"<pre><code>{System.Web.HttpUtility.HtmlEncode(codeBuffer.ToString())}</code></pre>");
                        codeBuffer.Clear();
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeLang = line.TrimStart().Substring(3).Trim();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBuffer.AppendLine(line);
                    continue;
                }

                // 表 (Table: | a | b |)
                if (line.Trim().StartsWith("|") && line.Trim().EndsWith("|"))
                {
                    if (!inTable)
                    {
                        inTable = true;
                        tableBuffer.Clear();
                        tableBuffer.AppendLine("<table border='1' cellspacing='0' cellpadding='6'>");
                    }

                    // ヘッダーセパレータ (|---|---|) は無視
                    if (Regex.IsMatch(line, @"^\|[\s\-:|]+\|$"))
                    {
                        continue;
                    }

                    var cells = line.Trim().Trim('|').Split('|');
                    tableBuffer.Append("<tr>");
                    foreach (var cell in cells)
                    {
                        string formattedCell = FormatInlineMarkdown(cell.Trim(), rawBase);
                        tableBuffer.Append($"<td>{formattedCell}</td>");
                    }
                    tableBuffer.AppendLine("</tr>");
                    continue;
                }
                else if (inTable)
                {
                    inTable = false;
                    tableBuffer.AppendLine("</table>");
                    sb.Append(tableBuffer.ToString());
                }

                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                // 見出し
                if (line.StartsWith("###### "))
                {
                    sb.AppendLine($"<h6>{FormatInlineMarkdown(line.Substring(7), rawBase)}</h6>");
                }
                else if (line.StartsWith("##### "))
                {
                    sb.AppendLine($"<h5>{FormatInlineMarkdown(line.Substring(6), rawBase)}</h5>");
                }
                else if (line.StartsWith("#### "))
                {
                    sb.AppendLine($"<h4>{FormatInlineMarkdown(line.Substring(5), rawBase)}</h4>");
                }
                else if (line.StartsWith("### "))
                {
                    sb.AppendLine($"<h3>{FormatInlineMarkdown(line.Substring(4), rawBase)}</h3>");
                }
                else if (line.StartsWith("## "))
                {
                    sb.AppendLine($"<h2>{FormatInlineMarkdown(line.Substring(3), rawBase)}</h2>");
                }
                else if (line.StartsWith("# "))
                {
                    sb.AppendLine($"<h1>{FormatInlineMarkdown(line.Substring(2), rawBase)}</h1>");
                }
                // 水平線
                else if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    sb.AppendLine("<hr/>");
                }
                // リスト
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                {
                    sb.AppendLine($"<li>{FormatInlineMarkdown(trimmed.Substring(2), rawBase)}</li>");
                }
                // 番号付きリスト
                else if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
                {
                    var match = Regex.Match(trimmed, @"^\d+\.\s+(.*)$");
                    sb.AppendLine($"<li>{FormatInlineMarkdown(match.Groups[1].Value, rawBase)}</li>");
                }
                // 引用
                else if (trimmed.StartsWith("> "))
                {
                    sb.AppendLine($"<blockquote>{FormatInlineMarkdown(trimmed.Substring(2), rawBase)}</blockquote>");
                }
                // 通常段落
                else
                {
                    sb.AppendLine($"<p>{FormatInlineMarkdown(line, rawBase)}</p>");
                }
            }

            if (inTable)
            {
                tableBuffer.AppendLine("</table>");
                sb.Append(tableBuffer.ToString());
            }

            return BuildStandardHtml(sb.ToString(), $"{owner}/{repo} README.md");
        }

        private static string FormatInlineMarkdown(string text, string rawBase)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // 1. 画像: ![alt](url)
            text = Regex.Replace(text, @"!\[([^\]]*)\]\(([^)]+)\)", m =>
            {
                string alt = m.Groups[1].Value;
                string url = m.Groups[2].Value.Trim();
                if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("//"))
                {
                    url = rawBase + url.TrimStart('/', '.');
                }
                return $"<img src=\"{url}\" alt=\"{System.Web.HttpUtility.HtmlEncode(alt)}\" style=\"max-width:100%; height:auto;\" />";
            });

            // 2. リンク: [text](url)
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", m =>
            {
                string label = m.Groups[1].Value;
                string url = m.Groups[2].Value.Trim();
                return $"<a href=\"{url}\" target=\"_blank\">{label}</a>";
            });

            // 3. インラインコード: `code`
            text = Regex.Replace(text, @"`([^`]+)`", "<code>$1</code>");

            // 4. 太字: **text** または __text__
            text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__([^_]+)__", "<strong>$1</strong>");

            // 5. イタリック: *text*
            text = Regex.Replace(text, @"\*([^*]+)\*", "<em>$1</em>");

            // HTMLタグ直接記述の画像・リンクの相対パス補正
            text = Regex.Replace(text, @"<img\s+([^>]*?)src=""([^""]+)""([^>]*?)>", m =>
            {
                string pre = m.Groups[1].Value;
                string src = m.Groups[2].Value;
                string post = m.Groups[3].Value;
                if (!src.StartsWith("http://") && !src.StartsWith("https://") && !src.StartsWith("//"))
                {
                    src = rawBase + src.TrimStart('/', '.');
                }
                return $"<img {pre}src=\"{src}\"{post} style=\"max-width:100%; height:auto;\">";
            }, RegexOptions.IgnoreCase);

            return text;
        }

        private static string BuildStandardHtml(string bodyContent, string title, bool preserveLineBreaks = false)
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
            string tableStripe = isDark ? "#252526" : "#F6F8FA";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'>");
            sb.AppendLine("<meta http-equiv='X-UA-Compatible' content='IE=edge'/>");
            sb.AppendLine($"<title>{System.Web.HttpUtility.HtmlEncode(title)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine($"html {{ color-scheme: {(isDark ? "dark" : "light")}; }}");
            if (isDark)
            {
                sb.AppendLine("html, body {");
                sb.AppendLine("  scrollbar-face-color: #454545;");
                sb.AppendLine("  scrollbar-track-color: #1E1E1E;");
                sb.AppendLine("  scrollbar-arrow-color: #CCCCCC;");
                sb.AppendLine("  scrollbar-highlight-color: #454545;");
                sb.AppendLine("  scrollbar-3dlight-color: #1E1E1E;");
                sb.AppendLine("  scrollbar-shadow-color: #1E1E1E;");
                sb.AppendLine("  scrollbar-darkshadow-color: #1E1E1E;");
                sb.AppendLine("  scrollbar-base-color: #1E1E1E;");
                sb.AppendLine("  scrollbar-color: #555555 #1E1E1E;");
                sb.AppendLine("  scrollbar-width: thin;");
                sb.AppendLine("}");
                sb.AppendLine("::-webkit-scrollbar { width: 10px; height: 10px; background: #1E1E1E; }");
                sb.AppendLine("::-webkit-scrollbar-thumb { background: #454545; border: 1px solid #1E1E1E; }");
                sb.AppendLine("::-webkit-scrollbar-thumb:hover { background: #555555; }");
                sb.AppendLine("::-webkit-scrollbar-track { background: #1E1E1E; }");
            }
            else
            {
                sb.AppendLine("html, body {");
                sb.AppendLine("  scrollbar-face-color: #C6C6C6;");
                sb.AppendLine("  scrollbar-track-color: #F0F0F0;");
                sb.AppendLine("  scrollbar-arrow-color: #333333;");
                sb.AppendLine("  scrollbar-highlight-color: #FFFFFF;");
                sb.AppendLine("  scrollbar-3dlight-color: #D0D0D0;");
                sb.AppendLine("  scrollbar-shadow-color: #A0A0A0;");
                sb.AppendLine("  scrollbar-darkshadow-color: #808080;");
                sb.AppendLine("  scrollbar-base-color: #F0F0F0;");
                sb.AppendLine("  scrollbar-color: #A0A0A0 #F0F0F0;");
                sb.AppendLine("  scrollbar-width: thin;");
                sb.AppendLine("}");
                sb.AppendLine("::-webkit-scrollbar { width: 10px; height: 10px; background: #F0F0F0; }");
                sb.AppendLine("::-webkit-scrollbar-thumb { background: #C6C6C6; border: 1px solid #F0F0F0; }");
                sb.AppendLine("::-webkit-scrollbar-thumb:hover { background: #A0A0A0; }");
                sb.AppendLine("::-webkit-scrollbar-track { background: #F0F0F0; }");
            }
            sb.AppendLine($"body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Meiryo', sans-serif; font-size: 13px; line-height: 1.6; padding: 16px; margin: 0; background-color: {bgColor}; color: {textColor}; }}");
            sb.AppendLine($"h1, h2, h3, h4, h5, h6 {{ margin-top: 24px; margin-bottom: 12px; font-weight: bold; line-height: 1.25; border-bottom: 1px solid {headingBorder}; padding-bottom: 6px; color: {textColor}; }}");
            sb.AppendLine("h1 { font-size: 20px; } h2 { font-size: 17px; } h3 { font-size: 15px; }");
            sb.AppendLine("p { margin-top: 0; margin-bottom: 12px; }");
            if (preserveLineBreaks)
                sb.AppendLine(".booth-description { white-space: pre-line; }");
            sb.AppendLine($"a {{ color: {linkColor}; text-decoration: none; }}");
            sb.AppendLine("a:hover { text-decoration: underline; }");
            sb.AppendLine("img { max-width: 100% !important; height: auto; display: inline-block; margin: 6px 0; }");
            sb.AppendLine($"pre {{ background-color: {codeBg}; border: 1px solid {codeBorder}; padding: 12px; overflow-x: auto; font-family: Consolas, monospace; font-size: 12px; color: {textColor}; }}");
            sb.AppendLine($"code {{ background-color: {codeBg}; padding: 2px 5px; font-family: Consolas, monospace; font-size: 12px; color: {textColor}; }}");
            sb.AppendLine("pre code { background: none; padding: 0; }");
            sb.AppendLine($"blockquote {{ padding: 0 1em; color: {blockquoteColor}; border-left: 0.25em solid {blockquoteBorder}; margin: 0 0 16px 0; }}");
            sb.AppendLine($"table {{ border-collapse: collapse; width: 100%; margin-bottom: 16px; border: 1px solid {codeBorder}; }}");
            sb.AppendLine($"table th, table td {{ padding: 6px 12px; border: 1px solid {codeBorder}; color: {textColor}; }}");
            sb.AppendLine($"table tr:nth-child(2n) {{ background-color: {tableStripe}; }}");
            sb.AppendLine($"hr {{ height: 1px; background-color: {headingBorder}; border: none; margin: 20px 0; }}");
            sb.AppendLine("li { margin-bottom: 4px; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine(preserveLineBreaks ? $"<div class='booth-description'>{bodyContent}</div>" : bodyContent);
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void AddToSelection_Click(object sender, RoutedEventArgs e)
        {
            _plugin.IsSelected = true;
            _owner?.UpdatePortalSelectionBar();
            Close();
        }

        private void OpenVersionSelect_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VersionSelectDialog(_plugin, _owner);
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
