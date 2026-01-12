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
        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "ja-JP";
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
        private ObservableCollection<GitHubReleaseDetail> _releases = new ObservableCollection<GitHubReleaseDetail>();
        public ObservableCollection<GitHubReleaseDetail> Releases { get => _releases; set { _releases = value; OnPropertyChanged(nameof(Releases)); } }
        private GitHubReleaseDetail? _selectedVersion;
        public GitHubReleaseDetail? SelectedVersion { get => _selectedVersion; set { _selectedVersion = value; OnPropertyChanged(nameof(SelectedVersion)); } }

        public string LatestVersionName
        {
            get
            {
                if (!IsEnabled) return Translate.EndDistribution;
                bool hasGitHub = (Url != null && Url.Contains("github.com")) || (Links != null && Links.Any(l => l != null && l.Contains("github.com")));
                if (!hasGitHub) return Translate.NoInfo;
                if (Releases == null || Releases.Count == 0) return Translate.Acquiring;
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
        public string FullPath { get => _fullPath; set { _fullPath = value; OnPropertyChanged(nameof(FullPath)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(IsEnabled)); OnPropertyChanged(nameof(IsSelectionValid)); } }
        public bool IsDirectory { get; set; }
        private string? _displayName;
        public string DisplayName
        {
            get
            {
                if (FullPath == "DUMMY_NONE_SELECTED")
                {
                    return $"--- {Translate.SelectInstance} ---";
                }
                return !string.IsNullOrEmpty(_displayName) ? _displayName : (IsDirectory ? Path.GetFileName(FullPath) : Path.GetFileNameWithoutExtension(FullPath).Replace(".disabled", ""));
            }
            set { _displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        public bool IsEnabled
        {
            get
            {
                if (FullPath == "DUMMY_NONE_SELECTED") return false;
                if (string.IsNullOrEmpty(FullPath)) return false;
                return IsDirectory ? !Path.GetFileName(FullPath).StartsWith("_") : !Path.GetFileName(FullPath).EndsWith(".disabled");
            }
        }

        public bool IsSelectionValid => FullPath != "DUMMY_NONE_SELECTED" && !string.IsNullOrEmpty(FullPath);
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

    public class PluginTypeFilterItem : INotifyPropertyChanged
    {
        public string InternalName { get; set; } = string.Empty;

        public string DisplayName => GetDisplayName(InternalName);

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

        private string GetDisplayName(string internalName)
        {
            return internalName switch
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
                _ => internalName
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _latestOnlineVersion = string.Empty;
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
        private readonly InstanceInfo _dummyInstance = new InstanceInfo { Name =Translate.SelectInstance, ExePath = string.Empty };

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
                RefreshLocalPlugins();
            }
        }
        private PluginCatalogItem? _selectedOnlinePlugin;
        public PluginCatalogItem? SelectedOnlinePlugin { get => _selectedOnlinePlugin; set { _selectedOnlinePlugin = value; OnPropertyChanged(nameof(SelectedOnlinePlugin)); if (value != null) _ = LoadReleaseDetails(value); } }
        public FontItem? SelectedFontItem
        {
            get => FilteredFonts.FirstOrDefault(f => f.InternalName == _currentSettings.FontFamily);
            set { if (value != null) { _currentSettings.FontFamily = value.InternalName; ApplyTheme(); SaveAll(); OnPropertyChanged(nameof(SelectedFontItem)); } }
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
            InitializeComponent();
            DataContext = this;
            _currentSettings = _settingsManager.Load();

            string langCode = _currentSettings.LanguageCode ?? "ja-JP";
            var culture = new CultureInfo(langCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

            InitializeComponent();
            this.DataContext = this;

            _selectedLanguage = Languages.FirstOrDefault(l => l.Code == langCode) ?? Languages[0];
            OnPropertyChanged(nameof(SelectedLanguage));

            this.FlowDirection = (langCode == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            _http.DefaultRequestHeaders.Add("User-Agent", "YukkuriMovieMaker4Hub");
            RefreshLocalPlugins();
            LoadOnlinePlugins();
            InitializeComponent();
            _currentSettings = _settingsManager.Load();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub");
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
            RestoreLastSelection();
            RefreshRecentProjects();
            _ = CheckYmmUpdates();
            _ = CheckForHubUpdateAsync();
        }
        private static readonly string HubVersion = "3.1.0";
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
        private void OpenPluginFolder_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance != null && Directory.Exists(SelectedInstance.PluginDirectory))
            {
                Process.Start("explorer.exe", SelectedInstance.PluginDirectory);
            }
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
                this.Resources["AppFont"] = new FontFamily(_currentSettings.FontFamily);
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
            try
            {
                _currentSettings.Instances = Instances.ToList();
                _currentSettings.ProjectDirectories = ProjectDirectories.ToList();
                _currentSettings.GitHubToken = GitHubTokenBox.Password;
                _settingsManager.Save(_currentSettings);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    Translate.AccessDenied ?? Translate.AdminPrivilegeRequired,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save Error: {ex.Message}");
            }
        }

        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe",
                Title = "Select YMM4 Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newInstance = new InstanceInfo
                    {
                        Name = Path.GetFileName(Path.GetDirectoryName(dialog.FileName)) ?? "New Instance",
                        ExePath = dialog.FileName
                    };
                    newInstance.PropertyChanged += (s, ev) => SaveAll();
                    Instances.Add(newInstance);
                    SelectedInstance = newInstance;
                    SaveAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to add instance: {ex.Message}");
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
            catch (Exception ex) { MessageBox.Show(Translate.PortalLoadFailed + ex.Message); }
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
                    MessageBox.Show(Translate.GitHubApiLimit);
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
                catch (Exception ex) { MessageBox.Show(Translate.OpenLinkFailed + ex.Message); }
            }
        }

        private void RefreshLocalPlugins()
        {
            if (SelectedInstance == _dummyInstance || string.IsNullOrEmpty(SelectedInstance.ExePath) || !Directory.Exists(SelectedInstance.PluginDirectory))
            {
                var dummyList = new List<LocalPluginInfo> { new LocalPluginInfo { FullPath = "DUMMY_NONE_SELECTED" } };
                _allLocalPlugins = dummyList;
                LocalPlugins.Clear();
                foreach (var item in dummyList) LocalPlugins.Add(item);
                ApplyLocalPluginFilter();
                return;
            }

            var items = new List<LocalPluginInfo>();

            foreach (var dir in Directory.GetDirectories(SelectedInstance.PluginDirectory))
            {
                var info = new LocalPluginInfo { FullPath = dir, IsDirectory = true };
                info.DisplayName = GetPluginNameFromYml(dir);
                items.Add(info);
            }
            foreach (var file in Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll*"))
            {
                items.Add(new LocalPluginInfo { FullPath = file, IsDirectory = false });
            }

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
            if (MessageBox.Show(Translate.ExitYmm4ForPlugin, Translate.Confirm, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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
            if (MessageBox.Show($"{selected.Count}{Translate.DeleteCountPermanently}\n{Translate.CannotBeUndone}", Translate.ConfirmDeletion, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!EnsureYmmClosed()) return;
            foreach (var p in selected) DeleteOne(p);
            RefreshLocalPlugins();
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn && btn.DataContext is LocalPluginInfo plugin)) return;
            if (MessageBox.Show($"{plugin.DisplayName} {Translate.DeletePermanently}", Translate.Confirm, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (!EnsureYmmClosed()) return;
                DeleteOne(plugin);
                RefreshLocalPlugins();
            }
        }

        private void DeleteOne(LocalPluginInfo p)
        {
            try { if (p.IsDirectory) Directory.Delete(p.FullPath, true); else File.Delete(p.FullPath); }
            catch (Exception ex) { MessageBox.Show($"{p.DisplayName}{Translate.DeleteFailed} {ex.Message}"); }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header && header.Column != null)
            {
                string? headerText = header.Content?.ToString();
                string? field = null;
                if (headerText == Translate.PluginName) field = "DisplayName";
                else if (headerText == Translate.Status) field = "IsEnabled";
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
            if (OnlinePlugins == null) return;

            var selectedTypes = PluginTypeFilters
                .Where(f => f.IsSelected)
                .Select(f => f.InternalName)
                .ToList();

            if (_allOnlinePlugins == null) return;

            var query = OnlinePluginSearchText?.ToLower() ?? "";

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
    }
}