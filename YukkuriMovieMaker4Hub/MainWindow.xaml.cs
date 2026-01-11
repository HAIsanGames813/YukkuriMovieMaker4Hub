using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using static YukkuriMovieMaker4Hub.MainWindow;

namespace YukkuriMovieMaker4Hub
{
    public enum AppTheme
    {
        Windows,
        Light,
        Dark,
        Black
    }

    public class AppSettings
    {
        [JsonPropertyName("fontFamily")]
        public string FontFamily { get; set; } = "Segoe UI";
        [JsonPropertyName("theme")]
        public AppTheme Theme { get; set; } = AppTheme.Windows;
        [JsonPropertyName("instances")]
        public List<InstanceInfo> Instances { get; set; } = new List<InstanceInfo>();
        [JsonPropertyName("projectDirectories")]
        public List<string> ProjectDirectories { get; set; } = new List<string>();
        [JsonPropertyName("closeOnLaunch")]
        public bool CloseOnLaunch { get; set; } = false;
        [JsonPropertyName("githubToken")]
        public string GitHubToken { get; set; } = string.Empty;
        
        [JsonPropertyName("lastSelectedInstanceId")]
        public string? LastSelectedInstanceId { get; set; }
    }

    public class InstanceInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        private string? _name;
        [JsonPropertyName("name")]
        public string Name { get => _name ?? string.Empty; set { _name = value; OnPropertyChanged(nameof(Name)); } }
        private string? _iconPath;
        [JsonPropertyName("iconPath")]
        public string? IconPath { get => _iconPath; set { _iconPath = value; OnPropertyChanged(nameof(IconPath)); OnPropertyChanged(nameof(IconImage)); } }
        [JsonIgnore]
        public ImageSource? IconImage
        {
            get
            {
                if (!string.IsNullOrEmpty(IconPath) && File.Exists(IconPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(IconPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                    catch { return null; }
                }
                return null;
            }
        }
        [JsonPropertyName("exePath")]
        public string ExePath { get; set; } = string.Empty;
        [JsonIgnore]
        public string RootDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.GetDirectoryName(ExePath) ?? string.Empty;
        [JsonIgnore]
        public string PluginDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.Combine(RootDirectory, "user", "plugin");
        [JsonIgnore]
        public string InstallerPath => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.Combine(RootDirectory, "Resources", "bin", "Installer", "YukkuriMovieMaker.Plugin.Installer.exe");
    }

    public class ProjectFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public long FileSize { get; set; }
        public string Extension { get; set; } = string.Empty;
        public string DisplaySize => $"{FileSize / 1024.0 / 1024.0:F2} MB";
        public string DisplayDate => LastWriteTime.ToString("yyyy/MM/dd HH:mm");
    }

    public class PluginLink
    {
        public string Url { get; set; } = string.Empty;
        public string Label => GetLabel(Url);
        public string FaviconUrl => string.IsNullOrEmpty(Url) ? string.Empty : $"https://www.google.com/s2/favicons?domain={new Uri(Url).Host}&sz=32";
        private string GetLabel(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLower();
                if (host.Contains("github.com")) return "GitHub";
                if (host.Contains("twitter.com") || host.Contains("x.com")) return "X (Twitter)";
                if (host.Contains("booth.pm")) return "BOOTH";
                if (host.Contains("youtube.com") || host.Contains("youtu.be")) return "YouTube";
                if (host.Contains("nicovideo.jp")) return "ニコニコ動画";
                if (host.Contains("ymm4-info.net")) return "YMM4サイト";
                return host;
            }
            catch { return "配布サイト"; }
        }
    }

    public class GitHubReleaseDetail
    {
        public string TagName { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public bool Prerelease { get; set; }
    }

    public class PluginCatalogItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string? Url { get; set; }
        public List<string> Links { get; set; } = new List<string>();
        private ObservableCollection<GitHubReleaseDetail> _releases = new ObservableCollection<GitHubReleaseDetail>();
        public ObservableCollection<GitHubReleaseDetail> Releases { get => _releases; set { _releases = value; OnPropertyChanged(nameof(Releases)); } }
        private GitHubReleaseDetail? _selectedVersion;
        public GitHubReleaseDetail? SelectedVersion { get => _selectedVersion; set { _selectedVersion = value; OnPropertyChanged(nameof(SelectedVersion)); } }

        public string LatestVersionName
        {
            get
            {
                if (!IsEnabled) return "配布終了";
                bool hasGitHub = (Url != null && Url.Contains("github.com")) || (Links != null && Links.Any(l => l != null && l.Contains("github.com")));
                if (!hasGitHub) return "情報なし";
                if (Releases == null || Releases.Count == 0) return "取得中";
                return Releases[0].TagName;
            }
        }

        public List<PluginLink> AllLinks
        {
            get
            {
                var list = new List<PluginLink>();
                if (!string.IsNullOrEmpty(Url)) try { list.Add(new PluginLink { Url = Url }); } catch { }
                foreach (var l in Links)
                {
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    try { list.Add(new PluginLink { Url = l }); } catch { }
                }
                return list.GroupBy(x => x.Url).Select(g => g.First()).ToList();
            }
        }
    }

    public class LocalPluginInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private string _fullPath = string.Empty;
        public string FullPath { get => _fullPath; set { _fullPath = value; OnPropertyChanged(nameof(FullPath)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(IsEnabled)); } }
        public bool IsDirectory { get; set; }
        private string? _displayName;
        public string DisplayName { get => !string.IsNullOrEmpty(_displayName) ? _displayName : Path.GetFileName(FullPath); set { _displayName = value; OnPropertyChanged(nameof(DisplayName)); } }
        public bool IsEnabled => IsDirectory ? !Path.GetFileName(FullPath).StartsWith("_") : !Path.GetFileName(FullPath).EndsWith(".disabled");
    }

    public class FontItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public bool IsJapanese { get; set; }
        public FontFamily Family { get; set; } = new FontFamily("Segoe UI");
    }

    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? "有効" : "無効";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Brushes.LightGreen : Brushes.Gray;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class IsNotNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class SettingsManager
    {
        private readonly string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        public AppSettings Load()
        {
            if (!File.Exists(_path)) return new AppSettings();
            try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings(); }
            catch { return new AppSettings(); }
        }
        public void Save(AppSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_path, json);
        }
    }

    public class PluginTypeFilterItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public string Name { get; set; } = "";
        public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private SettingsManager _settingsManager = new SettingsManager();
        private AppSettings _currentSettings = new AppSettings();
        private HttpClient _http = new HttpClient();
        public ObservableCollection<InstanceInfo> Instances { get; set; } = new ObservableCollection<InstanceInfo>();
        public ObservableCollection<LocalPluginInfo> LocalPlugins { get; set; } = new ObservableCollection<LocalPluginInfo>();
        public ObservableCollection<PluginCatalogItem> OnlinePlugins { get; set; } = new ObservableCollection<PluginCatalogItem>();
        public ObservableCollection<string> ProjectDirectories { get; set; } = new ObservableCollection<string>();
        private List<ProjectFileItem> _allProjects = new List<ProjectFileItem>();
        public ObservableCollection<ProjectFileItem> FilteredProjects { get; set; } = new ObservableCollection<ProjectFileItem>();
        private string _projectSearchText = string.Empty;
        public string ProjectSearchText { get => _projectSearchText; set { _projectSearchText = value; ApplyProjectFilter(); OnPropertyChanged(nameof(ProjectSearchText)); } }
        private bool _showYmmp = true;
        public bool ShowYmmp { get => _showYmmp; set { _showYmmp = value; ApplyProjectFilter(); OnPropertyChanged(nameof(ShowYmmp)); } }
        private bool _showYmmpx = true;
        public bool ShowYmmpx { get => _showYmmpx; set { _showYmmpx = value; ApplyProjectFilter(); OnPropertyChanged(nameof(ShowYmmpx)); } }
        private bool _showYmmx = true;
        public bool ShowYmmx { get => _showYmmx; set { _showYmmx = value; ApplyProjectFilter(); OnPropertyChanged(nameof(ShowYmmx)); } }
        private List<FontItem> _allFonts = new List<FontItem>();

        private string _localPluginSearchText = string.Empty;
        public string LocalPluginSearchText
        {
            get => _localPluginSearchText;
            set { _localPluginSearchText = value; ApplyLocalPluginFilter(); OnPropertyChanged(nameof(LocalPluginSearchText)); }
        }

        private string _onlinePluginSearchText = string.Empty;
        public string OnlinePluginSearchText
        {
            get => _onlinePluginSearchText;
            set { _onlinePluginSearchText = value; ApplyOnlinePluginFilter(); OnPropertyChanged(nameof(OnlinePluginSearchText)); }
        }

        private string _selectedPluginType = "すべて";
        public string SelectedPluginType
        {
            get => _selectedPluginType;
            set { _selectedPluginType = value; ApplyOnlinePluginFilter(); OnPropertyChanged(nameof(SelectedPluginType)); }
        }

        private ObservableCollection<FontItem> _filteredFonts = new ObservableCollection<FontItem>();
        public ObservableCollection<FontItem> FilteredFonts
        {
            get => _filteredFonts;
            set { _filteredFonts = value; OnPropertyChanged(nameof(FilteredFonts)); }
        }

        public ObservableCollection<PluginTypeFilterItem> PluginTypeFilters { get; } = new ObservableCollection<PluginTypeFilterItem>();

        private void InitializePluginFilters()
        {
            var types = new[] { "映像エフェクト", "音声エフェクト", "音声合成", "動画出力", "動画読み込み", "音声読み込み", "画像読み込み", "場面切り替え", "図形", "立ち絵", "ツール", "テキスト補完", "模様", "文字起こし", "その他", "配布終了" };
            foreach (var t in types)
            {
                var item = new PluginTypeFilterItem { Name = t, IsSelected = true };
                item.PropertyChanged += (s, e) => ApplyOnlinePluginFilter();
                PluginTypeFilters.Add(item);
            }
        }
        private List<LocalPluginInfo> _allLocalPlugins = new List<LocalPluginInfo>();
        private List<PluginCatalogItem> _allOnlinePlugins = new List<PluginCatalogItem>();
        private string _fontSearchText = string.Empty;
        public string FontSearchText { get => _fontSearchText; set { _fontSearchText = value; ApplyFontFilter(); OnPropertyChanged(nameof(FontSearchText)); } }
        private bool _isJapaneseOnly = false;
        public bool IsJapaneseOnly { get => _isJapaneseOnly; set { _isJapaneseOnly = value; ApplyFontFilter(); OnPropertyChanged(nameof(IsJapaneseOnly)); } }
        public Array ThemeModes => Enum.GetValues(typeof(AppTheme));
        private InstanceInfo? _selectedInstance;
        public InstanceInfo? SelectedInstance
        {
            get => _selectedInstance;
            set
            {
                _selectedInstance = value;
                OnPropertyChanged(nameof(SelectedInstance));
                if (value != null)
                {
                    _currentSettings.LastSelectedInstanceId = value.Id;
                    SaveAll();
                }
                RefreshLocalPlugins();
                RefreshRecentProjects();
            }
        }
        private PluginCatalogItem? _selectedOnlinePlugin;
        public PluginCatalogItem? SelectedOnlinePlugin { get => _selectedOnlinePlugin; set { _selectedOnlinePlugin = value; OnPropertyChanged(nameof(SelectedOnlinePlugin)); if (value != null) _ = LoadReleaseDetails(value); } }
        public FontItem? SelectedFontItem
        {
            get => FilteredFonts.FirstOrDefault(f => f.InternalName == _currentSettings.FontFamily);
            set { if (value != null) { _currentSettings.FontFamily = value.InternalName; ApplyTheme(); SaveAll(); OnPropertyChanged(nameof(SelectedFontItem)); } }
        }
        public AppTheme SelectedTheme { get => _currentSettings.Theme; set { _currentSettings.Theme = value; ApplyTheme(); SaveAll(); OnPropertyChanged(nameof(SelectedTheme)); } }
        public bool CloseOnLaunch
        {
            get => _currentSettings.CloseOnLaunch;
            set { _currentSettings.CloseOnLaunch = value; SaveAll(); OnPropertyChanged(nameof(CloseOnLaunch)); }
        }
        private string _lastSortField = "DisplayName";
        private ListSortDirection _lastSortDir = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();
            _currentSettings = _settingsManager.Load();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub/1.0");
            this.DataContext = this;
            InitializePluginFilters();
            InitializeFonts();
            foreach (var i in _currentSettings.Instances)
            {
                i.PropertyChanged += (s, e) => SaveAll();
                Instances.Add(i);
            }
            foreach (var p in _currentSettings.ProjectDirectories)
                ProjectDirectories.Add(p);
            SystemEvents.UserPreferenceChanged += (s, e) => { if (SelectedTheme == AppTheme.Windows) ApplyTheme(); };

            GitHubTokenBox.Password = _currentSettings.GitHubToken;

            ApplyTheme();

            // 以下の1行を追加
            RestoreLastSelection();

            RefreshRecentProjects();
        }
        private void GitHubTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _currentSettings.GitHubToken = pb.Password;
                SaveAll();
            }
        }
        private void RestoreLastSelection()
        {
            if (Instances.Count == 0) return;
            var lastId = _currentSettings.LastSelectedInstanceId;
            var target = Instances.FirstOrDefault(i => i.Id == lastId) ?? Instances[0];
            SelectedInstance = target;
        }
        private void InitializeFonts()
        {
            var jpCulture = XmlLanguage.GetLanguage("ja-jp");
            foreach (var ff in Fonts.SystemFontFamilies)
            {
                string name = ff.FamilyNames.TryGetValue(jpCulture, out var jpName) ? jpName : ff.Source;
                bool isJp = ff.FamilyNames.ContainsKey(jpCulture);
                _allFonts.Add(new FontItem { DisplayName = name, InternalName = ff.Source, IsJapanese = isJp, Family = ff });
            }
            _allFonts = _allFonts.OrderBy(f => f.DisplayName).ToList();
            ApplyFontFilter();
        }

        private void ApplyFontFilter()
        {
            var filtered = _allFonts.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(FontSearchText)) filtered = filtered.Where(f => f.DisplayName.IndexOf(FontSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            if (IsJapaneseOnly) filtered = filtered.Where(f => f.IsJapanese);
            FilteredFonts.Clear();
            foreach (var f in filtered) FilteredFonts.Add(f);
            OnPropertyChanged(nameof(SelectedFontItem));
        }

        private void ApplyTheme()
        {
            try
            {
                this.Resources["AppFont"] = new FontFamily(_currentSettings.FontFamily);
                var mode = SelectedTheme;
                if (mode == AppTheme.Windows)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var value = key?.GetValue("AppsUseLightTheme");
                    mode = (value is int i && i == 1) ? AppTheme.Light : AppTheme.Dark;
                }
                switch (mode)
                {
                    case AppTheme.Light:
                        SetThemeColors("#FFFFFF", "#F0F0F0", "#E0E0E0", "#000000", "#555555", "#4acff0", "#DDDDDD");
                        break;
                    case AppTheme.Dark:
                        SetThemeColors("#252525", "#333333", "#444444", "#FFFFFF", "#AAAAAA", "#2E7D32", "#555555");
                        break;
                    case AppTheme.Black:
                        SetThemeColors("#000000", "#121212", "#1F1F1F", "#FFFFFF", "#888888", "#2E7D32", "#333333");
                        break;
                }
            }
            catch { }
        }

        private void SetThemeColors(string bg, string panel, string item, string text, string subText, string accent, string border)
        {
            this.Resources["ThemeBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            this.Resources["PanelBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(panel));
            this.Resources["ItemBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item));
            this.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
            this.Resources["SubTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(subText));
            this.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
            this.Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        }

        private void SaveAll()
        {
            _currentSettings.Instances = Instances.ToList();
            _currentSettings.ProjectDirectories = ProjectDirectories.ToList();
            _settingsManager.Save(_currentSettings);
        }

        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe" };
            if (dialog.ShowDialog() == true)
            {
                var newInstance = new InstanceInfo { Name = "新規インスタンス", ExePath = dialog.FileName };
                newInstance.PropertyChanged += (s, ee) => SaveAll();
                Instances.Add(newInstance);
                SaveAll();
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null) return;
            if (MessageBox.Show($"{SelectedInstance.Name} をHubから削除しますか？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Instances.Remove(SelectedInstance);
                SaveAll();
                SelectedInstance = null;
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedItem is TabItem ti && ti.Header != null)
            {
                string header = ti.Header.ToString() ?? "";
                if (header == "プラグインポータル") await LoadOnlinePlugins();
                if (header == "概要") RefreshRecentProjects();
            }
        }
        private void IssueToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/settings/tokens/new",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async Task LoadOnlinePlugins()
        {
            if (OnlinePlugins.Count > 0) return;
            try
            {
                var yml = await _http.GetStringAsync("https://manjubox.net/ymm4plugins.yml");
                var catalog = ParseYmm4PluginsYaml(yml);
                _allOnlinePlugins = catalog;
                ApplyOnlinePluginFilter();
            }
            catch (Exception ex) { MessageBox.Show("ポータル読み込み失敗: " + ex.Message); }
        }

        private Dictionary<string, (DateTime Time, List<GitHubReleaseDetail> Releases)> _releaseCache = new();


        private async Task LoadReleaseDetails(PluginCatalogItem plugin)
        {
            var githubUrl = FindGitHubRepositoryUrl(plugin);
            if (githubUrl == null) return;
            var match = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)");
            if (!match.Success) return;
            string owner = match.Groups[1].Value;
            string repo = match.Groups[2].Value.Replace(".git", "").TrimEnd('/');
            string cacheKey = $"{owner}/{repo}";

            // キャッシュが1時間以内の場合はそれを使用
            if (_releaseCache.TryGetValue(cacheKey, out var cache) && (DateTime.Now - cache.Time).TotalMinutes < 60)
            {
                plugin.Releases = new ObservableCollection<GitHubReleaseDetail>(cache.Releases);
                if (plugin.Releases.Count > 0) plugin.SelectedVersion = plugin.Releases[0];
                return;
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases");
                if (!string.IsNullOrEmpty(_currentSettings.GitHubToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentSettings.GitHubToken);
                }

                var response = await _http.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    MessageBox.Show("GitHub APIの取得制限に達しました。アカウントをお持ちでない場合は、しばらく時間を置いてから再度お試しください。");
                    return;
                }

                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseBody);
                var releaseList = new List<GitHubReleaseDetail>();
                response.EnsureSuccessStatusCode();
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    var tagName = rel.GetProperty("tag_name").GetString() ?? "";
                    var publishedAt = rel.GetProperty("published_at").GetDateTime();
                    var isPrerelease = rel.GetProperty("prerelease").GetBoolean();
                    var assets = rel.GetProperty("assets");
                    if (assets.GetArrayLength() > 0)
                    {
                        var firstAsset = assets[0];
                        releaseList.Add(new GitHubReleaseDetail
                        {
                            TagName = tagName,
                            PublishedAt = publishedAt,
                            Prerelease = isPrerelease,
                            BrowserDownloadUrl = firstAsset.GetProperty("browser_download_url").GetString() ?? "",
                            FileName = firstAsset.GetProperty("name").GetString() ?? ""
                        });
                    }
                }
                _releaseCache[cacheKey] = (DateTime.Now, releaseList);

                plugin.Releases = new ObservableCollection<GitHubReleaseDetail>(releaseList);
                if (plugin.Releases.Count > 0) plugin.SelectedVersion = plugin.Releases[0];
            }
            catch { }
        }

        private List<PluginCatalogItem> ParseYmm4PluginsYaml(string yaml)
        {
            var plugins = new List<PluginCatalogItem>();
            var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            PluginCatalogItem? current = null;
            bool inLinks = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (line.StartsWith("- "))
                {
                    current = new PluginCatalogItem();
                    plugins.Add(current);
                    inLinks = false;
                    var firstKeyValue = trimmed.Substring(2).Split(new[] { ':' }, 2);
                    if (firstKeyValue.Length == 2) ApplyYamlValue(current, firstKeyValue[0].Trim(), firstKeyValue[1].Trim(), ref inLinks);
                }
                else if (current != null)
                {
                    if (trimmed.StartsWith("-") && inLinks)
                        current.Links.Add(trimmed.Substring(1).Trim().Trim('\'', '\"'));
                    else
                    {
                        var keyValue = trimmed.Split(new[] { ':' }, 2);
                        if (keyValue.Length == 2) ApplyYamlValue(current, keyValue[0].Trim(), keyValue[1].Trim(), ref inLinks);
                    }
                }
            }
            return plugins;
        }

        private void ApplyYamlValue(PluginCatalogItem item, string key, string value, ref bool inLinks)
        {
            value = value.Trim('\'', '\"');
            switch (key.ToLower())
            {
                case "name": item.Name = value; inLinks = false; break;
                case "author": item.Author = value; inLinks = false; break;
                case "description": item.Description = value; inLinks = false; break;
                case "type":
                    item.Type = value;
                    inLinks = false;
                    break;
                case "isenabled":
                    if (bool.TryParse(value, out bool enabled)) item.IsEnabled = enabled;
                    inLinks = false;
                    break;
                case "url": item.Url = value; inLinks = false; break;
                case "links": inLinks = true; break;
                default: inLinks = false; break;
            }
        }

        private static string? FindGitHubRepositoryUrl(PluginCatalogItem? plugin)
        {
            if (plugin == null) return null;
            if (plugin.Url != null && plugin.Url.Contains("github.com")) return plugin.Url;
            return plugin.Links.FirstOrDefault(link => link?.Contains("github.com") ?? false);
        }

        private void OpenUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("リンクを開けませんでした: " + ex.Message); }
            }
        }

        private void RefreshLocalPlugins()
        {
            if (SelectedInstance == null || !Directory.Exists(SelectedInstance.PluginDirectory)) { LocalPlugins.Clear(); return; }

            var items = new List<LocalPluginInfo>();

            foreach (var dir in Directory.GetDirectories(SelectedInstance.PluginDirectory))
            {
                var info = new LocalPluginInfo { FullPath = dir, IsDirectory = true };
                info.DisplayName = GetPluginNameFromYml(dir);
                items.Add(info);
            }
            foreach (var file in Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll*"))
                items.Add(new LocalPluginInfo { FullPath = file, IsDirectory = false });

            _allLocalPlugins = items;
            ApplyLocalPluginFilter();
            ApplyCurrentSort();
        }

        private void RefreshRecentProjects()
        {
            _allProjects.Clear();
            foreach (var dir in ProjectDirectories)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(s => s.EndsWith(".ymmp") || s.EndsWith(".ymmpx") || s.EndsWith(".ymmx"));
                    foreach (var f in files)
                    {
                        var info = new FileInfo(f);
                        _allProjects.Add(new ProjectFileItem { Name = info.Name, FullPath = info.FullName, LastWriteTime = info.LastWriteTime, FileSize = info.Length, Extension = info.Extension.ToLower() });
                    }
                }
                catch { }
            }
            ApplyProjectFilter();
        }

        private void ApplyProjectFilter()
        {
            var filtered = _allProjects.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(ProjectSearchText)) filtered = filtered.Where(p => p.Name.IndexOf(ProjectSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!ShowYmmp) filtered = filtered.Where(p => p.Extension != ".ymmp");
            if (!ShowYmmpx)filtered = filtered.Where(p => p.Extension != ".ymmpx");
            if (!ShowYmmx) filtered = filtered.Where(p => p.Extension != ".ymmx");
            FilteredProjects.Clear();
            foreach (var p in filtered.OrderByDescending(x => x.LastWriteTime)) FilteredProjects.Add(p);
        }

        private string GetPluginNameFromYml(string dirPath)
        {
            try
            {
                var ymlFiles = Directory.GetFiles(dirPath, "*.yml", SearchOption.TopDirectoryOnly);
                foreach (var yml in ymlFiles)
                {
                    var lines = File.ReadAllLines(yml, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("name:")) return trimmed.Substring(5).Trim().Trim('\'', '\"');
                    }
                }
            }
            catch { }
            return Path.GetFileName(dirPath);
        }

        private bool EnsureYmmClosed()
        {
            var processes = Process.GetProcessesByName("YukkuriMovieMaker");
            if (processes.Length == 0) return true;
            if (MessageBox.Show("プラグイン操作のためにYMM4を終了しますか？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                foreach (var p in processes) { try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } catch { } }
                return true;
            }
            return false;
        }

        private void TogglePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn && btn.DataContext is LocalPluginInfo plugin)) return;
            ToggleOne(plugin);
        }

        private void ToggleOne(LocalPluginInfo plugin)
        {
            if (!EnsureYmmClosed()) return;
            string? parent = Path.GetDirectoryName(plugin.FullPath);
            if (parent == null) return;
            string oldName = Path.GetFileName(plugin.FullPath);
            string newName = plugin.IsDirectory ? (plugin.IsEnabled ? "_" + oldName : oldName.TrimStart('_')) : (plugin.IsEnabled ? oldName + ".disabled" : oldName.Replace(".disabled", ""));
            try
            {
                string newPath = Path.Combine(parent, newName);
                if (plugin.IsDirectory) Directory.Move(plugin.FullPath, newPath); else File.Move(plugin.FullPath, newPath);
                RefreshLocalPlugins();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BulkToggle_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalPluginList.SelectedItems.Cast<LocalPluginInfo>().ToList();
            if (selected.Count == 0 || !EnsureYmmClosed()) return;
            foreach (var p in selected) ToggleOne(p);
        }

        private void BulkDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalPluginList.SelectedItems.Cast<LocalPluginInfo>().ToList();
            if (selected.Count == 0) return;
            if (MessageBox.Show($"{selected.Count}件のプラグインを完全に削除しますか？\nこの操作は戻せません。", "削除の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!EnsureYmmClosed()) return;
            foreach (var p in selected) DeleteOne(p);
            RefreshLocalPlugins();
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn && btn.DataContext is LocalPluginInfo plugin)) return;
            if (MessageBox.Show($"{plugin.DisplayName} を完全に削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (!EnsureYmmClosed()) return;
                DeleteOne(plugin);
                RefreshLocalPlugins();
            }
        }

        private void DeleteOne(LocalPluginInfo p)
        {
            try { if (p.IsDirectory) Directory.Delete(p.FullPath, true); else File.Delete(p.FullPath); }
            catch (Exception ex) { MessageBox.Show($"{p.DisplayName}の削除に失敗: {ex.Message}"); }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header && header.Column != null)
            {
                string? headerText = header.Content?.ToString();
                string? field = null;
                if (headerText == "プラグイン名") field = "DisplayName";
                else if (headerText == "状態") field = "IsEnabled";
                if (field == null) return;
                if (_lastSortField == field) _lastSortDir = (_lastSortDir == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
                else { _lastSortField = field; _lastSortDir = ListSortDirection.Ascending; }
                ApplyCurrentSort();
            }
        }

        private void ApplyCurrentSort()
        {
            var list = LocalPlugins.ToList();
            if (_lastSortField == "DisplayName")
                list = _lastSortDir == ListSortDirection.Ascending ? list.OrderBy(p => p.DisplayName.TrimStart('_')).ToList() : list.OrderByDescending(p => p.DisplayName.TrimStart('_')).ToList();
            else if (_lastSortField == "IsEnabled")
                list = _lastSortDir == ListSortDirection.Ascending ? list.OrderBy(p => p.IsEnabled).ThenBy(p => p.DisplayName.TrimStart('_')).ToList() : list.OrderByDescending(p => p.IsEnabled).ThenBy(p => p.DisplayName.TrimStart('_')).ToList();
            LocalPlugins.Clear();
            foreach (var p in list) LocalPlugins.Add(p);
        }

        private void LaunchYmm(InstanceInfo? instance, string args = "")
        {
            var target = instance ?? SelectedInstance;
            if (target == null || !File.Exists(target.ExePath)) return;
            Process.Start(new ProcessStartInfo(target.ExePath, args) { WorkingDirectory = target.RootDirectory, UseShellExecute = true });

            if (CloseOnLaunch)
            {
                Application.Current.Shutdown();
            }
        }

        private void InstanceLaunch_Click(object sender, RoutedEventArgs e)
        {
            var instance = (sender as FrameworkElement)?.DataContext as InstanceInfo;
            LaunchYmm(instance);
        }

        private void InstanceLaunchLastProject_Click(object sender, RoutedEventArgs e)
        {
            var instance = (sender as FrameworkElement)?.DataContext as InstanceInfo;
            LaunchYmm(instance, "OpenLatestProject");
        }

        private void InstanceCreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            var instance = (sender as FrameworkElement)?.DataContext as InstanceInfo;
            LaunchYmm(instance, "CreateNewProject");
        }

        private void LaunchInstance_Click(object sender, RoutedEventArgs e)
        {
            LaunchYmm(SelectedInstance);
        }

        private void LaunchLastProject_Click(object sender, RoutedEventArgs e)
        {
            LaunchYmm(SelectedInstance, "OpenLatestProject");
        }

        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            LaunchYmm(SelectedInstance, "CreateNewProject");
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (SelectedInstance != null) Process.Start("explorer.exe", SelectedInstance.RootDirectory); }
        private void ChangeIcon_Click(object sender, RoutedEventArgs e) { if (SelectedInstance == null) return; var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.ico" }; if (d.ShowDialog() == true) SelectedInstance.IconPath = d.FileName; }
        private void AddProjectDir_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog() == true)
            {
                if (!ProjectDirectories.Contains(d.FolderName))
                {
                    ProjectDirectories.Add(d.FolderName);
                    SaveAll();
                    RefreshRecentProjects();
                }
            }
        }
        private void RemoveProjectDir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string dir)
            {
                ProjectDirectories.Remove(dir);
                SaveAll();
                RefreshRecentProjects();
            }
        }
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectGrid.SelectedItem is ProjectFileItem project)
                LaunchYmm(SelectedInstance, $"\"{project.FullPath}\"");
        }
        private void ResetProjectFilters_Click(object sender, RoutedEventArgs e)
        {
            ProjectSearchText = string.Empty;
            ShowYmmp = true;
            ShowYmmpx = true;
            ShowYmmx = true;
            RefreshRecentProjects();
        }
        private void ApplyLocalPluginFilter()
        {
            var filtered = _allLocalPlugins.Where(p =>
                string.IsNullOrWhiteSpace(LocalPluginSearchText) ||
                (p.DisplayName != null && p.DisplayName.Contains(LocalPluginSearchText, StringComparison.OrdinalIgnoreCase)));

            LocalPlugins.Clear();
            foreach (var p in filtered) LocalPlugins.Add(p);
        }

        private void ApplyOnlinePluginFilter()
        {
            if (_allOnlinePlugins == null) return;
            var query = OnlinePluginSearchText?.ToLower() ?? "";
            var selectedTypes = PluginTypeFilters.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            bool showDisabled = selectedTypes.Contains("配布終了");

            var filtered = _allOnlinePlugins.Where(p =>
                (string.IsNullOrWhiteSpace(query) ||
                (p.Name?.ToLower().Contains(query) ?? false) ||
                (p.Author?.ToLower().Contains(query) ?? false) ||
                (p.Description?.ToLower().Contains(query) ?? false)) &&
                ((p.IsEnabled && selectedTypes.Contains(string.IsNullOrEmpty(p.Type) ? "その他" : p.Type)) || (!p.IsEnabled && showDisabled))
            );

            OnlinePlugins.Clear();
            foreach (var p in filtered) OnlinePlugins.Add(p);
        }

        private void AllCheck_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in PluginTypeFilters) f.IsSelected = true;
        }

        private void NoneCheck_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in PluginTypeFilters) f.IsSelected = false;
        }
        private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOnlinePlugin?.SelectedVersion == null || SelectedInstance == null)
            {
                MessageBox.Show("インストールするバージョンと対象のインスタンスを選択してください。");
                return;
            }
            if (!File.Exists(SelectedInstance.InstallerPath))
            {
                MessageBox.Show($"指定されたインスタンスにインストーラーが見つかりません。\nパス: {SelectedInstance.InstallerPath}");
                return;
            }
            var release = SelectedOnlinePlugin.SelectedVersion;
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "YMM4Hub");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string savePath = Path.Combine(tempDir, release.FileName);
                using var request = new HttpRequestMessage(HttpMethod.Get, release.BrowserDownloadUrl);
                request.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub/1.0");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(savePath))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
                var psi = new ProcessStartInfo
                {
                    FileName = SelectedInstance.InstallerPath,
                    Arguments = $"\"{savePath}\"",
                    WorkingDirectory = SelectedInstance.RootDirectory,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show($"ダウンロード中にエラーが発生しました: {ex.Message}"); }
            finally { RefreshLocalPlugins(); }
        }
    }
}