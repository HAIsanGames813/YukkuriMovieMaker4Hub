using System;
using System.Collections.Generic;
// using System.Drawing; // 削除: System.Windows.Media と競合するため完全修飾名を使用
using System.Windows.Interop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YukkuriMovieMaker.Settings;
using static YukkuriMovieMaker4Hub.MainWindow;

namespace YukkuriMovieMaker4Hub
{
    public enum AppTheme { Windows, Light, Dark, Black }
    public class LanguageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public enum PluginLocalStatus
    {
        NotInstalled,
        UpToDate,
        HasUpdate
    }

    public class HubInfo
    {
        public string PortalName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
    }

    // プラグインディレクトリ直下のinfo.jsonに保存する統合プラグイン情報
    public class PluginsInfo
    {
        [JsonPropertyName("plugins")]
        public List<PluginMetadata> Plugins { get; set; } = new List<PluginMetadata>();
    }

    public class PluginMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("portalName")]
        public string PortalName { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("owner")]
        public string Owner { get; set; } = string.Empty;

        [JsonPropertyName("repo")]
        public string Repo { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("downloadedAt")]
        public DateTime? DownloadedAt { get; set; }
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
        [JsonPropertyName("lastSelectedInstanceId")]
        public string? LastSelectedInstanceId { get; set; }
        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "ja-JP";
        [JsonPropertyName("itemSize")]
        public double ItemSize { get; set; } = 300;
        [JsonPropertyName("instancePanelWidth")]
        public double InstancePanelWidth { get; set; } = 320;
    }
    public class YmmUpdateItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Version? Version { get; set; }
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
                // カスタムアイコンファイルが指定されている場合はそちらを使用
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
                    catch { }
                }
                // IconPathが未設定またはファイルが存在しない場合はexeからアイコンを抽出
                if (!string.IsNullOrEmpty(ExePath) && File.Exists(ExePath))
                {
                    try
                    {
                        var icon = System.Drawing.Icon.ExtractAssociatedIcon(ExePath);
                        if (icon != null)
                        {
                            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                System.Windows.Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bs.Freeze();
                            return bs;
                        }
                    }
                    catch { }
                }
                return null;
            }
        }

        [JsonPropertyName("exePath")]
        public string ExePath { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsRealInstance => !string.IsNullOrEmpty(ExePath);

        [JsonIgnore]
        public string RootDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.GetDirectoryName(ExePath) ?? string.Empty;
        [JsonIgnore]
        public string PluginDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.Combine(RootDirectory, "user", "plugin");
        [JsonIgnore]
        public string InstallerPath => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.Combine(RootDirectory, "Resources", "bin", "Installer", "YukkuriMovieMaker.Plugin.Installer.exe");

        private bool _hasUpdate;
        [JsonIgnore]
        public bool HasUpdate { get => _hasUpdate; set { _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }

        private bool _isRunning;
        [JsonIgnore]
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); } }

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
        public string GetLocalVersion()
        {
            try
            {
                string settingsPath = Path.Combine(RootDirectory, "user", "setting");
                if (!Directory.Exists(settingsPath)) return "0.0.0.0";
                var versionDirectories = new DirectoryInfo(settingsPath).GetDirectories();
                var latestDir = versionDirectories
                    .Select(d => new
                    {
                        Directory = d,
                        LastWriteTime = d.EnumerateFiles("*", SearchOption.AllDirectories)
                                         .Select(f => f.LastWriteTime)
                                         .DefaultIfEmpty(d.LastWriteTime)
                                         .Max()
                    })
                    .OrderByDescending(x => x.LastWriteTime)
                    .FirstOrDefault();

                return latestDir?.Directory.Name ?? "0.0.0.0";
            }
            catch
            {
                return "0.0.0.0";
            }
        }
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
                if (host.Contains("nicovideo.jp")) return Translate.Niconico;
                if (host.Contains("ymm4-info.net")) return Translate.Ymm4InfoSite;
                return host;
            }
            catch { return Translate.DistributionSite; }
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
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        private PluginLocalStatus _localStatus = PluginLocalStatus.NotInstalled;
        public PluginLocalStatus LocalStatus { get => _localStatus; set { _localStatus = value; OnPropertyChanged(nameof(LocalStatus)); OnPropertyChanged(nameof(IsInstalled)); } }
        private string _localVersion = "";
        public string LocalVersion { get => _localVersion; set { _localVersion = value; OnPropertyChanged(nameof(LocalVersion)); } }
        private ObservableCollection<GitHubReleaseDetail> _releases = new ObservableCollection<GitHubReleaseDetail>();
        public ObservableCollection<GitHubReleaseDetail> Releases { get => _releases; set { _releases = value; OnPropertyChanged(nameof(Releases)); } }
        private GitHubReleaseDetail? _selectedVersion;
        public GitHubReleaseDetail? SelectedVersion { get => _selectedVersion; set { _selectedVersion = value; OnPropertyChanged(nameof(SelectedVersion)); } }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        private bool _hasUpdate;
        public bool HasUpdate { get => _hasUpdate; set { _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }
        private bool _isNew;
        public bool IsNew { get => _isNew; set { _isNew = value; OnPropertyChanged(nameof(IsNew)); } }
        private bool _isUnlinked;
        public bool IsUnlinked { get => _isUnlinked; set { _isUnlinked = value; OnPropertyChanged(nameof(IsUnlinked)); } }
        private bool _isLocalEnabled = true;
        public bool IsLocalEnabled { get => _isLocalEnabled; set { _isLocalEnabled = value; OnPropertyChanged(nameof(IsLocalEnabled)); } }
        public bool IsDirectDownloadSupported => !string.IsNullOrEmpty(Url) && Url.Contains("github.com");
        public bool IsInstalled => LocalStatus != PluginLocalStatus.NotInstalled;

        // リリースが取得済みで0件（GitHub URLはあるがリリースが存在しない）
        private bool _hasNoRelease;
        public bool HasNoRelease
        {
            get => _hasNoRelease;
            set { _hasNoRelease = value; OnPropertyChanged(nameof(HasNoRelease)); OnPropertyChanged(nameof(IsAssetDownloadable)); OnPropertyChanged(nameof(LatestVersionName)); }
        }

        // リリースデータ取得完了フラグ（取得前は判定しない）
        private bool _releaseLoaded;
        public bool ReleaseLoaded
        {
            get => _releaseLoaded;
            set { _releaseLoaded = value; OnPropertyChanged(nameof(ReleaseLoaded)); OnPropertyChanged(nameof(IsAssetDownloadable)); }
        }

        // 最新リリースのアセットが .ymme / .zip / .dll のいずれかであること
        public bool IsAssetDownloadable
        {
            get
            {
                if (!IsDirectDownloadSupported) return false;
                if (!ReleaseLoaded) return true; // 取得前は仮にtrueとして表示を崩さない
                if (HasNoRelease) return false;
                if (Releases == null || Releases.Count == 0) return false;
                var fn = Releases[0].FileName.ToLower();
                return fn.EndsWith(".ymme") || fn.EndsWith(".zip") || fn.EndsWith(".dll");
            }
        }
        // プラグイン自体の初公開日（最も古いリリースの日付）
        public DateTime FirstPublishedAt => Releases != null && Releases.Count > 0 ? Releases.Min(r => r.PublishedAt) : DateTime.MinValue;
        // 最新リリース日
        public DateTime LatestPublishedAt => Releases != null && Releases.Count > 0 ? Releases.Max(r => r.PublishedAt) : DateTime.MinValue;
        public string LatestVersionName
        {
            get
            {
                if (!IsEnabled) return Translate.EndDistribution;
                if (!IsDirectDownloadSupported) return Translate.NoInfo;
                if (!ReleaseLoaded) return Translate.Acquiring;
                if (HasNoRelease) return Translate.EndDistribution;
                if (Releases == null || Releases.Count == 0) return Translate.Acquiring;
                if (!IsAssetDownloadable) return Translate.NoInfo; // 対応形式なし
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
        public void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string FullPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool IsEnabled
        {
            get
            {
                if (IsDirectory)
                {
                    // ディレクトリの場合：フォルダ名の先頭が _ なら無効
                    string dirName = Path.GetFileName(FullPath);
                    if (dirName.StartsWith("_")) return false;

                    // フォルダが存在し、中に .dll があれば有効
                    if (Directory.Exists(FullPath))
                    {
                        var enabledDlls = Directory.GetFiles(FullPath, "*.dll", SearchOption.AllDirectories);
                        return enabledDlls.Length > 0;
                    }
                    return true;
                }
                else
                {
                    // ファイルの場合：.disabledで終わっていれば無効
                    return !FullPath.EndsWith(".disabled");
                }
            }
        }
        public bool IsSelectionValid => FullPath != "DUMMY_NONE_SELECTED";
        public string Author { get; set; } = "-";
        public string Version { get; set; } = "-";
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        private bool _hasUpdate;
        public bool IsBackup { get; set; } = false;
        public bool HasUpdate { get => _hasUpdate; set { _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }

        private string _type = string.Empty;
        public string Type { get => _type; set { _type = value; OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(DisplayType)); } }

        /// <summary>Type を多言語表示名に変換したプロパティ（タイプフィルターと同じ翻訳を使用）</summary>
        public string DisplayType => PluginTypeHelper.GetDisplayName(Type);

        public DateTime? PublishedAt { get; set; }
        public DateTime? DownloadedAt { get; set; }

        public string DisplayPublishedAt => PublishedAt.HasValue ? PublishedAt.Value.ToString("yyyy/MM/dd") : "-";
        public string DisplayDownloadedAt => DownloadedAt.HasValue ? DownloadedAt.Value.ToString("yyyy/MM/dd HH:mm") : "-";

        // 日本語名順ソート用：先頭の _ や . を除去した読み名
        public string DisplayNameSortKey => DisplayName.TrimStart('_', '.', ' ');

        public void NotifyPathChanged() => OnPropertyChanged(nameof(IsEnabled));
    }
    public class FontItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public FontFamily Family { get; set; } = new FontFamily("Segoe UI");
    }

    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Translate.Enable : Translate.Disable;
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

    /// <summary>プラグインタイプの内部名→多言語表示名変換（LocalPluginInfo と PluginTypeFilterItem の両方から使用）</summary>
    public static class PluginTypeHelper
    {
        public static string GetDisplayName(string internalName) => internalName switch
        {
            "映像エフェクト" => Translate.VideoEffects,
            "音声エフェクト" => Translate.AudioEffects,
            "音声合成" => Translate.SpeechSynthesis,
            "動画出力" => Translate.VideoOutput,
            "動画読み込み" => Translate.LoadVideo,
            "音声読み込み" => Translate.LoadAudio,
            "画像読み込み" => Translate.LoadImage,
            "場面切り替え" => Translate.SceneTransition,
            "図形" => Translate.Shapes,
            "立ち絵" => Translate.Character,
            "ツール" => Translate.Tools,
            "テキスト補完" => Translate.TextCompletion,
            "模様" => Translate.Pattern,
            "文字起こし" => Translate.Transcription,
            "その他" => Translate.Others,
            "配布終了" => Translate.EndDistribution,
            _ => internalName   // 未知のタイプはそのまま表示
        };
    }

    public class PluginTypeFilterItem : INotifyPropertyChanged
    {
        public string InternalName { get; set; } = string.Empty;

        public string DisplayName => PluginTypeHelper.GetDisplayName(InternalName);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class InstalledVersionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s) && s != "-")
                return string.Format(Translate.InstalledVersion, s);
            return string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _latestOnlineVersion = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private SettingsManager _settingsManager = new SettingsManager();
        private AppSettings _currentSettings = new AppSettings();
        private HttpClient _http = new HttpClient();
        // APIへの同時リクエスト数を制限するセマフォ（サーバー負荷・レート制限対策）
        private readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(4, 4);
        public ObservableCollection<InstanceInfo> Instances { get; set; } = new ObservableCollection<InstanceInfo>();
        public ObservableCollection<LocalPluginInfo> LocalPlugins { get; set; } = new ObservableCollection<LocalPluginInfo>();
        public ObservableCollection<PluginCatalogItem> OnlinePlugins { get; set; } = new ObservableCollection<PluginCatalogItem>();
        public ObservableCollection<string> ProjectDirectories { get; set; } = new ObservableCollection<string>();
        private List<ProjectFileItem> _allProjects = new List<ProjectFileItem>();
        public ObservableCollection<ProjectFileItem> FilteredProjects { get; set; } = new ObservableCollection<ProjectFileItem>();
        private string _projectSearchText = string.Empty;

        private bool _hasAnyUpdate;
        public bool HasAnyUpdate
        {
            get => _hasAnyUpdate;
            set { _hasAnyUpdate = value; OnPropertyChanged(nameof(HasAnyUpdate)); }
        }

        private bool _hasAnyNew;
        public bool HasAnyNew
        {
            get => _hasAnyNew;
            set { _hasAnyNew = value; OnPropertyChanged(nameof(HasAnyNew)); }
        }

        private bool _isViewTile = true;
        public bool IsViewTile
        {
            get => _isViewTile;
            set { _isViewTile = value; OnPropertyChanged(nameof(IsViewTile)); }
        }

        // ソート順保存用
        private string _lastPluginSortField = "DisplayName";
        private ListSortDirection _lastPluginSortDirection = ListSortDirection.Ascending;

        public bool ShowBackup
        {
            get => _showBackup;
            set
            {
                _showBackup = value;
                RefreshRecentProjects(); // ← ApplyProjectFilterではなくRefreshRecentProjectsを呼ぶ
                OnPropertyChanged(nameof(ShowBackup));
            }
        }
        // バックアップフィルタ用
        private bool _showBackup = false;
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

        private string _selectedPluginType = Translate.All;
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
                var item = new PluginTypeFilterItem { InternalName = t, IsSelected = true };
                item.PropertyChanged += (s, e) => ApplyOnlinePluginFilter();
                PluginTypeFilters.Add(item);
            }
        }
        public ObservableCollection<LanguageInfo> Languages { get; } = new ObservableCollection<LanguageInfo>
        {
            new LanguageInfo { Name = "日本語", Code = "ja-JP" },
            new LanguageInfo { Name = "English", Code = "en-US" },
            new LanguageInfo { Name = "中文 (简体)", Code = "zh-CN" },
            new LanguageInfo { Name = "中文 (繁體)", Code = "zh-TW" },
            new LanguageInfo { Name = "한국어", Code = "ko-KR" },
            new LanguageInfo { Name = "Español", Code = "es-ES" },
            new LanguageInfo { Name = "العربية", Code = "ar-SA" },
            new LanguageInfo { Name = "Bahasa Indonesia", Code = "id-ID" }
        };

        private LanguageInfo _selectedLanguage;
        public LanguageInfo SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    OnPropertyChanged(nameof(SelectedLanguage));

                    if (value != null && _currentSettings != null)
                    {
                        _currentSettings.LanguageCode = value.Code;
                        _settingsManager.Save(_currentSettings);
                    }
                }
            }
        }

        private List<LocalPluginInfo> _allLocalPlugins = new List<LocalPluginInfo>();
        private List<PluginCatalogItem> _allOnlinePlugins = new List<PluginCatalogItem>();
        private string _fontSearchText = string.Empty;
        public string FontSearchText { get => _fontSearchText; set { _fontSearchText = value; ApplyFontFilter(); OnPropertyChanged(nameof(FontSearchText)); } }
        public Array ThemeModes => Enum.GetValues(typeof(AppTheme));
        private readonly InstanceInfo _dummyInstance = new InstanceInfo { Name = Translate.SelectInstance, ExePath = string.Empty };

        public string HubTitle => $"YukkuriMovieMaker4Hub  v{HubVersion}";

        private InstanceInfo? _selectedInstance;
        public InstanceInfo SelectedInstance
        {
            get => _selectedInstance ?? _dummyInstance;
            set
            {
                _selectedInstance = value;
                OnPropertyChanged(nameof(SelectedInstance));
                _currentSettings.LastSelectedInstanceId = value?.Id;
                _settingsManager.Save(_currentSettings);
                RefreshLocalPlugins(); // ソート順はRefreshLocalPlugins→ApplyLocalPluginFilter内で復元される
            }
        }
        private PluginCatalogItem? _selectedOnlinePlugin;
        public PluginCatalogItem? SelectedOnlinePlugin { get => _selectedOnlinePlugin; set { _selectedOnlinePlugin = value; OnPropertyChanged(nameof(SelectedOnlinePlugin)); if (value != null) _ = LoadReleaseDetails(value); } }
        public FontItem? SelectedFontItem
        {
            get => FilteredFonts.FirstOrDefault(f => f.InternalName == _currentSettings.FontFamily);
            set { if (value != null) { _currentSettings.FontFamily = value.InternalName; ApplyTheme(); SaveAll(); OnPropertyChanged(nameof(SelectedFontItem)); } }
        }
        // 既存の ItemSize を以下のように書き換えてください
        public double ItemSize
        {
            get => _currentSettings.ItemSize;
            set
            {
                if (_currentSettings.ItemSize != value)
                {
                    _currentSettings.ItemSize = value;
                    OnPropertyChanged(nameof(ItemSize));
                    SaveAll(); // ここでファイルに保存されます
                }
            }
        }
        public bool CloseOnLaunch
        {
            get => _currentSettings.CloseOnLaunch;
            set { _currentSettings.CloseOnLaunch = value; SaveAll(); OnPropertyChanged(nameof(CloseOnLaunch)); }
        }
        private string _lastSortField = "DisplayName";
        private ListSortDirection _lastSortDir = ListSortDirection.Ascending;
        private List<YmmUpdateItem> _ymmUpdates = new List<YmmUpdateItem>();

        private async Task CheckYmmUpdates()
        {
            try
            {
                var xml = await _http.GetStringAsync("https://manjubox.net/rss.xml");
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                var nodes = doc.SelectNodes("//item");
                _ymmUpdates.Clear();

                if (nodes != null)
                {
                    foreach (System.Xml.XmlNode node in nodes)
                    {
                        string title = node.SelectSingleNode("title")?.InnerText ?? "";
                        string desc = node.SelectSingleNode("description")?.InnerText ?? "";
                        var match = Regex.Match(title, @"v(\d+\.\d+\.\d+\.\d+)");

                        if (match.Success && Version.TryParse(match.Groups[1].Value, out var v))
                        {
                            _ymmUpdates.Add(new YmmUpdateItem { Title = title, Description = desc, Version = v });
                        }
                    }
                }

                if (_ymmUpdates.Count == 0) return;
                var latest = _ymmUpdates[0].Version;

                foreach (var instance in Instances)
                {
                    if (Version.TryParse(instance.GetLocalVersion(), out var localV))
                    {
                        instance.HasUpdate = latest > localV;
                    }
                }
            }
            catch { }
        }

        private void ShowUpdateInfo_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null) return;

            Version.TryParse(SelectedInstance.GetLocalVersion(), out var localV);
            var filteredUpdates = _ymmUpdates
                .Where(u => u.Version > localV)
                .Take(5)
                .ToList();

            if (filteredUpdates.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"{Translate.LatestVersion}: v{SelectedInstance.GetLocalVersion()}");
            sb.AppendLine("------------------------------------");

            foreach (var update in filteredUpdates)
            {
                sb.AppendLine($"■ {update.Title}");

                string cleanDesc = Regex.Replace(update.Description, "<.*?>", string.Empty);
                cleanDesc = cleanDesc.Replace(" ", "\n");

                sb.AppendLine(cleanDesc);
                sb.AppendLine();
            }

            MessageBox.Show(sb.ToString(), Translate.UpdateDetails, MessageBoxButton.OK, MessageBoxImage.Information);
        }


        public MainWindow()
        {
            _currentSettings = _settingsManager.Load();

            string langCode = _currentSettings.LanguageCode ?? "ja-JP";
            var culture = new CultureInfo(langCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

            InitializeComponent();
            this.DataContext = this;

            // インスタンスパネルの初期幅を復元
            if (InstancePanelColumn != null)
                InstancePanelColumn.Width = new GridLength(_currentSettings.InstancePanelWidth);

            _selectedLanguage = Languages.FirstOrDefault(l => l.Code == langCode) ?? Languages[0];
            OnPropertyChanged(nameof(SelectedLanguage));

            this.FlowDirection = (langCode == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
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

            ApplyTheme();
            RestoreLastSelection();
            RefreshRecentProjects();
            _ = CheckYmmUpdates();
            _ = CheckForHubUpdateAsync();
            _ = LoadOnlinePlugins();

            // 起動中インスタンスのポーリングタイマー（2秒間隔）
            var runningTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            runningTimer.Tick += (s, e) => RefreshRunningStatus();
            runningTimer.Start();
        }
        private static readonly string HubVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        private async Task CheckForHubUpdateAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/HAIsanGames813/YukkuriMovieMaker4Hub/releases/latest");
                request.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var latestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;

                    if (!string.IsNullOrEmpty(latestTag) && latestTag != HubVersion)
                    {
                        var assets = doc.RootElement.GetProperty("assets");
                        if (assets.GetArrayLength() > 0)
                        {
                            var asset = assets[0];
                            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            var fileName = asset.GetProperty("name").GetString();

                            if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(fileName))
                            {
                                var result = MessageBox.Show(
                                    $"{Translate.UpdateAvailable}\nLocal: {HubVersion}\nLatest: {latestTag}\n\n{Translate.ConfirmUpdate}",
                                    "Update Check",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Information);

                                if (result == MessageBoxResult.Yes)
                                {
                                    await DownloadAndExecuteUpdateAsync(downloadUrl, fileName);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
        private async Task DownloadAndExecuteUpdateAsync(string url, string fileName)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "YMM4HubUpdate");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string savePath = Path.Combine(tempDir, fileName);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(savePath))
                {
                    await contentStream.CopyToAsync(fileStream);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (File.Exists(savePath))
                    {
                        File.Delete(savePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Translate.DownloadError} {ex.Message}");
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
            var currentLang = XmlLanguage.GetLanguage(_currentSettings.LanguageCode ?? "ja-JP");
            var enLang = XmlLanguage.GetLanguage("en-US");

            _allFonts.Clear();
            foreach (var ff in Fonts.SystemFontFamilies)
            {
                if (!ff.FamilyNames.TryGetValue(currentLang, out string name))
                {
                    if (!ff.FamilyNames.TryGetValue(enLang, out name))
                    {
                        name = ff.Source;
                    }
                }

                _allFonts.Add(new FontItem { DisplayName = name, InternalName = ff.Source, Family = ff });
            }
            _allFonts = _allFonts.OrderBy(f => f.DisplayName).ToList();
            ApplyFontFilter();
        }

        private void ApplyFontFilter()
        {
            var filtered = _allFonts.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(FontSearchText)) filtered = filtered.Where(f => f.DisplayName.IndexOf(FontSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            FilteredFonts.Clear();
            foreach (var f in filtered) FilteredFonts.Add(f);
            OnPropertyChanged(nameof(SelectedFontItem));
        }
        public AppTheme SelectedTheme
        {
            get => _currentSettings.Theme;
            set
            {
                if (_currentSettings.Theme != value)
                {
                    _currentSettings.Theme = value;
                    OnPropertyChanged(nameof(SelectedTheme));
                    ApplyTheme();
                    SaveAll();
                }
            }
        }

        private void ApplyTheme()
        {
            try
            {
                var mode = SelectedTheme;
                if (mode == AppTheme.Windows)
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var value = key?.GetValue("AppsUseLightTheme");
                    mode = (value is int i && i == 1) ? AppTheme.Light : AppTheme.Dark;
                }

                switch (mode)
                {
                    case AppTheme.Light:
                        SetThemeColors("#FFFFFF", "#F0F0F0", "#E0E0E0", "#000000", "#555555", "#2255BB", "#DDDDDD");
                        break;
                    case AppTheme.Dark:
                        SetThemeColors("#252525", "#333333", "#444444", "#FFFFFF", "#AAAAAA", "#4CAF50", "#555555");
                        break;
                    case AppTheme.Black:
                        SetThemeColors("#000000", "#121212", "#1F1F1F", "#FFFFFF", "#888888", "#4CAF50", "#333333");
                        break;
                }
            }
            catch { }
        }

        private void SetThemeColors(string bg, string panel, string item, string text, string subText, string accent, string border)
        {
            var font = new FontFamily(_currentSettings.FontFamily);

            var brushes = new Dictionary<string, SolidColorBrush>
            {
                ["ThemeBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                ["PanelBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(panel)),
                ["ItemBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item)),
                ["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text)),
                ["SubTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(subText)),
                ["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                ["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
            };

            // ① Application.Current.Resources に書き込む（起動直後やウィンドウ未表示時のフォールバック）
            Application.Current.Resources["AppFont"] = font;
            foreach (var kv in brushes)
                Application.Current.Resources[kv.Key] = kv.Value;

            // ② 既に開いている全ウィンドウの Window.Resources を直接更新する
            //    （Window.Resources は Application.Resources より優先されるため、ここも更新が必要）
            foreach (Window win in Application.Current.Windows)
            {
                if (win.Resources.Contains("AppFont"))
                    win.Resources["AppFont"] = font;
                foreach (var kv in brushes)
                    if (win.Resources.Contains(kv.Key))
                        win.Resources[kv.Key] = kv.Value;
            }
        }

        // 各インスタンスの起動状態を更新する
        private void RefreshRunningStatus()
        {
            foreach (var instance in Instances)
            {
                if (string.IsNullOrEmpty(instance.ExePath)) { instance.IsRunning = false; continue; }
                string procName = Path.GetFileNameWithoutExtension(instance.ExePath);
                instance.IsRunning = Process.GetProcessesByName(procName)
                    .Any(p =>
                    {
                        try { return string.Equals(p.MainModule?.FileName, instance.ExePath, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });
            }
        }

        // インスタンスパネル幅グリッパー：ドラッグ完了時に保存
        private void InstancePanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (InstancePanelColumn != null)
            {
                _currentSettings.InstancePanelWidth = InstancePanelColumn.ActualWidth;
                SaveAll();
            }
        }

        private void SaveAll()
        {
            try
            {
                _currentSettings.Instances = Instances.ToList();
                _currentSettings.ProjectDirectories = ProjectDirectories.ToList();
                _settingsManager.Save(_currentSettings);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    Translate.AccessDenied + " " + Translate.AdminPrivilegeRequired,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save Error: {ex.Message}");
            }
        }

        // --- インスタンスリスト ドラッグ&ドロップ並び替え ---
        private Point _instanceDragStart;
        private InstanceInfo? _instanceDragItem;
        private bool _instanceDragging = false;

        private void InstanceList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _instanceDragStart = e.GetPosition(null);
            _instanceDragItem = null;
            _instanceDragging = false;

            // クリックされた ListBoxItem の DataContext を取得
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is ListBoxItem))
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            if (element is ListBoxItem item && item.DataContext is InstanceInfo info)
                _instanceDragItem = info;
        }

        private void InstanceList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _instanceDragItem == null || _instanceDragging)
                return;

            var pos = e.GetPosition(null);
            var diff = pos - _instanceDragStart;
            // 長押し判定の代わりに最小ドラッグ距離で開始（SystemParameters 準拠）
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _instanceDragging = true;
            DragDrop.DoDragDrop(InstanceListBox, _instanceDragItem, DragDropEffects.Move);
            _instanceDragging = false;
            _instanceDragItem = null;
        }

        private void InstanceList_Drop(object sender, DragEventArgs e)
        {
            if (_instanceDragItem == null) return;

            // ドロップ先の ListBoxItem を特定
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is ListBoxItem))
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);

            InstanceInfo? target = null;
            if (element is ListBoxItem dropItem && dropItem.DataContext is InstanceInfo ti)
                target = ti;

            if (target == null || target == _instanceDragItem) return;

            int fromIndex = Instances.IndexOf(_instanceDragItem);
            int toIndex = Instances.IndexOf(target);
            if (fromIndex < 0 || toIndex < 0) return;

            Instances.Move(fromIndex, toIndex);
            InstanceListBox.SelectedItem = _instanceDragItem;
            SaveAll();
        }

        private async void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            // ① 追加方法を選択（新規DL / 既存追加）
            var choiceDialog = new AddInstanceChoiceDialog { Owner = this };
            if (choiceDialog.ShowDialog() != true || choiceDialog.Mode == AddInstanceMode.Cancelled)
                return;

            if (choiceDialog.Mode == AddInstanceMode.NewDownload)
            {
                // ② 新規ダウンロード
                var setupDialog = new InstanceSetupDialog(isNewDownload: true) { Owner = this };
                if (setupDialog.ShowDialog() != true || string.IsNullOrEmpty(setupDialog.ResultExePath))
                    return;

                try
                {
                    var newInstance = new InstanceInfo
                    {
                        Name = setupDialog.InstanceName ?? "YukkuriMovieMaker4",
                        ExePath = setupDialog.ResultExePath,
                        IconPath = setupDialog.IconPath  // nullの場合IconImageがexeから自動取得
                    };
                    newInstance.PropertyChanged += (s, ev) => SaveAll();
                    Instances.Add(newInstance);
                    SelectedInstance = newInstance;
                    SaveAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"インスタンスの追加に失敗しました: {ex.Message}");
                }
            }
            else
            {
                // ③ 既存を追加：exe選択 → 名前・アイコン設定
                var fileDialog = new OpenFileDialog
                {
                    Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe",
                    Title = Translate.SelectExeTitle
                };
                if (fileDialog.ShowDialog() != true) return;

                string exePath = fileDialog.FileName;
                string defaultName = Path.GetFileName(Path.GetDirectoryName(exePath)) ?? "YukkuriMovieMaker4";

                // exeパスを渡すことでアイコンを自動プレビュー
                var setupDialog = new InstanceSetupDialog(exePath, defaultName) { Owner = this };
                if (setupDialog.ShowDialog() != true) return;

                try
                {
                    var newInstance = new InstanceInfo
                    {
                        Name = setupDialog.InstanceName ?? defaultName,
                        ExePath = exePath,
                        IconPath = setupDialog.IconPath  // nullの場合IconImageがexeから自動取得
                    };
                    newInstance.PropertyChanged += (s, ev) => SaveAll();
                    Instances.Add(newInstance);
                    SelectedInstance = newInstance;
                    SaveAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"インスタンスの追加に失敗しました: {ex.Message}");
                }
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance != null && !string.IsNullOrEmpty(SelectedInstance.ExePath))
            {
                _currentSettings.Instances.Remove(SelectedInstance);
                Instances.Remove(SelectedInstance);
                _settingsManager.Save(_currentSettings);
                SelectedInstance = Instances.Count > 0 ? Instances[0] : _dummyInstance;
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedItem is TabItem ti && ti.Header != null)
            {
                string header = ti.Header.ToString() ?? "";
                if (header == Translate.PluginPortal) await LoadOnlinePlugins();
                if (header == Translate.Overview)
                {
                    RefreshRecentProjects();
                    _ = CheckYmmUpdates();
                }
            }
        }
        private async Task LoadOnlinePlugins()
        {
            if (OnlinePlugins.Count > 0) return;
            try
            {
                // 1. YAMLから一覧を取得
                var yml = await _http.GetStringAsync("https://manjubox.net/ymm4plugins.yml");
                var catalog = ParseYmm4PluginsYaml(yml);
                _allOnlinePlugins = catalog;

                // UIにまず一覧を表示（ユーザーを待たせない）
                OnlinePlugins.Clear();
                foreach (var p in _allOnlinePlugins) OnlinePlugins.Add(p);
                ApplyOnlinePluginFilter();

                // 2. 詳細情報（Releases）をセマフォで並列数を制限しながらバックグラウンド取得
                // 同時リクエスト数を _apiSemaphore で絞ることでレート制限・サーバー負荷を回避
                var detailTasks = _allOnlinePlugins.Select(async plugin =>
                {
                    await _apiSemaphore.WaitAsync();
                    try
                    {
                        await LoadReleaseDetails(plugin);
                        plugin.OnPropertyChanged(nameof(plugin.LatestVersionName));
                    }
                    finally
                    {
                        _apiSemaphore.Release();
                    }
                });

                // 全ての詳細取得を開始（完了を待たずにUI操作可能）
                _ = Task.WhenAll(detailTasks);

            }
            catch (Exception ex)
            {
                MessageBox.Show(Translate.PortalLoadFailed + ex.Message);
            }
        }

        // 既存の LoadReleaseDetails をそのまま使用、あるいは以下の微修正版を使用
        private async Task LoadReleaseDetails(PluginCatalogItem plugin)
        {
            var githubUrl = FindGitHubRepositoryUrl(plugin);
            if (githubUrl == null)
            {
                // GitHub URL なし → サイトで確認扱い（取得完了とみなす）
                Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                return;
            }

            var match = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)");
            if (!match.Success)
            {
                Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                return;
            }

            string owner = match.Groups[1].Value;
            string repo = match.Groups[2].Value.Replace(".git", "").TrimEnd('/');

            // 429 レート制限が来た場合に備えてリトライ（最大3回、指数バックオフ）
            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get,
                        $"https://manjubox.net/api/ymm4plugins/github/detail/{owner}/{repo}");
                    var response = await _http.SendAsync(request);

                    // 403 Forbidden → GitHub側のブロック、リトライ不要
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                        return;
                    }

                    // 429 Too Many Requests → Retry-After を待ってリトライ
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        int waitSec = 10 * (attempt + 1); // 10s, 20s, 30s
                        if (response.Headers.TryGetValues("Retry-After", out var retryVals)
                            && int.TryParse(retryVals.FirstOrDefault(), out int ra))
                            waitSec = ra + 1;
                        await Task.Delay(waitSec * 1000);
                        continue; // リトライ
                    }

                    // その他の非成功ステータス
                    if (!response.IsSuccessStatusCode)
                    {
                        Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                        return;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();

                    // レスポンスが JSON 配列でない場合（HTMLエラーページ等）は無視
                    var trimmed = responseBody.TrimStart();
                    if (!trimmed.StartsWith("[") && !trimmed.StartsWith("{"))
                    {
                        Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                        return;
                    }

                    using var doc = JsonDocument.Parse(responseBody);
                    var releaseList = new List<GitHubReleaseDetail>();

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

                    // UIスレッドでデータをセット
                    Dispatcher.Invoke(() => {
                        plugin.Releases = new ObservableCollection<GitHubReleaseDetail>(releaseList);
                        if (plugin.Releases.Count > 0)
                        {
                            plugin.SelectedVersion = plugin.Releases[0];
                            plugin.HasNoRelease = false;
                        }
                        else
                        {
                            plugin.HasNoRelease = true;
                        }
                        plugin.ReleaseLoaded = true;
                        plugin.OnPropertyChanged(nameof(plugin.LatestVersionName));
                        plugin.OnPropertyChanged(nameof(plugin.IsAssetDownloadable));
                        plugin.OnPropertyChanged(nameof(plugin.FirstPublishedAt));
                        plugin.OnPropertyChanged(nameof(plugin.LatestPublishedAt));
                    });
                    return; // 成功したのでループ終了
                }
                catch (System.Text.Json.JsonException)
                {
                    // JSONパース失敗（HTMLエラーページ等）→ リトライしても無駄なので終了
                    Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                    return;
                }
                catch (HttpRequestException) when (attempt < maxRetries - 1)
                {
                    // 接続エラー → 少し待ってリトライ
                    await Task.Delay(3000 * (attempt + 1));
                }
                catch
                {
                    // その他のエラー → 取得失敗扱いで終了
                    Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
                    return;
                }
            }

            // リトライ上限到達
            Dispatcher.Invoke(() => { plugin.ReleaseLoaded = true; });
        }
        private async void IndividualDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GitHubReleaseDetail release)
            {
                if (SelectedInstance == null)
                {
                    MessageBox.Show(Translate.SelectInstance);
                    return;
                }

                if (SelectedOnlinePlugin != null)
                {
                    SelectedOnlinePlugin.SelectedVersion = release;
                    await ExecuteDownload(SelectedOnlinePlugin, SelectedInstance);
                }
            }
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

        private void SelectAllPlugins_Click(object sender, RoutedEventArgs e)
        {
            if (OnlinePlugins == null) return;
            foreach (var p in OnlinePlugins) p.IsSelected = true;
        }

        private void UnselectAllPlugins_Click(object sender, RoutedEventArgs e)
        {
            if (OnlinePlugins == null) return;
            foreach (var p in OnlinePlugins) p.IsSelected = false;
        }
        private async void VersionDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GitHubReleaseDetail release)
            {
                if (SelectedInstance == null || SelectedOnlinePlugin == null) return;
                SelectedOnlinePlugin.SelectedVersion = release;
                await ExecuteDownload(SelectedOnlinePlugin, SelectedInstance);
            }
        }
        private void ListView_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    ItemSizeSlider.Value += ItemSizeSlider.TickFrequency * 2;
                else
                    ItemSizeSlider.Value -= ItemSizeSlider.TickFrequency * 2;

                e.Handled = true;
            }
        }

        private async void DirectDownload_Click(object sender, RoutedEventArgs e)
        {
            var plugin = (sender as Button)?.DataContext as PluginCatalogItem ?? SelectedOnlinePlugin;
            if (plugin == null || SelectedInstance == null) return;

            if (!plugin.IsEnabled || plugin.Releases == null || plugin.Releases.Count == 0)
            {
                MessageBox.Show(Translate.SkipNoRelease, Translate.SkipTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ExecuteDownload(plugin, SelectedInstance, null, false);

            RefreshLocalPlugins();
        }
        private async void BulkDownload_Click(object sender, RoutedEventArgs e)
        {
            var targets = OnlinePlugins.Where(p => p.IsSelected).ToList();
            if (targets.Count == 0) return;

            var dialog = new BulkDownloadWindow(targets, _currentSettings.Instances);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                var selectedPlugins = dialog.SelectedPlugins;
                var selectedInstances = dialog.SelectedInstances;

                if (selectedPlugins.Count == 0 || selectedInstances.Count == 0) return;

                var progressWin = new DownloadProgressWindow();
                progressWin.Owner = this;
                progressWin.Show();

                int totalTasks = selectedPlugins.Count * selectedInstances.Count;
                int currentTask = 0;

                foreach (var plugin in selectedPlugins)
                {
                    if (plugin.Releases == null || plugin.Releases.Count == 0)
                    {
                        await LoadReleaseDetails(plugin);
                    }

                    var release = SelectedOnlinePlugin?.SelectedVersion;
                    if (release == null) return;

                    foreach (var instance in selectedInstances)
                    {
                        currentTask++;
                        string statusMsg = $"[{currentTask}/{totalTasks}] {plugin.Name}";
                        progressWin.UpdateStatus(statusMsg, ((double)(currentTask - 1) / totalTasks) * 100, $"{currentTask} / {totalTasks}");

                        try
                        {
                            await ExecuteDownload(plugin, instance, progressWin, true);
                        }
                        catch (Exception ex)
                        {
                            progressWin.AddReadme(plugin.Name, $"Error: {instance.Name}\n{ex.Message}");
                        }

                        progressWin.UpdateStatus(statusMsg, ((double)currentTask / totalTasks) * 100, $"{currentTask} / {totalTasks}");
                    }
                }

                foreach (var p in OnlinePlugins) p.IsSelected = false;
                RefreshLocalPlugins();
                progressWin.ShowFinalClose();
            }
        }

        private async Task ExecuteDownload(PluginCatalogItem plugin, InstanceInfo instance, DownloadProgressWindow? progressWin = null, bool isBulk = false)
        {
            var release = plugin.SelectedVersion;
            if (release == null || instance == null)
            {
                if (progressWin != null && !isBulk)
                {
                    progressWin.UpdateStatus(Translate.ErrorNoDownloadVersion, 100, "");
                    progressWin.ShowFinalClose();
                }
                return;
            }

            // ファイル拡張子チェック：.ymme, .zip, .dllのみダウンロード
            string fileName = release.FileName.ToLower();
            if (!fileName.EndsWith(".ymme") && !fileName.EndsWith(".zip") && !fileName.EndsWith(".dll"))
            {
                if (progressWin != null && !isBulk)
                {
                    progressWin.UpdateStatus(string.Format(Translate.SkipUnsupportedFormat, release.FileName), 100, "");
                    progressWin.ShowFinalClose();
                }
                return;
            }

            if (progressWin == null)
            {
                progressWin = new DownloadProgressWindow();
                progressWin.Owner = this;
                progressWin.Show();
            }

            int maxRetries = 3;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    string uniqueId = Guid.NewGuid().ToString("N");
                    string tempBase = Path.Combine(Path.GetTempPath(), "YMM4Hub_" + uniqueId);
                    string downloadFile = Path.Combine(tempBase, release.FileName);
                    string extractPath = Path.Combine(tempBase, "extract");

                    Directory.CreateDirectory(tempBase);
                    Directory.CreateDirectory(extractPath);

                    using (var request = new HttpRequestMessage(HttpMethod.Get, release.BrowserDownloadUrl))
                    {
                        request.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
                        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var fs = File.Create(downloadFile)) await response.Content.CopyToAsync(fs);
                        }
                    }

                    string pluginBaseDir = Path.Combine(instance.RootDirectory, "user", "plugin");
                    DateTime? releasePublishedAt = release.PublishedAt == default ? (DateTime?)null : release.PublishedAt;

                    await Task.Run(async () =>
                    {
                        if (!Directory.Exists(pluginBaseDir)) Directory.CreateDirectory(pluginBaseDir);

                        // .dllファイル → プラグイン名のフォルダを作成してその中に配置
                        if (release.FileName.ToLower().EndsWith(".dll"))
                        {
                            string pluginDirName = Path.GetFileNameWithoutExtension(release.FileName);
                            // portal名があればそちらをフォルダ名に使う
                            if (!string.IsNullOrEmpty(plugin.Name))
                                pluginDirName = plugin.Name;

                            string targetDir = Path.Combine(pluginBaseDir, pluginDirName);
                            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                            string targetPath = Path.Combine(targetDir, release.FileName);
                            File.Copy(downloadFile, targetPath, true);

                            UpdateCentralPluginsInfo(pluginBaseDir, pluginDirName, plugin, release.TagName, releasePublishedAt);
                            Dispatcher.Invoke(() => progressWin.AddReadme(plugin.Name, $"【{instance.Name}】\n\n{string.Format(Translate.DllInstalledMsg, pluginDirName)}"));
                            return;
                        }

                        // .zip / .ymme → 解凍してプラグインフォルダに配置
                        ZipFile.ExtractToDirectory(downloadFile, extractPath);

                        var dllFile = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories).FirstOrDefault();

                        if (dllFile != null)
                        {
                            string pluginSourceDir = Path.GetDirectoryName(dllFile)!;
                            string pluginDirName = Path.GetFileName(pluginSourceDir);

                            if (string.Equals(pluginDirName, "extract", StringComparison.OrdinalIgnoreCase))
                            {
                                pluginDirName = Path.GetFileNameWithoutExtension(release.FileName).Replace(".ymme", "");
                            }

                            string targetDir = Path.Combine(pluginBaseDir, pluginDirName);

                            if (Directory.Exists(targetDir))
                            {
                                for (int d = 0; d < 5; d++)
                                {
                                    try { Directory.Delete(targetDir, true); break; }
                                    catch { await Task.Delay(500); }
                                }
                            }

                            Directory.CreateDirectory(targetDir);

                            foreach (var dirPath in Directory.GetDirectories(pluginSourceDir, "*", SearchOption.AllDirectories))
                                Directory.CreateDirectory(dirPath.Replace(pluginSourceDir, targetDir));
                            foreach (var filePath in Directory.GetFiles(pluginSourceDir, "*", SearchOption.AllDirectories))
                                File.Copy(filePath, filePath.Replace(pluginSourceDir, targetDir), true);

                            UpdateCentralPluginsInfo(pluginBaseDir, pluginDirName, plugin, release.TagName, releasePublishedAt);

                            var readmeFile = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)
                                .FirstOrDefault(f => {
                                    string n = Path.GetFileNameWithoutExtension(f).ToLower();
                                    return n.Contains("readme") || n.Contains("説明書") || n.Contains("はじめに");
                                });
                            string readmeContent = readmeFile != null ? File.ReadAllText(readmeFile) : Translate.NoReadme;
                            Dispatcher.Invoke(() => progressWin.AddReadme(plugin.Name, $"【{instance.Name}】\n\n{readmeContent}"));
                        }
                        else
                        {
                            throw new Exception(Translate.NoDllFound);
                        }
                    });

                    if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
                    break;
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(1000 * (i + 1));
                }
            }

            if (!isBulk)
            {
                RefreshLocalPlugins();
                progressWin.ShowFinalClose();
            }
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var plugin in OnlinePlugins)
            {
                plugin.IsSelected = false;
            }
        }

        private void PluginListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ハイライト選択の変化は何もしない
            // IsSelected（予約チェック）は「予約に追加」ボタン押下時のみ更新する
        }

        // リストの複数選択（ハイライト）されているものを一括で予約(チェック)に入れる
        private void BulkAddReservation_Click(object sender, RoutedEventArgs e)
        {
            if (PluginListView == null) return;
            var selectedItems = PluginListView.SelectedItems.Cast<PluginCatalogItem>().ToList();
            foreach (var plugin in selectedItems)
            {
                plugin.IsSelected = true;
            }
        }

        private void SyncPortalStatus()
        {
            if (_allOnlinePlugins == null || _allLocalPlugins == null) return;

            // フィルター済みの OnlinePlugins ではなく _allOnlinePlugins 全体を走査する。
            // こうすることで「フィルターで隠れているプラグイン」の LocalStatus も正しく更新され、
            // インスタンス変更後に ApplyOnlinePluginFilter が呼ばれても正確なフィルター結果になる。
            foreach (var online in _allOnlinePlugins)
            {
                online.IsUnlinked = false;

                var local = _allLocalPlugins.FirstOrDefault(l =>
                    (!string.IsNullOrEmpty(l.Owner) && !string.IsNullOrEmpty(online.Owner) &&
                     string.Equals(l.Owner, online.Owner, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(l.Repo, online.Repo, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(l.DisplayName, online.Name, StringComparison.OrdinalIgnoreCase));

                if (local != null)
                {
                    online.IsNew = false;
                    online.LocalVersion = local.Version ?? "-";
                    online.IsLocalEnabled = local.IsEnabled;

                    if (!string.IsNullOrEmpty(local.Version) && local.Version != "-" && online.Releases != null && online.Releases.Count > 0)
                    {
                        var latest = online.Releases[0].TagName.TrimStart('v');
                        var current = local.Version.TrimStart('v');

                        if (latest != current)
                        {
                            online.HasUpdate = true;
                            online.LocalStatus = PluginLocalStatus.HasUpdate;
                        }
                        else
                        {
                            online.HasUpdate = false;
                            online.LocalStatus = PluginLocalStatus.UpToDate;
                        }
                    }
                    else
                    {
                        online.HasUpdate = false;
                        online.LocalStatus = PluginLocalStatus.UpToDate;
                    }
                }
                else
                {
                    online.IsNew = true;
                    online.HasUpdate = false;
                    online.LocalVersion = "";
                    online.LocalStatus = PluginLocalStatus.NotInstalled;
                    online.IsLocalEnabled = true;
                }
            }

            HasAnyUpdate = _allOnlinePlugins.Any(p => p.HasUpdate);
            HasAnyNew = _allOnlinePlugins.Any(p => p.IsNew);
            // 全プラグインの LocalStatus 更新後にフィルターを再適用
            ApplyOnlinePluginFilter();
        }

        private async void UpdatePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PluginCatalogItem onlinePlugin)
            {
                // バージョン情報がなければ取得
                if (onlinePlugin.Releases.Count == 0) await LoadReleaseDetails(onlinePlugin);

                // 最新版をセットして実行
                onlinePlugin.SelectedVersion = onlinePlugin.Releases.FirstOrDefault();
                if (onlinePlugin.SelectedVersion != null && SelectedInstance != null)
                {
                    await ExecuteDownload(onlinePlugin, SelectedInstance);
                }
            }
        }

        private async void UpdateLocalPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LocalPluginInfo localPlugin)
            {
                // オンラインプラグインを探す
                var onlinePlugin = OnlinePlugins.FirstOrDefault(o =>
                    (!string.IsNullOrEmpty(localPlugin.Owner) && !string.IsNullOrEmpty(o.Owner) &&
                     string.Equals(localPlugin.Owner, o.Owner, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(localPlugin.Repo, o.Repo, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(localPlugin.DisplayName, o.Name, StringComparison.OrdinalIgnoreCase));

                if (onlinePlugin != null && onlinePlugin.HasUpdate)
                {
                    // バージョン情報がなければ取得
                    if (onlinePlugin.Releases.Count == 0) await LoadReleaseDetails(onlinePlugin);

                    // 最新版をセットして実行
                    onlinePlugin.SelectedVersion = onlinePlugin.Releases.FirstOrDefault();
                    if (onlinePlugin.SelectedVersion != null && SelectedInstance != null)
                    {
                        await ExecuteDownload(onlinePlugin, SelectedInstance);
                    }
                }
            }
        }


        private void BulkToggleFromPortal_Click(object sender, RoutedEventArgs e)
        {
            var selectedOnline = OnlinePlugins.Where(p => p.IsSelected).ToList();
            if (selectedOnline.Count == 0 || !EnsureYmmClosed()) return;

            foreach (var online in selectedOnline)
            {
                var local = _allLocalPlugins.FirstOrDefault(l => l.Owner == online.Owner && l.Repo == online.Repo);
                if (local != null) ToggleOne(local);
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
                catch (Exception ex) { MessageBox.Show(Translate.OpenLinkFailed + ex.Message); }
            }
        }

        private void RefreshLocalPlugins()
        {
            // pluginフォルダが未作成・存在しない場合はエラーなしで0件扱い
            if (SelectedInstance == null
                || string.IsNullOrEmpty(SelectedInstance.PluginDirectory)
                || !Directory.Exists(SelectedInstance.PluginDirectory))
            {
                _allLocalPlugins = new List<LocalPluginInfo>();
                ApplyLocalPluginFilter();
                SyncPortalStatus();
                return;
            }

            var newLocalPlugins = new List<LocalPluginInfo>();

            try
            {
                // 統合info.jsonのパス
                string centralInfoPath = Path.Combine(SelectedInstance.PluginDirectory, "info.json");

                // 既存の統合info.jsonを読み込む（存在する場合）
                PluginsInfo? pluginsInfo = null;
                if (File.Exists(centralInfoPath))
                {
                    try
                    {
                        var json = File.ReadAllText(centralInfoPath);
                        pluginsInfo = JsonSerializer.Deserialize<PluginsInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { }
                }

                if (pluginsInfo == null)
                {
                    pluginsInfo = new PluginsInfo();
                }

                var dirs = Directory.GetDirectories(SelectedInstance.PluginDirectory);
                // 直下の .dll（有効）と .dll.disabled（無効）を両方列挙
                var files = Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll")
                    .Concat(Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll.disabled"))
                    .ToArray();
                bool infoUpdated = false;

                foreach (var path in dirs.Concat(files))
                {
                    var info = new LocalPluginInfo();
                    info.FullPath = path;
                    info.IsDirectory = Directory.Exists(path);

                    // ディレクトリの場合：内部に .dll または .dll.disabled が1つもなければスキップ
                    if (info.IsDirectory)
                    {
                        bool hasDll = Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).Any()
                                   || Directory.EnumerateFiles(path, "*.dll.disabled", SearchOption.AllDirectories).Any();
                        if (!hasDll) continue;
                    }

                    // プラグイン名を取得（ディレクトリ/ファイル名から）
                    string pluginFileName = Path.GetFileName(path);
                    string pluginName = Path.GetFileNameWithoutExtension(path);

                    // _プレフィックスや.disabled拡張子を除去して本来の名前を取得
                    string actualName = pluginFileName;
                    if (info.IsDirectory && pluginFileName.StartsWith("_"))
                    {
                        actualName = pluginFileName.Substring(1);
                    }
                    else if (!info.IsDirectory && pluginFileName.EndsWith(".disabled"))
                    {
                        actualName = pluginFileName.Substring(0, pluginFileName.Length - 9);
                        pluginName = Path.GetFileNameWithoutExtension(actualName);
                    }

                    info.DisplayName = pluginName;

                    // 個別のinfo.jsonとhub_info.jsonから情報を読み取る（削除はしない）
                    string oldInfoPath = string.Empty;
                    string oldHubInfoPath = string.Empty;

                    if (info.IsDirectory)
                    {
                        oldInfoPath = Path.Combine(path, "info.json");
                        oldHubInfoPath = Path.Combine(path, "hub_info.json");
                    }
                    else
                    {
                        var dirName = Path.GetDirectoryName(path);
                        if (dirName != null)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(path);
                            oldInfoPath = Path.Combine(dirName, baseName + ".info.json");
                            oldHubInfoPath = Path.Combine(dirName, baseName + ".hub_info.json");
                        }
                    }

                    // 個別のinfo.jsonから情報を読み取る（削除しない）
                    HubInfo? oldHubInfo = null;
                    if (File.Exists(oldInfoPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(oldInfoPath);
                            oldHubInfo = JsonSerializer.Deserialize<HubInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { }
                    }

                    // hub_info.jsonからも読み取る（削除しない）
                    if (oldHubInfo == null && File.Exists(oldHubInfoPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(oldHubInfoPath);
                            oldHubInfo = JsonSerializer.Deserialize<HubInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { }
                    }

                    // 統合info.jsonから情報を取得（優先）
                    var metadata = pluginsInfo.Plugins.FirstOrDefault(p => p.Name == actualName);
                    if (metadata != null)
                    {
                        info.Owner = metadata.Owner;
                        info.Repo = metadata.Repo;
                        info.Version = metadata.Version;
                        info.Author = metadata.Author;
                        info.Type = metadata.Type;
                        info.PublishedAt = metadata.PublishedAt;
                        info.DownloadedAt = metadata.DownloadedAt;
                        if (!string.IsNullOrEmpty(metadata.PortalName))
                        {
                            info.DisplayName = metadata.PortalName;
                        }
                    }
                    else if (oldHubInfo != null)
                    {
                        // 統合info.jsonになければ個別JSONから取得
                        info.Owner = oldHubInfo.Owner;
                        info.Repo = oldHubInfo.Repo;
                        info.Version = oldHubInfo.Version;
                        info.Author = oldHubInfo.Author;
                        if (!string.IsNullOrEmpty(oldHubInfo.PortalName))
                        {
                            info.DisplayName = oldHubInfo.PortalName;
                        }
                    }

                    newLocalPlugins.Add(info);
                }

                // 統合info.jsonを保存（更新があった場合）
                if (infoUpdated)
                {
                    SaveCentralPluginsInfo(SelectedInstance.PluginDirectory, pluginsInfo);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Translate.LoadError + ex.Message);
            }

            _allLocalPlugins = newLocalPlugins;
            ApplyLocalPluginFilter(); // LocalPluginsを更新
            SyncPortalStatus();
        }

        // 統合info.jsonを更新・保存するヘルパーメソッド
        private void UpdateCentralPluginsInfo(string pluginDirectory, string pluginName, PluginCatalogItem plugin, string version, DateTime? publishedAt = null)
        {
            if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(pluginDirectory)) return;

            try
            {
                string centralInfoPath = Path.Combine(pluginDirectory, "info.json");
                PluginsInfo pluginsInfo;

                // 既存のinfo.jsonを読み込む
                if (File.Exists(centralInfoPath))
                {
                    var json = File.ReadAllText(centralInfoPath);
                    pluginsInfo = JsonSerializer.Deserialize<PluginsInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PluginsInfo();
                }
                else
                {
                    pluginsInfo = new PluginsInfo();
                }

                var now = DateTime.Now;

                // 既存のエントリを探す
                var existingMeta = pluginsInfo.Plugins.FirstOrDefault(p => p.Name == pluginName);
                if (existingMeta != null)
                {
                    existingMeta.PortalName = plugin.Name;
                    existingMeta.Author = plugin.Author;
                    existingMeta.Version = version;
                    existingMeta.Owner = plugin.Owner;
                    existingMeta.Repo = plugin.Repo;
                    existingMeta.Type = plugin.Type;
                    existingMeta.DownloadedAt = now;
                    if (publishedAt.HasValue) existingMeta.PublishedAt = publishedAt;
                }
                else
                {
                    pluginsInfo.Plugins.Add(new PluginMetadata
                    {
                        Name = pluginName,
                        PortalName = plugin.Name,
                        Author = plugin.Author,
                        Version = version,
                        Owner = plugin.Owner,
                        Repo = plugin.Repo,
                        Type = plugin.Type,
                        PublishedAt = publishedAt,
                        DownloadedAt = now
                    });
                }

                SaveCentralPluginsInfo(pluginDirectory, pluginsInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.CentralInfoUpdateFailed, ex.Message));
            }
        }

        // 統合info.jsonを保存する共通メソッド
        private void SaveCentralPluginsInfo(string pluginDirectory, PluginsInfo pluginsInfo)
        {
            try
            {
                string centralInfoPath = Path.Combine(pluginDirectory, "info.json");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var jsonString = JsonSerializer.Serialize(pluginsInfo, options);
                File.WriteAllText(centralInfoPath, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.CentralInfoSaveFailed, ex.Message));
            }
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

            // バックアップフォルダ（各インスタンスの user/backup）
            if (_showBackup)
            {
                foreach (var instance in Instances)
                {
                    if (string.IsNullOrEmpty(instance.ExePath)) continue;
                    var backupDir = Path.Combine(instance.RootDirectory, "user", "backup");
                    if (!Directory.Exists(backupDir)) continue;
                    try
                    {
                        var files = Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories)
                            .Where(s => s.EndsWith(".ymmp") || s.EndsWith(".ymmpx") || s.EndsWith(".ymmx"));
                        foreach (var f in files)
                        {
                            var info = new FileInfo(f);
                            // 重複チェック
                            if (!_allProjects.Any(p => p.FullPath == info.FullName))
                                _allProjects.Add(new ProjectFileItem { Name = info.Name, FullPath = info.FullName, LastWriteTime = info.LastWriteTime, FileSize = info.Length, Extension = info.Extension.ToLower() });
                        }
                    }
                    catch { }
                }
            }

            ApplyProjectFilter();
        }

        private void ApplyProjectFilter()
        {
            var filtered = _allProjects.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(ProjectSearchText)) filtered = filtered.Where(p => p.Name.IndexOf(ProjectSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!ShowYmmp) filtered = filtered.Where(p => p.Extension != ".ymmp");
            if (!ShowYmmpx) filtered = filtered.Where(p => p.Extension != ".ymmpx");
            if (!ShowYmmx) filtered = filtered.Where(p => p.Extension != ".ymmx");
            FilteredProjects.Clear();
            foreach (var p in filtered.OrderByDescending(x => x.LastWriteTime)) FilteredProjects.Add(p);
        }
        /// <summary>
        /// 操作対象インスタンス（SelectedInstance）のプロセスのみを確認・終了する。
        /// インスタンスのexeパスが特定できない場合や、そのプロセスが起動していない場合は即 true を返す。
        /// </summary>
        private bool EnsureYmmClosed()
        {
            // SelectedInstance の exe パスから対象プロセスを特定する
            string? targetExe = SelectedInstance?.ExePath;
            if (string.IsNullOrEmpty(targetExe)) return true;

            string targetName = Path.GetFileNameWithoutExtension(targetExe);
            var processes = Process.GetProcessesByName(targetName)
                .Where(p =>
                {
                    try { return string.Equals(p.MainModule?.FileName, targetExe, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                })
                .ToArray();

            if (processes.Length == 0) return true;

            if (MessageBox.Show(Translate.ExitYmm4ForPlugin, Translate.Confirm, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                foreach (var p in processes) { try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } catch { } }
                return true;
            }
            return false;
        }
        private void TogglePluginFromPortal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PluginCatalogItem onlinePlugin)
            {
                var local = FindLocalPlugin(onlinePlugin);
                if (local != null) ToggleOne(local);
            }
        }

        private void DeletePluginFromPortal_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;
            if (sender is Button btn && btn.DataContext is PluginCatalogItem onlinePlugin)
            {
                var local = FindLocalPlugin(onlinePlugin);
                if (local != null)
                {
                    DeleteOne(local);
                    RefreshLocalPlugins();
                }
            }
        }

        private void ToggleOne(LocalPluginInfo plugin)
        {
            if (SelectedInstance == null) return;

            string currentPath = plugin.FullPath;

            // ディレクトリまたはファイルが存在するか確認
            bool isDirectory = Directory.Exists(currentPath);
            bool isFile = File.Exists(currentPath);

            if (!isDirectory && !isFile)
            {
                MessageBox.Show($"{Translate.TargetNotFound}\n{currentPath}");
                return;
            }

            try
            {
                if (isDirectory)
                {
                    // ディレクトリの場合：フォルダ名先頭の _ を付け外しで有効/無効切替
                    string parentDir = Path.GetDirectoryName(currentPath) ?? string.Empty;
                    string dirName = Path.GetFileName(currentPath);

                    if (plugin.IsEnabled)
                    {
                        // 有効 -> 無効：フォルダ名先頭に _ を追加
                        string newPath = Path.Combine(parentDir, "_" + dirName);
                        Directory.Move(currentPath, newPath);
                        plugin.FullPath = newPath;
                    }
                    else
                    {
                        // 無効 -> 有効：フォルダ名先頭の _ を除去
                        string baseName = dirName.StartsWith("_") ? dirName.Substring(1) : dirName;
                        string newPath = Path.Combine(parentDir, baseName);
                        Directory.Move(currentPath, newPath);
                        plugin.FullPath = newPath;
                    }
                }
                else
                {
                    // ファイルの場合：DLLファイルに.disabledを付け外し
                    if (plugin.IsEnabled)
                    {
                        // 有効 -> 無効
                        if (!currentPath.EndsWith(".disabled"))
                        {
                            File.Move(currentPath, currentPath + ".disabled");
                        }
                    }
                    else
                    {
                        // 無効 -> 有効
                        if (currentPath.EndsWith(".disabled"))
                        {
                            string newPath = currentPath.Substring(0, currentPath.Length - 9);
                            File.Move(currentPath, newPath);
                        }
                    }
                }

                plugin.NotifyPathChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Translate.RenameError}\n{ex.Message}");
            }
        }

        private void DeleteOne(LocalPluginInfo plugin)
        {
            try
            {
                // プラグイン名を取得（_プレフィックスや.disabled拡張子を除去）
                string pluginFileName = Path.GetFileName(plugin.FullPath);
                string actualName = pluginFileName;
                if (plugin.IsDirectory && pluginFileName.StartsWith("_"))
                {
                    actualName = pluginFileName.Substring(1);
                }
                else if (!plugin.IsDirectory && pluginFileName.EndsWith(".disabled"))
                {
                    actualName = pluginFileName.Substring(0, pluginFileName.Length - 9);
                }

                // プラグインの削除
                if (Directory.Exists(plugin.FullPath)) Directory.Delete(plugin.FullPath, true);
                else if (File.Exists(plugin.FullPath)) File.Delete(plugin.FullPath);

                // 統合info.jsonからエントリを削除
                if (SelectedInstance != null && !string.IsNullOrEmpty(SelectedInstance.PluginDirectory))
                {
                    RemoveFromCentralPluginsInfo(SelectedInstance.PluginDirectory, actualName);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // 統合info.jsonからプラグインエントリを削除するヘルパーメソッド
        private void RemoveFromCentralPluginsInfo(string pluginDirectory, string pluginName)
        {
            try
            {
                string centralInfoPath = Path.Combine(pluginDirectory, "info.json");
                if (!File.Exists(centralInfoPath)) return;

                var json = File.ReadAllText(centralInfoPath);
                var pluginsInfo = JsonSerializer.Deserialize<PluginsInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (pluginsInfo != null)
                {
                    pluginsInfo.Plugins.RemoveAll(p => p.Name == pluginName);
                    SaveCentralPluginsInfo(pluginDirectory, pluginsInfo);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.CentralInfoDeleteFailed, ex.Message));
            }
        }

        private void BulkToggle_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalPluginList?.SelectedItems.Cast<LocalPluginInfo>().Where(p => p.IsSelectionValid).ToList()
                           ?? new List<LocalPluginInfo>();
            if (selected.Count == 0 || !EnsureYmmClosed()) return;
            foreach (var p in selected) ToggleOne(p);
            RefreshLocalPlugins();
        }

        private void TogglePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;

            // LocalPluginList でハイライト選択されている全アイテムを取得
            List<LocalPluginInfo> targets = new List<LocalPluginInfo>();
            if (LocalPluginList != null && LocalPluginList.SelectedItems.Count > 0)
            {
                targets = LocalPluginList.SelectedItems.Cast<LocalPluginInfo>().ToList();
            }
            else if (sender is Button btn && btn.DataContext is LocalPluginInfo single)
            {
                targets.Add(single);
            }

            if (targets.Count == 0) return;

            foreach (var plugin in targets)
                ToggleOne(plugin);

            RefreshLocalPlugins();
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            // LocalPluginList でハイライト選択されている全アイテムを取得
            List<LocalPluginInfo> targets = new List<LocalPluginInfo>();
            if (LocalPluginList != null && LocalPluginList.SelectedItems.Count > 0)
            {
                targets = LocalPluginList.SelectedItems.Cast<LocalPluginInfo>().Where(p => p.IsSelectionValid).ToList();
            }
            else if (sender is Button btn && btn.DataContext is LocalPluginInfo single)
            {
                targets.Add(single);
            }

            if (targets.Count == 0) return;

            if (!EnsureYmmClosed()) return;

            foreach (var plugin in targets)
                DeleteOne(plugin);

            RefreshLocalPlugins();
        }

        private void EnablePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;

            // 複数選択されている場合はすべて処理
            if (PluginListView != null && PluginListView.SelectedItems.Count > 1)
            {
                var selectedPlugins = PluginListView.SelectedItems.Cast<PluginCatalogItem>().ToList();
                foreach (var plugin in selectedPlugins)
                {
                    var local = FindLocalPlugin(plugin);
                    if (local != null && !local.IsEnabled) ToggleOne(local);
                }
            }
            else if (SelectedOnlinePlugin != null)
            {
                var local = FindLocalPlugin(SelectedOnlinePlugin);
                if (local != null && !local.IsEnabled) ToggleOne(local);
            }
            RefreshLocalPlugins();
        }

        private void DisablePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;

            // 複数選択されている場合はすべて処理
            if (PluginListView != null && PluginListView.SelectedItems.Count > 1)
            {
                var selectedPlugins = PluginListView.SelectedItems.Cast<PluginCatalogItem>().ToList();
                foreach (var plugin in selectedPlugins)
                {
                    var local = FindLocalPlugin(plugin);
                    if (local != null && local.IsEnabled) ToggleOne(local);
                }
            }
            else if (SelectedOnlinePlugin != null)
            {
                var local = FindLocalPlugin(SelectedOnlinePlugin);
                if (local != null && local.IsEnabled) ToggleOne(local);
            }
            RefreshLocalPlugins();
        }

        private void TogglePluginEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOnlinePlugin == null) return;
            var local = FindLocalPlugin(SelectedOnlinePlugin);
            if (local != null) { ToggleOne(local); RefreshLocalPlugins(); }
        }

        private void UninstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;

            // 複数選択されている場合はすべて処理
            if (PluginListView != null && PluginListView.SelectedItems.Count > 1)
            {
                var selectedPlugins = PluginListView.SelectedItems.Cast<PluginCatalogItem>().ToList();
                foreach (var plugin in selectedPlugins)
                {
                    var local = FindLocalPlugin(plugin);
                    if (local != null) DeleteOne(local);
                }
                RefreshLocalPlugins();
            }
            else if (SelectedOnlinePlugin != null)
            {
                var local = FindLocalPlugin(SelectedOnlinePlugin);
                if (local != null)
                {
                    DeleteOne(local);
                    RefreshLocalPlugins();
                }
            }
        }

        private void RegenerateInfoJson_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null || string.IsNullOrEmpty(SelectedInstance.PluginDirectory))
            {
                MessageBox.Show(Translate.InstanceNotSelected);
                return;
            }

            var result = MessageBox.Show(
                Translate.RegenerateConfirm,
                Translate.Confirm,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var pluginsInfo = new PluginsInfo();

                // 個別のinfo.json/hub_info.jsonから情報を収集
                var dirs = Directory.GetDirectories(SelectedInstance.PluginDirectory);
                var files = Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll");

                foreach (var path in dirs.Concat(files))
                {
                    string pluginFileName = Path.GetFileName(path);
                    string actualName = pluginFileName;

                    // .disabledを除去
                    if (!Directory.Exists(path) && pluginFileName.EndsWith(".disabled"))
                    {
                        actualName = pluginFileName.Substring(0, pluginFileName.Length - 9);
                    }

                    // info.jsonまたはhub_info.jsonを探す
                    string oldInfoPath = string.Empty;
                    string oldHubInfoPath = string.Empty;

                    if (Directory.Exists(path))
                    {
                        oldInfoPath = Path.Combine(path, "info.json");
                        oldHubInfoPath = Path.Combine(path, "hub_info.json");
                    }
                    else
                    {
                        var dirName = Path.GetDirectoryName(path);
                        if (dirName != null)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(path);
                            oldInfoPath = Path.Combine(dirName, baseName + ".info.json");
                            oldHubInfoPath = Path.Combine(dirName, baseName + ".hub_info.json");
                        }
                    }

                    HubInfo? hubInfo = null;
                    if (File.Exists(oldInfoPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(oldInfoPath);
                            hubInfo = JsonSerializer.Deserialize<HubInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { }
                    }

                    if (hubInfo == null && File.Exists(oldHubInfoPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(oldHubInfoPath);
                            hubInfo = JsonSerializer.Deserialize<HubInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { }
                    }

                    if (hubInfo != null)
                    {
                        pluginsInfo.Plugins.Add(new PluginMetadata
                        {
                            Name = actualName,
                            PortalName = hubInfo.PortalName,
                            Author = hubInfo.Author,
                            Version = hubInfo.Version,
                            Owner = hubInfo.Owner,
                            Repo = hubInfo.Repo
                        });
                    }
                }

                // 統合info.jsonを保存
                SaveCentralPluginsInfo(SelectedInstance.PluginDirectory, pluginsInfo);

                MessageBox.Show(string.Format(Translate.RegenerateSuccess, pluginsInfo.Plugins.Count));

                RefreshLocalPlugins();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.RegenerateFailed, ex.Message));
            }
        }

        private async void ReloadPluginPortal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // プラグインポータルを再読み込み
                await LoadOnlinePlugins();
                RefreshLocalPlugins();
                MessageBox.Show(Translate.ReloadPortalSuccess);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.ReloadPortalFailed, ex.Message));
            }
        }

        private void OpenPluginDirectoryRoot_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null || string.IsNullOrEmpty(SelectedInstance.PluginDirectory)) return;

            try
            {
                if (Directory.Exists(SelectedInstance.PluginDirectory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", SelectedInstance.PluginDirectory);
                }
                else
                {
                    MessageBox.Show(Translate.PluginFolderNotFound);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.FolderOpenError, ex.Message));
            }
        }

        private void OpenPluginFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PluginCatalogItem plugin)
            {
                var local = FindLocalPlugin(plugin);
                if (local != null && Directory.Exists(local.FullPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", local.FullPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(string.Format(Translate.FolderOpenError, ex.Message));
                    }
                }
                else
                {
                    MessageBox.Show(Translate.PluginFolderNotFound);
                }
            }
        }

        private void SortOrder_Changed(object sender, RoutedEventArgs e)
        {
            ApplySorting();
        }

        private void ApplySorting()
        {
            if (OnlinePlugins == null) return;
            var view = CollectionViewSource.GetDefaultView(OnlinePlugins);
            if (view == null) return;

            view.SortDescriptions.Clear();

            var direction = SortDescendingDir?.IsChecked == true
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            if (SortByName?.IsChecked == true)
            {
                view.SortDescriptions.Add(new SortDescription("Name", direction));
            }
            else if (SortByAuthor?.IsChecked == true)
            {
                view.SortDescriptions.Add(new SortDescription("Author", direction));
            }
            else if (SortByType?.IsChecked == true)
            {
                view.SortDescriptions.Add(new SortDescription("Type", direction));
            }
            else if (SortByStatus?.IsChecked == true)
            {
                view.SortDescriptions.Add(new SortDescription("LocalStatus", direction));
            }
            else if (SortByPublishDate?.IsChecked == true)
            {
                // 初公開日時順：最も古いリリース日（昇順=古い順、降順=新しい順）
                view.SortDescriptions.Add(new SortDescription("FirstPublishedAt", direction));
            }
            else if (SortByLatestDate?.IsChecked == true)
            {
                // 最終更新日時順
                view.SortDescriptions.Add(new SortDescription("LatestPublishedAt", direction));
            }
        }
        private LocalPluginInfo? FindLocalPlugin(PluginCatalogItem online)
        {
            return _allLocalPlugins?.FirstOrDefault(l =>
                (!string.IsNullOrEmpty(l.Owner) && l.Owner == online.Owner && l.Repo == online.Repo) ||
                (l.DisplayName == online.Name));
        }
        private void BulkDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalPluginList?.SelectedItems.Cast<LocalPluginInfo>().Where(p => p.IsSelectionValid).ToList()
                           ?? new List<LocalPluginInfo>();
            if (selected.Count == 0) return;
            if (!EnsureYmmClosed()) return;
            foreach (var p in selected) DeleteOne(p);
            RefreshLocalPlugins();
        }

        // OpenLocalPluginFolder_Clickメソッド
        private void OpenLocalPluginFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LocalPluginInfo plugin)
            {
                if (Directory.Exists(plugin.FullPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", plugin.FullPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(string.Format(Translate.FolderOpenError, ex.Message));
                    }
                }
                else
                {
                    MessageBox.Show(Translate.FolderNotFound);
                }
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header && header.Column != null)
            {
                string? field = header.Tag as string ?? header.Content?.ToString();
                if (string.IsNullOrEmpty(field)) return;

                // プラグイン管理リストのソート
                var view = CollectionViewSource.GetDefaultView(LocalPlugins);
                if (view != null)
                {
                    // 同じフィールドなら方向を反転
                    if (field == _lastPluginSortField)
                    {
                        _lastPluginSortDirection = _lastPluginSortDirection == ListSortDirection.Ascending
                            ? ListSortDirection.Descending
                            : ListSortDirection.Ascending;
                    }
                    else
                    {
                        _lastPluginSortField = field;
                        _lastPluginSortDirection = ListSortDirection.Ascending;
                    }

                    view.SortDescriptions.Clear();
                    // プラグイン名は日本語順（DisplayNameSortKey）でソート
                    string sortField = field == "DisplayName" ? "DisplayNameSortKey" : field;
                    view.SortDescriptions.Add(new SortDescription(sortField, _lastPluginSortDirection));
                }
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

        private void ApplyLocalPluginFilter()
        {
            var filtered = _allLocalPlugins.Where(p =>
                string.IsNullOrWhiteSpace(LocalPluginSearchText) ||
                (p.DisplayName != null && p.DisplayName.Contains(LocalPluginSearchText, StringComparison.OrdinalIgnoreCase)) ||
                (p.Author != null && p.Author.Contains(LocalPluginSearchText, StringComparison.OrdinalIgnoreCase)) ||
                (p.Version != null && p.Version.Contains(LocalPluginSearchText, StringComparison.OrdinalIgnoreCase)));

            LocalPlugins.Clear();
            foreach (var p in filtered) LocalPlugins.Add(p);

            // ソートを復元（プラグイン名は DisplayNameSortKey で日本語順）
            var view = CollectionViewSource.GetDefaultView(LocalPlugins);
            if (view != null)
            {
                view.SortDescriptions.Clear();
                string sortField = _lastPluginSortField == "DisplayName" ? "DisplayNameSortKey" : _lastPluginSortField;
                view.SortDescriptions.Add(new SortDescription(sortField, _lastPluginSortDirection));
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
        private void ForceKillInstance_Click(object sender, RoutedEventArgs e)
        {
            var instance = (sender as FrameworkElement)?.DataContext as InstanceInfo;
            if (instance == null || string.IsNullOrEmpty(instance.ExePath)) return;

            string procName = Path.GetFileNameWithoutExtension(instance.ExePath);
            var processes = Process.GetProcessesByName(procName)
                .Where(p =>
                {
                    try { return string.Equals(p.MainModule?.FileName, instance.ExePath, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                })
                .ToArray();

            foreach (var p in processes)
            {
                try { p.Kill(); } catch { }
            }
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
        private void ChangeIcon_Click(object sender, RoutedEventArgs e) { if (SelectedInstance == null) return; var d = new OpenFileDialog { Filter = Translate.Image + "|*.png;*.jpg;*.ico" }; if (d.ShowDialog() == true) SelectedInstance.IconPath = d.FileName; }
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

        private void OpenWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string dir && Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
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
        private void ApplyOnlinePluginFilter()
        {
            if (OnlinePlugins == null) return;

            var selectedTypes = PluginTypeFilters
                .Where(f => f.IsSelected)
                .Select(f => f.InternalName)
                .ToList();

            if (_allOnlinePlugins == null) return;

            var query = OnlinePluginSearchText?.ToLower() ?? "";

            bool showDisabled = selectedTypes.Contains("配布終了");

            var filtered = _allOnlinePlugins.Where(p =>
            {
                // 検索フィルタ（名前、作者、説明、バージョン）
                if (!string.IsNullOrWhiteSpace(query))
                {
                    bool matchesSearch =
                        (p.Name?.ToLower().Contains(query) ?? false) ||
                        (p.Author?.ToLower().Contains(query) ?? false) ||
                        (p.Description?.ToLower().Contains(query) ?? false) ||
                        (p.LatestVersionName?.ToLower().Contains(query) ?? false) ||
                        (p.LocalVersion?.ToLower().Contains(query) ?? false);

                    if (!matchesSearch) return false;
                }

                // タイプフィルタ
                bool typeMatch = (p.IsEnabled && selectedTypes.Contains(string.IsNullOrEmpty(p.Type) ? "その他" : p.Type)) ||
                                (!p.IsEnabled && showDisabled);

                if (!typeMatch) return false;

                // ダウンロード状態フィルタ
                bool isInstalled = p.LocalVersion != "-" && !string.IsNullOrEmpty(p.LocalVersion);

                if (FilterDownloaded?.IsChecked == true && !isInstalled) return false;
                if (FilterNotDownloaded?.IsChecked == true && isInstalled) return false;
                if (FilterHasUpdate?.IsChecked == true && !p.HasUpdate) return false;

                return true;
            });

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
                MessageBox.Show(Translate.SelectInstallVersion);
                return;
            }
            if (!File.Exists(SelectedInstance.InstallerPath))
            {
                MessageBox.Show($"{Translate.InstallerNotFound}\n{Translate.Path} {SelectedInstance.InstallerPath}");
                return;
            }
            var release = SelectedOnlinePlugin.SelectedVersion;
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "YMM4Hub");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string savePath = Path.Combine(tempDir, release.FileName);
                using var request = new HttpRequestMessage(HttpMethod.Get, release.BrowserDownloadUrl);
                request.Headers.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
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
            catch (Exception ex) { MessageBox.Show($"{Translate.DownloadError} {ex.Message}"); }
            finally { RefreshLocalPlugins(); }
        }

        // Ctrl+Aで全選択
        private void PluginListView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (PluginListView != null)
                {
                    PluginListView.SelectAll();
                    e.Handled = true;
                }
            }
        }

        // 予約に追加
        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (PluginListView == null) return;

            // ListViewでハイライト選択されているアイテムにチェック（IsSelected）を付ける
            var highlighted = PluginListView.SelectedItems.Cast<PluginCatalogItem>().ToList();
            if (highlighted.Count == 0)
            {
                MessageBox.Show(Translate.SelectPluginToInstall);
                return;
            }

            foreach (var plugin in highlighted)
                plugin.IsSelected = true;
        }
        // リスト更新
        private async void RefreshPluginList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadOnlinePlugins();
                RefreshLocalPlugins();
                MessageBox.Show(Translate.RefreshListSuccess);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.RefreshListFailed, ex.Message));
            }
        }

        // 一括ダウンロード
        private async void BulkDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = OnlinePlugins.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(Translate.SelectDownloadPlugin);
                return;
            }

            var bulkWindow = new BulkDownloadWindow(selected, Instances.ToList());
            if (bulkWindow.ShowDialog() == true)
            {
                var selectedInstances = bulkWindow.SelectedInstances;
                var selectedPlugins = bulkWindow.SelectedPlugins;

                await ExecuteBulkDownload(selectedPlugins, selectedInstances);
            }
        }

        // 一括ダウンロード実行
        private async Task ExecuteBulkDownload(List<PluginCatalogItem> plugins, List<InstanceInfo> instances)
        {
            if (!EnsureYmmClosed()) return;

            var progressWin = new DownloadProgressWindow();
            progressWin.Owner = this;
            progressWin.Show();

            int totalTasks = plugins.Count * instances.Count;
            int currentTask = 0;

            foreach (var instance in instances)
            {
                foreach (var plugin in plugins)
                {
                    currentTask++;
                    string statusMsg = $"[{currentTask}/{totalTasks}] {instance.Name} - {plugin.Name}";
                    progressWin.UpdateStatus(statusMsg, ((double)(currentTask - 1) / totalTasks) * 100, $"{currentTask} / {totalTasks}");

                    try
                    {
                        // バージョン情報がなければ取得
                        if (plugin.Releases.Count == 0)
                        {
                            await LoadReleaseDetails(plugin);
                        }

                        // 最新版をセット
                        plugin.SelectedVersion = plugin.Releases.FirstOrDefault();

                        if (plugin.SelectedVersion != null)
                        {
                            await ExecuteDownload(plugin, instance, progressWin, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        progressWin.AddReadme(plugin.Name, $"【{instance.Name}】\n\n{Translate.LoadError}{ex.Message}");
                    }

                    progressWin.UpdateStatus(statusMsg, ((double)currentTask / totalTasks) * 100, $"{currentTask} / {totalTasks}");
                }
            }

            RefreshLocalPlugins();
            progressWin.ShowFinalClose();
        }

        private void ToggleViewMode_Click(object sender, RoutedEventArgs e)
        {
            IsViewTile = !IsViewTile;
        }

        // ダウンロード状態フィルタ変更
        private void DownloadFilter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyOnlinePluginFilter();
        }
    }
}