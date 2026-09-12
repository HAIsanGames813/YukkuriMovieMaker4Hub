using System;
using System.Collections.Generic;
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
using System.Threading;
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

namespace YukkuriMovieMaker4Hub
{
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

        public bool IsViewTile
        {
            get => _currentSettings.IsViewTile;
            set { _currentSettings.IsViewTile = value; SaveAll(); OnPropertyChanged(nameof(IsViewTile)); }
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
        public bool HideExePath
        {
            get => _currentSettings.HideExePath;
            set { _currentSettings.HideExePath = value; SaveAll(); OnPropertyChanged(nameof(HideExePath)); }
        }
        public bool AutoOpenSiteOnBulkDownload
        {
            get => _currentSettings.AutoOpenSiteOnBulkDownload;
            set { _currentSettings.AutoOpenSiteOnBulkDownload = value; SaveAll(); OnPropertyChanged(nameof(AutoOpenSiteOnBulkDownload)); }
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
        public string HubVersionText => $"現在のバージョン: v{HubVersion}";

        private static readonly string HubVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private static bool IsNewerVersion(string latestTag, string currentVersion)
        {
            if (string.IsNullOrEmpty(latestTag)) return false;
            string v1Str = latestTag.TrimStart('v', 'V');
            string v2Str = currentVersion.TrimStart('v', 'V');
            if (Version.TryParse(v1Str, out var v1) && Version.TryParse(v2Str, out var v2))
            {
                return v1 > v2;
            }
            return !string.Equals(latestTag, currentVersion, StringComparison.OrdinalIgnoreCase);
        }

        private async Task CheckForHubUpdateAsync(bool isManual = false)
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

                    bool hasUpdate = IsNewerVersion(latestTag, HubVersion);

                    if (hasUpdate)
                    {
                        if (!isManual && !string.IsNullOrEmpty(_currentSettings.IgnoreHubUpdateTag) &&
                            string.Equals(_currentSettings.IgnoreHubUpdateTag, latestTag, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        string? downloadUrl = null;
                        string? fileName = null;
                        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                        {
                            var asset = assets[0];
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            fileName = asset.GetProperty("name").GetString();
                        }

                        var dlg = new HubUpdateDialog(HubVersion, latestTag, downloadUrl, fileName) { Owner = this };
                        dlg.ShowDialog();

                        if (dlg.DoNotShowAgain)
                        {
                            _currentSettings.IgnoreHubUpdateTag = latestTag;
                            SaveAll();
                        }

                        if (dlg.ExecuteUpdate && !string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(fileName))
                        {
                            await DownloadAndExecuteUpdateAsync(downloadUrl, fileName);
                        }
                    }
                    else if (isManual)
                    {
                        MessageBox.Show($"現在のバージョン（v{HubVersion}）は最新です。", "アップデート確認", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (isManual)
                {
                    MessageBox.Show("最新情報の取得に失敗しました。ネットワーク接続を確認してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                if (isManual)
                {
                    MessageBox.Show($"アップデートの確認中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CheckHubUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForHubUpdateAsync(isManual: true);
        }

        private async void ExecuteHubUpdate_Click(object sender, RoutedEventArgs e)
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

                    if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var asset = assets[0];
                        var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        var fileName = asset.GetProperty("name").GetString();

                        if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(fileName))
                        {
                            var r = MessageBox.Show($"最新バージョン {latestTag} をダウンロードしてアップデートを実行しますか？", "アップデートの実行", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (r == MessageBoxResult.Yes)
                            {
                                await DownloadAndExecuteUpdateAsync(downloadUrl, fileName);
                            }
                            return;
                        }
                    }
                }
                MessageBox.Show("ダウンロード可能なアセットが見つかりませんでした。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アップデート実行中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Translate.DownloadError}\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

                switch (mode)
                {
                    case AppTheme.Windows:
                        SetThemeColors("#F0F0F0", "#FFFFFF", "#E5E5E5", "#000000", "#555555", "#0078D4", "#CCCCCC");
                        break;
                    case AppTheme.Light:
                        SetThemeColors("#FFFFFF", "#FAFAFA", "#FFFFFF", "#000000", "#555555", "#0078D4", "#E0E0E0");
                        break;
                    case AppTheme.Dark:
                        SetThemeColors("#252525", "#333333", "#444444", "#FFFFFF", "#BBBBBB", "#4CAF50", "#555555");
                        break;
                    case AppTheme.Black:
                        SetThemeColors("#000000", "#121212", "#1F1F1F", "#FFFFFF", "#CCCCCC", "#4CAF50", "#333333");
                        break;
                }

                // DynamicAero2 テーマカラー連動
                var aeroTheme = Application.Current?.Resources?.MergedDictionaries?.OfType<DynamicAero2.Theme>()?.FirstOrDefault();
                if (aeroTheme != null)
                {
                    switch (mode)
                    {
                        case AppTheme.Windows:
                            aeroTheme.Color = DynamicAero2.ThemeColor.NormalColor;
                            break;
                        case AppTheme.Light:
                            aeroTheme.Color = DynamicAero2.ThemeColor.Light;
                            break;
                        case AppTheme.Dark:
                            aeroTheme.Color = DynamicAero2.ThemeColor.Dark;
                            break;
                        case AppTheme.Black:
                            aeroTheme.Color = DynamicAero2.ThemeColor.Black;
                            break;
                    }
                }

                ThemeHelper.ApplyTitleBarTheme(this, ThemeHelper.IsCurrentDarkTheme);
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


        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            var setupDialog = new InstanceSetupDialog { Owner = this };
            if (setupDialog.ShowDialog() != true || string.IsNullOrEmpty(setupDialog.ResultExePath))
                return;

            try
            {
                var newInstance = new InstanceInfo
                {
                    Name = setupDialog.InstanceName,
                    ExePath = setupDialog.ResultExePath,
                };
                setupDialog.CopyIconSettingsTo(newInstance);
                newInstance.PropertyChanged += (s, ev) => SaveAll();
                Instances.Add(newInstance);
                SelectedInstance = newInstance;
                SaveAll();

                // 設定ファイルのコピー
                if (setupDialog.InheritedSettingFiles.Count > 0)
                    CopyInheritedSettings(setupDialog.InheritedSettingFiles, newInstance);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.AddInstanceFailed, ex.Message));
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
                var yml = await _http.GetStringAsync("https://manjubox.net/ymm4plugins.yml");
                var catalog = ParseYmm4PluginsYaml(yml);

                // 全YAMLプラグインのGitHub Owner/Repoを抽出
                foreach (var p in catalog) p.EnsureGitHubOwnerRepo();

                // GitHub連携・未掲載プラグイン一覧API取得 (manjubox.net/api/ymm4plugins/github/list)
                try
                {
                    var ghListJson = await _http.GetStringAsync("https://manjubox.net/api/ymm4plugins/github/list");
                    using var doc = JsonDocument.Parse(ghListJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            // "user" または "owner"
                            string owner = "";
                            if (item.TryGetProperty("user", out var u) && !string.IsNullOrEmpty(u.GetString()))
                                owner = u.GetString()!;
                            else if (item.TryGetProperty("owner", out var o) && !string.IsNullOrEmpty(o.GetString()))
                                owner = o.GetString()!;

                            string repo = item.TryGetProperty("repo", out var r) ? r.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)) continue;

                            string tagName = item.TryGetProperty("tag_name", out var tn) ? tn.GetString() ?? "" : "";
                            string relName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : tagName;
                            string fileName = item.TryGetProperty("file_name", out var fn) ? fn.GetString() ?? "" : "";
                            string downloadUrl = item.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                            DateTime pubDate = DateTime.MinValue;
                            if (item.TryGetProperty("published_at", out var pa) && DateTime.TryParse(pa.GetString(), out var pd))
                                pubDate = pd;
                            bool isPre = item.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();

                            var releaseDetail = new GitHubReleaseDetail
                            {
                                TagName = !string.IsNullOrEmpty(tagName) ? tagName : relName,
                                FileName = fileName,
                                BrowserDownloadUrl = downloadUrl,
                                PublishedAt = pubDate,
                                Prerelease = isPre
                            };

                            // catalog 内で同一リポジトリのプラグインを検索（バージョン違いは同一プラグインに集約）
                            var existing = catalog.FirstOrDefault(p =>
                                (!string.IsNullOrEmpty(p.Owner) && !string.IsNullOrEmpty(p.Repo) &&
                                 p.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
                                 p.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(p.Url) && p.Url.Contains($"github.com/{owner}/{repo}", StringComparison.OrdinalIgnoreCase)));

                            if (existing != null)
                            {
                                existing.EnsureGitHubOwnerRepo();
                                if (existing.Releases.All(rel => rel.TagName != releaseDetail.TagName))
                                {
                                    existing.Releases.Add(releaseDetail);
                                }
                                existing.ReleaseLoaded = true;
                                existing.HasNoRelease = false;
                                if (existing.SelectedVersion == null || releaseDetail.PublishedAt >= (existing.SelectedVersion?.PublishedAt ?? DateTime.MinValue))
                                {
                                    existing.SelectedVersion = releaseDetail;
                                }
                            }
                            else
                            {
                                // YAML に未掲載のリポジトリの場合、リポジトリ単位で1件のみ追加
                                var newPlugin = new PluginCatalogItem
                                {
                                    Name = repo,
                                    Author = owner,
                                    Description = "",
                                    Type = "その他",
                                    Owner = owner,
                                    Repo = repo,
                                    Url = $"https://github.com/{owner}/{repo}",
                                    IsEnabled = true,
                                    IsYmlItem = false,
                                    IsExternalGh = true,
                                    ReleaseLoaded = true,
                                    HasNoRelease = false
                                };
                                newPlugin.Releases.Add(releaseDetail);
                                newPlugin.SelectedVersion = releaseDetail;
                                catalog.Add(newPlugin);
                            }
                        }
                    }
                }
                catch { }

                _allOnlinePlugins = catalog;

                // 各カテゴリの件数更新
                UpdateCategoryFilterCounts();

                // 最新バージョンRSS取得
                try
                {
                    var rss = await _http.GetStringAsync("https://manjubox.net/rss.xml");
                    var match = Regex.Match(rss, @"v(\d+\.\d+\.\d+\.\d+)");
                    if (match.Success)
                        Ymm4LatestVersionText = $"YMM4最新: {match.Value}";
                }
                catch { }

                OnlinePlugins.Clear();
                foreach (var p in _allOnlinePlugins) OnlinePlugins.Add(p);
                ApplyOnlinePluginFilter();

                var detailTasks = _allOnlinePlugins.Where(p => p.IsGitHub).Select(async plugin =>
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

                _ = Task.WhenAll(detailTasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Translate.PortalLoadFailed + ex.Message);
            }
        }
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

                        // 対応拡張子（.ymme/.zip/.dll）のアセットを優先して選ぶ
                        GitHubReleaseDetail? detail = null;
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var assetName = asset.GetProperty("name").GetString() ?? "";
                            var lower = assetName.ToLower();
                            if (lower.EndsWith(".ymme") || lower.EndsWith(".zip") || lower.EndsWith(".dll"))
                            {
                                detail = new GitHubReleaseDetail
                                {
                                    TagName = tagName,
                                    PublishedAt = publishedAt,
                                    Prerelease = isPrerelease,
                                    BrowserDownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "",
                                    FileName = assetName
                                };
                                break; // 最初の対応アセットで確定
                            }
                        }
                        // 対応アセットがなくてもリリース自体は記録（アセットなし扱い）
                        if (detail == null && assets.GetArrayLength() > 0)
                        {
                            var first = assets[0];
                            detail = new GitHubReleaseDetail
                            {
                                TagName = tagName,
                                PublishedAt = publishedAt,
                                Prerelease = isPrerelease,
                                BrowserDownloadUrl = "",
                                FileName = first.GetProperty("name").GetString() ?? ""
                            };
                        }
                        else if (detail == null)
                        {
                            // アセット0のリリース（タグのみ）も記録
                            detail = new GitHubReleaseDetail
                            {
                                TagName = tagName,
                                PublishedAt = publishedAt,
                                Prerelease = isPrerelease,
                                BrowserDownloadUrl = "",
                                FileName = ""
                            };
                        }
                        releaseList.Add(detail);
                    }

                    // UIスレッドでデータをセット
                    Dispatcher.Invoke(() => {
                        plugin.Releases = new ObservableCollection<GitHubReleaseDetail>(releaseList);
                        if (plugin.Releases.Count > 0)
                        {
                            // 対応アセット付きリリースを優先して SelectedVersion に設定
                            plugin.SelectedVersion = plugin.Releases.FirstOrDefault(r =>
                            {
                                var fn = r.FileName.ToLower();
                                return fn.EndsWith(".ymme") || fn.EndsWith(".zip") || fn.EndsWith(".dll");
                            }) ?? plugin.Releases[0];
                            plugin.HasNoRelease = false;
                        }
                        else
                        {
                            plugin.HasNoRelease = true;
                        }
                        plugin.ReleaseLoaded = true;
                        plugin.OnPropertyChanged(nameof(plugin.LatestVersionName));
                        plugin.OnPropertyChanged(nameof(plugin.IsAssetDownloadable));
                        plugin.OnPropertyChanged(nameof(plugin.HasUpdate));
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
            bool inTags = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (line.StartsWith("- "))
                {
                    current = new PluginCatalogItem();
                    plugins.Add(current);
                    inLinks = false;
                    inTags = false;
                    var firstKeyValue = trimmed.Substring(2).Split(new[] { ':' }, 2);
                    if (firstKeyValue.Length == 2) ApplyYamlValue(current, firstKeyValue[0].Trim(), firstKeyValue[1].Trim(), ref inLinks, ref inTags);
                }
                else if (current != null)
                {
                    if (trimmed.StartsWith("-") && inLinks)
                        current.Links.Add(trimmed.Substring(1).Trim().Trim('\'', '\"'));
                    else if (trimmed.StartsWith("-") && inTags)
                        current.Tags.Add(trimmed.Substring(1).Trim().Trim('\'', '\"'));
                    else
                    {
                        var keyValue = trimmed.Split(new[] { ':' }, 2);
                        if (keyValue.Length == 2) ApplyYamlValue(current, keyValue[0].Trim(), keyValue[1].Trim(), ref inLinks, ref inTags);
                    }
                }
            }
            return plugins;
        }

        private void ApplyYamlValue(PluginCatalogItem item, string key, string value, ref bool inLinks, ref bool inTags)
        {
            value = value.Trim('\'', '\"');
            switch (key.ToLower())
            {
                case "name": item.Name = value; inLinks = false; inTags = false; break;
                case "author": item.Author = value; inLinks = false; inTags = false; break;
                case "description": item.Description = value; inLinks = false; inTags = false; break;
                case "type": item.Type = value; inLinks = false; inTags = false; break;
                case "price": item.Price = value; inLinks = false; inTags = false; break;
                case "license": item.License = value; inLinks = false; inTags = false; break;
                case "date":
                case "publishedat":
                    if (DateTime.TryParse(value, out var pubDt)) item.PublishedAt = pubDt;
                    inLinks = false; inTags = false; break;
                case "updatedat":
                    if (DateTime.TryParse(value, out var updDt)) item.UpdatedAt = updDt;
                    inLinks = false; inTags = false; break;
                case "isenabled":
                    if (bool.TryParse(value, out bool enabled)) item.IsEnabled = enabled;
                    inLinks = false; inTags = false;
                    break;
                case "url": item.Url = value; inLinks = false; inTags = false; break;
                case "links": inLinks = true; inTags = false; break;
                case "tags": inTags = true; inLinks = false; break;
                default: inLinks = false; inTags = false; break;
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
            // ItemSizeSlider removed in new UI
        }

        private async void DirectDownload_Click(object sender, RoutedEventArgs e)
        {
            var plugin = (sender as Button)?.DataContext as PluginCatalogItem ?? SelectedOnlinePlugin;
            if (plugin == null || SelectedInstance == null) return;

            // DL不可（サイトで確認状態）のときは BestSiteUrl を規定ブラウザで開く
            if (!plugin.IsAssetDownloadable)
            {
                var url = plugin.BestSiteUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show(Translate.OpenLinkFailed + ex.Message); }
                }
                return;
            }

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
            foreach (var plugin in OnlinePlugins) plugin.IsSelected = true;
            UpdatePortalSelectionBar();
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

        // BOOTH→情報サイト→X→YouTube→ニコニコ→その他 の優先順位で開く
        private void OpenBestSite_Click(object sender, RoutedEventArgs e)
        {
            var url = SelectedOnlinePlugin?.BestSiteUrl;
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(Translate.OpenLinkFailed + ex.Message); }
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
            if (SelectedOnlinePlugin != null)
            {
                var local = FindLocalPlugin(SelectedOnlinePlugin);
                if (local != null && !local.IsEnabled) ToggleOne(local);
            }
            RefreshLocalPlugins();
        }

                private void DisablePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureYmmClosed()) return;
            if (SelectedOnlinePlugin != null)
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
            if (SelectedOnlinePlugin != null)
            {
                var local = FindLocalPlugin(SelectedOnlinePlugin);
                if (local != null) { DeleteOne(local); RefreshLocalPlugins(); }
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
            // Sorting is now handled by ApplyOnlinePluginFilter via step_891
            ApplyOnlinePluginFilter();
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

            IEnumerable<LocalPluginInfo> sorted = LocalPluginSortIndex switch
            {
                0 => LocalPluginSortAscending ? filtered.OrderBy(p => p.DisplayNameSortKey) : filtered.OrderByDescending(p => p.DisplayNameSortKey),
                1 => LocalPluginSortAscending ? filtered.OrderBy(p => p.IsEnabled).ThenBy(p => p.DisplayNameSortKey) : filtered.OrderByDescending(p => p.IsEnabled).ThenBy(p => p.DisplayNameSortKey),
                2 => LocalPluginSortAscending ? filtered.OrderBy(p => p.Author) : filtered.OrderByDescending(p => p.Author),
                3 => LocalPluginSortAscending ? filtered.OrderBy(p => p.Type) : filtered.OrderByDescending(p => p.Type),
                4 => LocalPluginSortAscending ? filtered.OrderBy(p => p.Version) : filtered.OrderByDescending(p => p.Version),
                5 => LocalPluginSortAscending ? filtered.OrderBy(p => p.PublishedAt) : filtered.OrderByDescending(p => p.PublishedAt),
                6 => LocalPluginSortAscending ? filtered.OrderBy(p => p.DownloadedAt) : filtered.OrderByDescending(p => p.DownloadedAt),
                _ => LocalPluginSortAscending ? filtered.OrderBy(p => p.DisplayNameSortKey) : filtered.OrderByDescending(p => p.DisplayNameSortKey),
            };

            LocalPlugins.Clear();
            foreach (var p in sorted) LocalPlugins.Add(p);
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
        private void InstanceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null || !SelectedInstance.IsRealInstance) return;
            var dlg = new InstanceSettingsDialog(SelectedInstance) { Owner = this };
            dlg.ShowDialog();
            SaveAll();
        }

        /// <summary>引き継ぎ対象ファイルをコピーする（新規DL時・再引き継ぎ共通）</summary>
        private void CopyInheritedSettings(System.Collections.Generic.List<string> files, InstanceInfo target)
        {
            if (SelectedInstance == null || string.IsNullOrEmpty(SelectedInstance.ExePath)) return;
            try
            {
                // コピー元：現在選択インスタンスの最新バージョンフォルダ
                string srcSettings = Path.Combine(SelectedInstance.RootDirectory, "user", "setting");
                string srcBase = srcSettings;
                if (Directory.Exists(srcSettings))
                {
                    var vDirs = new DirectoryInfo(srcSettings).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime).ToArray();
                    if (vDirs.Length > 0) srcBase = vDirs[0].FullName;
                }

                // コピー先：新インスタンスの最新バージョンフォルダ
                string dstSettings = Path.Combine(target.RootDirectory, "user", "setting");
                string dstBase = dstSettings;
                if (Directory.Exists(dstSettings))
                {
                    var vDirs = new DirectoryInfo(dstSettings).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime).ToArray();
                    if (vDirs.Length > 0) dstBase = vDirs[0].FullName;
                }
                if (!Directory.Exists(dstBase)) Directory.CreateDirectory(dstBase);

                foreach (var fn in files)
                {
                    string src = Path.Combine(srcBase, fn);
                    string dst = Path.Combine(dstBase, fn);
                    if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Translate.InheritFailed}\n{ex.Message}");
            }
        }
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

        // ==========================================
        // プラグインポータル フィルタ・ソート・ページネーション (完全版)
        // ==========================================
        private void SelectAllPortalCards_Click(object sender, RoutedEventArgs e)
        {
            foreach (var plugin in OnlinePlugins) plugin.IsSelected = true;
            UpdatePortalSelectionBar();
        }

        private void PluginTypeFilter_Click(object sender, RoutedEventArgs e)
        {
            ApplyOnlinePluginFilter();
        }

        private void UpdateCategoryFilterCounts()
        {
            if (_allOnlinePlugins == null) return;
            foreach (var filter in PluginTypeFilters)
            {
                filter.Count = _allOnlinePlugins.Count(p =>
                    string.Equals(p.DisplayType, filter.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Type, filter.InternalName, StringComparison.OrdinalIgnoreCase));
            }

            // 配布サイト動的フィルターの集計・更新
            var siteGroups = _allOnlinePlugins
                .GroupBy(p => string.IsNullOrWhiteSpace(p.SiteTag) ? "その他" : p.SiteTag)
                .OrderByDescending(g => g.Count())
                .ToList();

            var currentStates = PluginSiteFilters.ToDictionary(f => f.SiteName, f => f.IsSelected, StringComparer.OrdinalIgnoreCase);

            PluginSiteFilters.Clear();
            foreach (var g in siteGroups)
            {
                bool isSelected = currentStates.TryGetValue(g.Key, out bool sel) ? sel : true;
                PluginSiteFilters.Add(new PluginSiteFilterItem
                {
                    SiteName = g.Key,
                    Count = g.Count(),
                    IsSelected = isSelected
                });
            }

            OnPropertyChanged(nameof(PortalSiteGitHubText));
            OnPropertyChanged(nameof(PortalSiteBoothText));
            OnPropertyChanged(nameof(PortalSiteInfoText));
            OnPropertyChanged(nameof(PortalSiteOtherText));
            PortalFilteredVsTotalText = $"{OnlinePlugins.Count} / {_allOnlinePlugins.Count} 件";
        }

        private void ApplyOnlinePluginFilter()
        {
            if (OnlinePlugins == null || _allOnlinePlugins == null) return;

            var selectedTypes = PluginTypeFilters
                .Where(f => f.IsSelected)
                .Select(f => f.InternalName)
                .ToList();

            var selectedSites = PluginSiteFilters.Count > 0
                ? PluginSiteFilters.Where(f => f.IsSelected).Select(f => f.SiteName).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;

            var query = OnlinePluginSearchText?.Trim().ToLower() ?? "";

            var filtered = _allOnlinePlugins.Where(p =>
            {
                // 検索フィルタ（名前、作者、説明、バージョン、タグ）
                if (!string.IsNullOrWhiteSpace(query))
                {
                    bool matchesSearch =
                        (p.Name?.ToLower().Contains(query) ?? false) ||
                        (p.Author?.ToLower().Contains(query) ?? false) ||
                        (p.Description?.ToLower().Contains(query) ?? false) ||
                        (p.LatestVersionName?.ToLower().Contains(query) ?? false) ||
                        (p.LocalVersion?.ToLower().Contains(query) ?? false) ||
                        (p.Tags != null && p.Tags.Any(t => t.ToLower().Contains(query)));

                    if (!matchesSearch) return false;
                }

                // タイプフィルタ
                string typeName = string.IsNullOrEmpty(p.Type) ? "その他" : p.Type;
                if (selectedTypes.Count > 0 && !selectedTypes.Contains(typeName)) return false;

                // 状況フィルター (0:すべて, 1:インストール済み, 2:未インストール, 3:更新あり)
                if (PortalInstallFilterIndex == 1 && !p.IsInstalled) return false;
                if (PortalInstallFilterIndex == 2 && p.IsInstalled) return false;
                if (PortalInstallFilterIndex == 3 && !p.HasUpdate) return false;

                // 配布ステータス (0:すべて, 1:配布中, 2:配布終了)
                if (PortalStatusIndex == 1 && !p.IsEnabled) return false;
                if (PortalStatusIndex == 2 && p.IsEnabled) return false;

                // 配布サイト動的フィルター
                if (selectedSites != null)
                {
                    string tag = string.IsNullOrWhiteSpace(p.SiteTag) ? "その他" : p.SiteTag;
                    if (!selectedSites.Contains(tag)) return false;
                }

                // GitHub/Booth未掲載切替 (IsYmlItem)
                if (p.IsGitHub)
                {
                    if (PortalGitHubExtraIndex == 0 && !p.IsYmlItem) return false; // 非表示
                    if (PortalGitHubExtraIndex == 2 && p.IsYmlItem) return false;  // のみ表示
                }
                if (p.IsBooth)
                {
                    if (PortalBoothExtraIndex == 0 && !p.IsYmlItem) return false;
                    if (PortalBoothExtraIndex == 2 && p.IsYmlItem) return false;
                }

                return true;
            });

            // 並び替え (0:公開日, 1:更新日, 2:名前, 3:作者, 4:カテゴリ, 5:価格, 6:配布元)
            IEnumerable<PluginCatalogItem> sorted = PortalSortFieldIndex switch
            {
                0 => PortalSortAscending ? filtered.OrderBy(p => p.FirstPublishedAt) : filtered.OrderByDescending(p => p.FirstPublishedAt),
                1 => PortalSortAscending ? filtered.OrderBy(p => p.LatestPublishedAt) : filtered.OrderByDescending(p => p.LatestPublishedAt),
                2 => PortalSortAscending ? filtered.OrderBy(p => p.Name) : filtered.OrderByDescending(p => p.Name),
                3 => PortalSortAscending ? filtered.OrderBy(p => p.Author) : filtered.OrderByDescending(p => p.Author),
                4 => PortalSortAscending ? filtered.OrderBy(p => p.DisplayType) : filtered.OrderByDescending(p => p.DisplayType),
                5 => PortalSortAscending ? filtered.OrderBy(p => p.Price) : filtered.OrderByDescending(p => p.Price),
                6 => PortalSortAscending ? filtered.OrderBy(p => p.SiteTag) : filtered.OrderByDescending(p => p.SiteTag),
                _ => PortalSortAscending ? filtered.OrderBy(p => p.FirstPublishedAt) : filtered.OrderByDescending(p => p.FirstPublishedAt),
            };

            var list = sorted.ToList();
            PortalTotalCount = list.Count;
            PortalFilteredVsTotalText = $"{PortalTotalCount} / {_allOnlinePlugins.Count} 件";

            // 1ページあたりの件数
            int pageSize = PortalPageSizeIndex switch
            {
                0 => 5,
                1 => 10,
                2 => 20,
                3 => 50,
                4 => 100,
                _ => int.MaxValue
            };

            PortalTotalPages = Math.Max(1, (int)Math.Ceiling((double)PortalTotalCount / pageSize));
            if (PortalCurrentPage > PortalTotalPages) PortalCurrentPage = PortalTotalPages;
            if (PortalCurrentPage < 1) PortalCurrentPage = 1;

            var pageItems = list.Skip((PortalCurrentPage - 1) * pageSize).Take(pageSize).ToList();

            PortalDisplayStart = PortalTotalCount > 0 ? (PortalCurrentPage - 1) * pageSize + 1 : 0;
            PortalDisplayEnd = PortalTotalCount > 0 ? Math.Min(PortalCurrentPage * pageSize, PortalTotalCount) : 0;

            OnlinePlugins.Clear();
            foreach (var p in pageItems) OnlinePlugins.Add(p);
        }

        // ==========================================
        // プラグインポータル プロパティ＆イベントハンドラ
        // ==========================================
        private int _portalStatusIndex = 0; // 0:すべて, 1:配布中, 2:配布終了
        public int PortalStatusIndex
        {
            get => _portalStatusIndex;
            set { _portalStatusIndex = value; OnPropertyChanged(nameof(PortalStatusIndex)); ApplyOnlinePluginFilter(); }
        }

        private bool _portalSiteGitHub = true;
        public bool PortalSiteGitHub { get => _portalSiteGitHub; set { _portalSiteGitHub = value; OnPropertyChanged(nameof(PortalSiteGitHub)); } }
        private bool _portalSiteBooth = true;
        public bool PortalSiteBooth { get => _portalSiteBooth; set { _portalSiteBooth = value; OnPropertyChanged(nameof(PortalSiteBooth)); } }
        private bool _portalSiteInfo = true;
        public bool PortalSiteInfo { get => _portalSiteInfo; set { _portalSiteInfo = value; OnPropertyChanged(nameof(PortalSiteInfo)); } }
        private bool _portalSiteOther = true;
        public bool PortalSiteOther { get => _portalSiteOther; set { _portalSiteOther = value; OnPropertyChanged(nameof(PortalSiteOther)); } }

        public string PortalSiteGitHubText => $"GitHub ({_allOnlinePlugins?.Count(p => p.IsGitHub) ?? 0})";
        public string PortalSiteBoothText => $"Booth ({_allOnlinePlugins?.Count(p => p.IsBooth) ?? 0})";
        public string PortalSiteInfoText => $"情報サイト ({_allOnlinePlugins?.Count(p => p.SiteTag == "情報サイト") ?? 0})";
        public string PortalSiteOtherText => $"その他 ({_allOnlinePlugins?.Count(p => !p.IsGitHub && !p.IsBooth && p.SiteTag != "情報サイト") ?? 0})";

        private string _ymm4LatestVersionText = "YMM4最新: 取得中...";
        public string Ymm4LatestVersionText
        {
            get => _ymm4LatestVersionText;
            set { _ymm4LatestVersionText = value; OnPropertyChanged(nameof(Ymm4LatestVersionText)); }
        }

        private string _portalFilteredVsTotalText = "0 / 0 件";
        public string PortalFilteredVsTotalText
        {
            get => _portalFilteredVsTotalText;
            set { _portalFilteredVsTotalText = value; OnPropertyChanged(nameof(PortalFilteredVsTotalText)); }
        }

        private int _portalGitHubExtraIndex = 0; // 0:非表示, 1:表示, 2:のみ表示
        public int PortalGitHubExtraIndex { get => _portalGitHubExtraIndex; set { _portalGitHubExtraIndex = value; OnPropertyChanged(nameof(PortalGitHubExtraIndex)); ApplyOnlinePluginFilter(); } }
        private int _portalBoothExtraIndex = 0;
        public int PortalBoothExtraIndex { get => _portalBoothExtraIndex; set { _portalBoothExtraIndex = value; OnPropertyChanged(nameof(PortalBoothExtraIndex)); ApplyOnlinePluginFilter(); } }

        private int _portalSortFieldIndex = 0; // 0:公開日, 1:更新日, 2:名前, 3:作者, 4:カテゴリ, 5:価格, 6:配布元
        public int PortalSortFieldIndex { get => _portalSortFieldIndex; set { _portalSortFieldIndex = value; OnPropertyChanged(nameof(PortalSortFieldIndex)); ApplyOnlinePluginFilter(); } }
        private bool _portalSortAscending = true;
        public bool PortalSortAscending { get => _portalSortAscending; set { _portalSortAscending = value; OnPropertyChanged(nameof(PortalSortAscending)); ApplyOnlinePluginFilter(); } }

        private int _portalPageSizeIndex = 2; // 0:5, 1:10, 2:20, 3:50, 4:100, 5:全表示
        public int PortalPageSizeIndex { get => _portalPageSizeIndex; set { _portalPageSizeIndex = value; OnPropertyChanged(nameof(PortalPageSizeIndex)); PortalCurrentPage = 1; ApplyOnlinePluginFilter(); } }

        private int _portalCurrentPage = 1;
        public int PortalCurrentPage { get => _portalCurrentPage; set { _portalCurrentPage = value; OnPropertyChanged(nameof(PortalCurrentPage)); ApplyOnlinePluginFilter(); } }
        private int _portalTotalPages = 1;
        public int PortalTotalPages { get => _portalTotalPages; set { _portalTotalPages = value; OnPropertyChanged(nameof(PortalTotalPages)); } }
        private int _portalTotalCount = 0;
        public int PortalTotalCount { get => _portalTotalCount; set { _portalTotalCount = value; OnPropertyChanged(nameof(PortalTotalCount)); } }
        private int _portalDisplayStart = 0;
        public int PortalDisplayStart { get => _portalDisplayStart; set { _portalDisplayStart = value; OnPropertyChanged(nameof(PortalDisplayStart)); } }
        private int _portalDisplayEnd = 0;
        public int PortalDisplayEnd { get => _portalDisplayEnd; set { _portalDisplayEnd = value; OnPropertyChanged(nameof(PortalDisplayEnd)); } }

        private int _portalInstallFilterIndex = 0; // 0:すべて, 1:インストール済み, 2:未インストール, 3:更新あり
        public int PortalInstallFilterIndex { get => _portalInstallFilterIndex; set { _portalInstallFilterIndex = value; OnPropertyChanged(nameof(PortalInstallFilterIndex)); ApplyOnlinePluginFilter(); } }

        private double _portalCardWidth = 260;
        public double PortalCardWidth { get => _portalCardWidth; set { _portalCardWidth = value; OnPropertyChanged(nameof(PortalCardWidth)); } }

        private bool _portalSelectionBarVisible = false;
        public bool PortalSelectionBarVisible { get => _portalSelectionBarVisible; set { _portalSelectionBarVisible = value; OnPropertyChanged(nameof(PortalSelectionBarVisible)); } }
        private int _portalSelectedCount = 0;
        public int PortalSelectedCount { get => _portalSelectedCount; set { _portalSelectedCount = value; OnPropertyChanged(nameof(PortalSelectedCount)); } }
        private int _portalGitHubCount = 0;
        public int PortalGitHubCount { get => _portalGitHubCount; set { _portalGitHubCount = value; OnPropertyChanged(nameof(PortalGitHubCount)); } }
        private int _portalExternalCount = 0;
        public int PortalExternalCount { get => _portalExternalCount; set { _portalExternalCount = value; OnPropertyChanged(nameof(PortalExternalCount)); } }

        // ローカルプラグインソートプロパティ
        private int _localPluginSortIndex = 0;
        public int LocalPluginSortIndex
        {
            get => _localPluginSortIndex;
            set
            {
                _localPluginSortIndex = value;
                OnPropertyChanged(nameof(LocalPluginSortIndex));
                _lastPluginSortField = value switch
                {
                    1 => "IsEnabled",
                    2 => "Author",
                    3 => "DisplayType",
                    4 => "Version",
                    5 => "DisplayPublishedAt",
                    6 => "DisplayDownloadedAt",
                    _ => "DisplayName"
                };
                ApplyLocalPluginFilter();
            }
        }

        private bool _localPluginSortAscending = true;
        public bool LocalPluginSortAscending
        {
            get => _localPluginSortAscending;
            set
            {
                _localPluginSortAscending = value;
                _lastPluginSortDirection = value ? ListSortDirection.Ascending : ListSortDirection.Descending;
                OnPropertyChanged(nameof(LocalPluginSortAscending));
                ApplyLocalPluginFilter();
            }
        }

        private void LocalPluginSort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb)
                LocalPluginSortIndex = cb.SelectedIndex;
        }

        private void LocalPluginSortOrder_Click(object sender, RoutedEventArgs e)
        {
            LocalPluginSortAscending = !LocalPluginSortAscending;
        }

        private void LocalPluginHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
            {
                string? headerText = header.Column.Header?.ToString();
                if (headerText != null)
                {
                    if (headerText == Translate.PluginName || headerText == "プラグイン名") LocalPluginSortIndex = 0;
                    else if (headerText == Translate.StatusColumn || headerText == "状態") LocalPluginSortIndex = 1;
                    else if (headerText == Translate.AuthorColumn || headerText == "作者") LocalPluginSortIndex = 2;
                    else if (headerText == Translate.TypeColumn || headerText == "タイプ") LocalPluginSortIndex = 3;
                    else if (headerText == Translate.VersionColumn || headerText == "バージョン") LocalPluginSortIndex = 4;
                    else if (headerText == Translate.PublishedAt || headerText == "公開日時") LocalPluginSortIndex = 5;
                    else if (headerText == Translate.DownloadedAt || headerText == "DL日時") LocalPluginSortIndex = 6;
                    else LocalPluginSortAscending = !LocalPluginSortAscending;
                }
            }
        }

        private void SelectAllTypeFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in PluginTypeFilters) t.IsSelected = true;
            ApplyOnlinePluginFilter();
        }

        private void ClearAllTypeFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in PluginTypeFilters) t.IsSelected = false;
            ApplyOnlinePluginFilter();
        }

        private void SelectAllSiteFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in PluginSiteFilters) s.IsSelected = true;
            PortalSiteGitHub = true;
            PortalSiteBooth = true;
            PortalSiteInfo = true;
            PortalSiteOther = true;
            ApplyOnlinePluginFilter();
        }

        private void ClearAllSiteFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in PluginSiteFilters) s.IsSelected = false;
            PortalSiteGitHub = false;
            PortalSiteBooth = false;
            PortalSiteInfo = false;
            PortalSiteOther = false;
            ApplyOnlinePluginFilter();
        }

        private void PortalClearSearch_Click(object sender, RoutedEventArgs e)
        {
            OnlinePluginSearchText = string.Empty;
        }

        private void PortalStatusFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb)
                PortalStatusIndex = cb.SelectedIndex;
        }

        private void ResetPortalFilters_Click(object sender, RoutedEventArgs e)
        {
            OnlinePluginSearchText = string.Empty;
            foreach (var t in PluginTypeFilters) t.IsSelected = true;
            foreach (var s in PluginSiteFilters) s.IsSelected = true;
            PortalSiteGitHub = true;
            PortalSiteBooth = true;
            PortalSiteInfo = true;
            PortalSiteOther = true;
            PortalStatusIndex = 0;
            PortalInstallFilterIndex = 0;
            PortalGitHubExtraIndex = 0;
            PortalBoothExtraIndex = 0;
            PortalSortFieldIndex = 0;
            PortalSortAscending = true;
            PortalPageSizeIndex = 2; // 20
            PortalCurrentPage = 1;
            ApplyOnlinePluginFilter();
        }

        // ==========================================
        // 設定タブ 除外ディレクトリ一覧
        // ==========================================
        public ObservableCollection<string> ExcludeDirectories { get; set; } = new ObservableCollection<string>();

        private void AddExcludeDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() == true)
            {
                if (!ExcludeDirectories.Contains(dlg.FolderName))
                {
                    ExcludeDirectories.Add(dlg.FolderName);
                    _currentSettings.ExcludeDirectories = ExcludeDirectories.ToList();
                    _settingsManager.Save(_currentSettings);
                    RefreshRecentProjects();
                }
            }
        }

        private void RemoveExcludeDir_Click(object sender, RoutedEventArgs e)
        {
            if (ExcludeDirListBox.SelectedItem is string selected)
            {
                ExcludeDirectories.Remove(selected);
                _currentSettings.ExcludeDirectories = ExcludeDirectories.ToList();
                _settingsManager.Save(_currentSettings);
                RefreshRecentProjects();
            }
        }

        // ==========================================
        // プラグインポータル ハンドラ & UI 連携
        // ==========================================
        public ObservableCollection<PluginSiteFilterItem> PluginSiteFilters { get; set; } = new ObservableCollection<PluginSiteFilterItem>();

        private void PortalSearch_Click(object sender, RoutedEventArgs e) => ApplyOnlinePluginFilter();
        private void PortalRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadOnlinePlugins();

        private void PluginSiteFilter_Click(object sender, RoutedEventArgs e) => ApplyOnlinePluginFilter();
        private void PortalInstallFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyOnlinePluginFilter();
        private void PortalExtraFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyOnlinePluginFilter();
        private void PortalSort_Changed(object sender, SelectionChangedEventArgs e) => ApplyOnlinePluginFilter();
        private void PortalSortOrder_Click(object sender, RoutedEventArgs e) => PortalSortAscending = !PortalSortAscending;

        private void PortalPageSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            PortalCurrentPage = 1;
            ApplyOnlinePluginFilter();
        }

        private void PortalScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0)
            {
                double availableWidth = e.NewSize.Width - 32;
                int columns = Math.Max(1, (int)(availableWidth / 330));
                PortalCardWidth = Math.Max(280, (availableWidth / columns) - 16);
            }
        }

        private void PortalCardHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PluginCatalogItem plugin)
            {
                var dialog = new PluginDetailDialog(plugin, this) { Owner = this };
                dialog.ShowDialog();
            }
        }

        private void PortalCardCheck_Click(object sender, RoutedEventArgs e) => UpdatePortalSelectionBar();

        private void PortalCardPrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PluginCatalogItem item)
            {
                if (item.IsGitHub)
                {
                    var dlg = new VersionSelectDialog(item, this) { Owner = this };
                    dlg.ShowDialog();
                }
                else if (!string.IsNullOrEmpty(item.BestSiteUrl))
                {
                    try { Process.Start(new ProcessStartInfo(item.BestSiteUrl) { UseShellExecute = true }); } catch { }
                }
                else if (!string.IsNullOrEmpty(item.Url))
                {
                    try { Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true }); } catch { }
                }
            }
        }

        private async void PortalCardUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PluginCatalogItem plugin)
            {
                if (plugin.IsDirectDownloadSupported)
                {
                    await ExecuteDirectDownloadAsync(plugin);
                }
            }
        }

        private void PortalCardInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PluginCatalogItem plugin)
            {
                var dialog = new PluginDetailDialog(plugin, this) { Owner = this };
                dialog.ShowDialog();
            }
        }

        private void PortalFirstPage_Click(object sender, RoutedEventArgs e) => PortalCurrentPage = 1;
        private void PortalPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (PortalCurrentPage > 1) PortalCurrentPage--;
        }

        private void PageNumberBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && int.TryParse(tb.Text, out int p))
            {
                PortalCurrentPage = Math.Clamp(p, 1, Math.Max(1, PortalTotalPages));
            }
        }

        private void PortalNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (PortalCurrentPage < PortalTotalPages) PortalCurrentPage++;
        }

        private void PortalLastPage_Click(object sender, RoutedEventArgs e) => PortalCurrentPage = Math.Max(1, PortalTotalPages);

        private void PortalDeselectCurrentPage_Click(object sender, RoutedEventArgs e)
        {
            foreach (var plugin in OnlinePlugins) plugin.IsSelected = false;
            UpdatePortalSelectionBar();
        }

        private void PortalClearSelection_Click(object sender, RoutedEventArgs e)
        {
            if (_allOnlinePlugins != null)
            {
                foreach (var plugin in _allOnlinePlugins) plugin.IsSelected = false;
            }
            foreach (var plugin in OnlinePlugins) plugin.IsSelected = false;
            UpdatePortalSelectionBar();
        }

        public void UpdatePortalSelectionBar()
        {
            var selected = (_allOnlinePlugins ?? OnlinePlugins.ToList()).Where(p => p.IsSelected).ToList();
            PortalSelectedCount = selected.Count;
            PortalSelectionBarVisible = PortalSelectedCount > 0;
            PortalGitHubCount = selected.Count(p => p.IsDirectDownloadSupported);
            PortalExternalCount = selected.Count(p => !p.IsDirectDownloadSupported);
        }

        public async Task ExecuteDirectDownloadAsync(PluginCatalogItem plugin)
        {
            if (SelectedInstance == null || string.IsNullOrEmpty(SelectedInstance.RootDirectory))
            {
                MessageBox.Show(Translate.InstanceNotSelected, Translate.Confirm, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (plugin.SelectedVersion == null)
            {
                if (plugin.Releases.Count == 0)
                {
                    await LoadReleaseDetails(plugin);
                }
                plugin.SelectedVersion = plugin.Releases.FirstOrDefault();
            }

            if (plugin.SelectedVersion == null)
            {
                MessageBox.Show(Translate.ErrorNoDownloadVersion, Translate.Confirm, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var progressWindow = new DownloadProgressWindow { Owner = this };
            progressWindow.Show();

            try
            {
                await ExecuteDownload(plugin, SelectedInstance, progressWindow, false);
                RefreshLocalPlugins();
                ApplyOnlinePluginFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Translate.DownloadError, ex.Message), Translate.Confirm, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressWindow.Close();
            }
        }

    
        private async void BulkDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = (_allOnlinePlugins ?? OnlinePlugins.ToList()).Where(p => p.IsSelected).ToList();
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
                        if (plugin.Releases.Count == 0)
                        {
                            await LoadReleaseDetails(plugin);
                        }

                        plugin.SelectedVersion = plugin.Releases.FirstOrDefault();

                        if (plugin.SelectedVersion != null)
                        {
                            await ExecuteDownload(plugin, instance, progressWin, true);
                        }
                        else if (_currentSettings.AutoOpenSiteOnBulkDownload
                                 && !string.IsNullOrEmpty(plugin.BestSiteUrl))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(plugin.BestSiteUrl) { UseShellExecute = true });
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        progressWin.AddReadme(plugin.Name, $"[{instance.Name}]\n\n{Translate.LoadError}{ex.Message}");
                    }

                    progressWin.UpdateStatus(statusMsg, ((double)currentTask / totalTasks) * 100, $"{currentTask} / {totalTasks}");
                }
            }

            RefreshLocalPlugins();
            progressWin.ShowFinalClose();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ウィンドウ表示後にテーマを再適用してWindow.Resourcesを確実に更新する
            ApplyTheme();
            ThemeHelper.ApplyTitleBarTheme(this, ThemeHelper.IsCurrentDarkTheme);
        }

    }
}
