using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    /// <summary>アップデート一覧ダイアログ用アイテム（INotifyPropertyChanged対応）</summary>
    public class YmmUpdateDisplayItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Title { get; set; } = string.Empty;
        public string DateText { get; set; } = string.Empty;
        public string ArticleUrl { get; set; } = string.Empty;

        private string _cleanDescription = string.Empty;
        public string CleanDescription
        {
            get => _cleanDescription;
            set { _cleanDescription = value; OnPropertyChanged(nameof(CleanDescription)); }
        }
    }

    public partial class YmmUpdateDialog : Window
    {
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// バージョン番号から GitHub Raw URL を組み立てる。
        /// https://raw.githubusercontent.com/manju-summoner/manjubox.posts/master/ymm4/release/{version}.md
        /// </summary>
        private static string MdUrl(string version)
            => $"https://raw.githubusercontent.com/manju-summoner/manjubox.posts/master/ymm4/release/{version}.md";

        static YmmUpdateDialog()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub/1.0");
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        public YmmUpdateDialog(
            string currentVersion,
            string latestVersion,
            List<YmmUpdateItem> updates)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);

            bool isUpToDate = updates.Count == 0;
            if (isUpToDate)
            {
                CurrentVersionText.Visibility = Visibility.Collapsed;
                LatestVersionText.Visibility = Visibility.Collapsed;
                UpToDateText.Text = $"v{currentVersion}  ✓ 最新版です";
                UpToDateText.Visibility = Visibility.Visible;
            }
            else
            {
                CurrentVersionText.Text = $"現在: v{currentVersion}";
                LatestVersionText.Text = $"最新: v{latestVersion}";
            }

            // CDATA の内容を先行表示
            var displayItems = new ObservableCollection<YmmUpdateDisplayItem>();
            foreach (var u in updates)
            {
                displayItems.Add(new YmmUpdateDisplayItem
                {
                    Title = u.Title,
                    DateText = u.PublishedAt != default
                                       ? u.PublishedAt.ToString("yyyy/MM/dd") : string.Empty,
                    CleanDescription = CleanCdata(u.Description) + "\n\n(詳細を読み込み中...)",
                    ArticleUrl = u.ArticleUrl,
                });
            }
            UpdateList.ItemsSource = displayItems;

            // Loaded 後に Markdown を取得
            Loaded += async (s, e) => await FetchMarkdownDetailsAsync(displayItems, updates);
        }

        private async Task FetchMarkdownDetailsAsync(
            ObservableCollection<YmmUpdateDisplayItem> items,
            List<YmmUpdateItem> updates)
        {
            for (int i = 0; i < updates.Count && i < items.Count; i++)
            {
                var update = updates[i];
                var item = items[i];

                // バージョン番号を取得（null チェック）
                string? ver = update.Version?.ToString(); // e.g. "4.52.0.0"
                if (string.IsNullOrEmpty(ver)) continue;

                string mdUrl = MdUrl(ver);

                try
                {
                    string md = await _http.GetStringAsync(mdUrl);
                    string parsed = ParseMarkdown(md, update.Title);

                    Dispatcher.Invoke(() =>
                    {
                        item.CleanDescription = string.IsNullOrWhiteSpace(parsed)
                            ? CleanCdata(update.Description)
                            : parsed;
                    });
                }
                catch
                {
                    // GitHub から取得できない場合は CDATA をそのまま使用
                    Dispatcher.Invoke(() =>
                    {
                        item.CleanDescription = CleanCdata(update.Description);
                    });
                }
            }
        }

        // ─────────────────────────────────────────────────
        // Markdown パース
        //
        // 想定フォーマット（mankubox.posts の release md）:
        //   # ゆっくりMovieMaker v4.52.0.0 を公開しました
        //   ## 追加
        //   - 項目
        //     - ネスト項目
        //   ## 修正
        //   - 項目
        //   ...
        // ─────────────────────────────────────────────────

        private static string ParseMarkdown(string md, string title)
        {
            if (string.IsNullOrWhiteSpace(md)) return string.Empty;

            var sb = new StringBuilder();
            var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            string? currentSection = null;
            bool inContent = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd();

                // h1（タイトル行）: 「公開しました」の見出し以降をコンテンツとみなす
                if (line.StartsWith("# "))
                {
                    // タイトルが「公開しました」を含む行でコンテンツ開始
                    if (line.Contains("公開しました") || line.Contains("を公開"))
                        inContent = true;
                    continue;
                }

                if (!inContent) continue;

                // h2（セクション見出し）
                if (line.StartsWith("## "))
                {
                    currentSection = line.Substring(3).Trim();
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine($"■ {currentSection}");
                    continue;
                }

                // h3（サブセクション）
                if (line.StartsWith("### "))
                {
                    string sub = line.Substring(4).Trim();
                    sb.AppendLine($"  □ {sub}");
                    continue;
                }

                // リスト項目（- または * ）
                var listMatch = Regex.Match(line, @"^(\s*)([-*])\s+(.+)$");
                if (listMatch.Success)
                {
                    int indentLen = listMatch.Groups[1].Length;
                    string text = listMatch.Groups[3].Value;

                    // Markdown インラインを除去（**bold** `code` [link](url) など）
                    text = CleanMarkdownInline(text);

                    // インデント深さに応じた記号
                    string bullet = indentLen == 0 ? "・" : "  ".PadRight(indentLen) + "└ ";
                    sb.AppendLine($"{bullet}{text}");
                    continue;
                }

                // 空行
                if (string.IsNullOrWhiteSpace(line))
                {
                    // セクション内の空行は1つにまとめる
                    continue;
                }

                // その他テキスト（段落）
                string para = CleanMarkdownInline(line).Trim();
                if (!string.IsNullOrEmpty(para))
                    sb.AppendLine(para);
            }

            return sb.ToString().Trim();
        }

        /// <summary>Markdown インライン記法を除去してプレーンテキストにする</summary>
        private static string CleanMarkdownInline(string text)
        {
            // [text](url) → text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            // **bold** / *italic* / __bold__ / _italic_
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = Regex.Replace(text, @"__(.+?)__", "$1");
            text = Regex.Replace(text, @"_(.+?)_", "$1");
            // `code`
            text = Regex.Replace(text, @"`(.+?)`", "$1");
            // HTML タグ（稀に混在）
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            return text.Trim();
        }

        private static string CleanCdata(string desc)
        {
            // HTML タグ除去
            string s = Regex.Replace(desc, "<.*?>", string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = s.Replace("&amp;", "&")
                 .Replace("&lt;", "<")
                 .Replace("&gt;", ">")
                 .Replace("&nbsp;", " ")
                 .Replace("&#160;", " ");
            // 連続スペースを整理
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}