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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace YukkuriMovieMaker4Hub
{
    public class InstanceInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string Id { get; set; } = Guid.NewGuid().ToString();
        private string? _name;
        public string Name { get => _name ?? string.Empty; set { _name = value; OnPropertyChanged(nameof(Name)); } }
        private string? _iconPath;
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
        public string ExePath { get; set; } = string.Empty;
        public string RootDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.GetDirectoryName(ExePath) ?? string.Empty;
        public string PluginDirectory => string.IsNullOrEmpty(ExePath) ? string.Empty : Path.Combine(RootDirectory, "user", "plugin");
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
                if (host.Contains("manjubox.net")) return "YMM4公式サイト";
                return host;
            }
            catch { return "配布サイト"; }
        }
    }

    public class OnlinePlugin : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public List<string> Links { get; set; } = new List<string>();
        [JsonIgnore]
        public List<PluginLink> AllLinks
        {
            get
            {
                var list = new List<PluginLink>();
                if (!string.IsNullOrEmpty(Url)) try { list.Add(new PluginLink { Url = Url }); } catch { }
                if (Links != null) foreach (var l in Links) try { list.Add(new PluginLink { Url = l }); } catch { }
                return list.GroupBy(x => x.Url).Select(g => g.First()).ToList();
            }
        }
        public string RepoName { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        [JsonIgnore]
        public string? FullRepoPath => (string.IsNullOrEmpty(Owner) || string.IsNullOrEmpty(RepoName)) ? null : $"{Owner}/{RepoName}";
        [JsonIgnore]
        public string? DetailApiUrl => (string.IsNullOrEmpty(FullRepoPath)) ? null : $"https://manjubox.net/api/ymm4plugins/github/detail/{FullRepoPath}";
    }

    public class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }
    }

    public class GithubPluginDetail
    {
        [JsonPropertyName("releases")]
        public List<GithubRelease> Releases { get; set; } = new List<GithubRelease>();
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
        public List<InstanceInfo> LoadInstances()
        {
            if (!File.Exists(_path)) return new List<InstanceInfo>();
            try { return JsonSerializer.Deserialize<List<InstanceInfo>>(File.ReadAllText(_path)) ?? new List<InstanceInfo>(); }
            catch { return new List<InstanceInfo>(); }
        }
        public void SaveInstances(List<InstanceInfo> instances)
        {
            var json = JsonSerializer.Serialize(instances, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private SettingsManager _settings = new SettingsManager();
        private HttpClient _http = new HttpClient();
        public ObservableCollection<InstanceInfo> Instances { get; set; } = new ObservableCollection<InstanceInfo>();
        public ObservableCollection<LocalPluginInfo> LocalPlugins { get; set; } = new ObservableCollection<LocalPluginInfo>();
        public ObservableCollection<OnlinePlugin> OnlinePlugins { get; set; } = new ObservableCollection<OnlinePlugin>();
        private Dictionary<string, string> _versionCache = new Dictionary<string, string>();
        private InstanceInfo? _selectedInstance;
        public InstanceInfo? SelectedInstance { get => _selectedInstance; set { _selectedInstance = value; OnPropertyChanged(nameof(SelectedInstance)); RefreshLocalPlugins(); } }
        private OnlinePlugin? _selectedOnlinePlugin;
        public OnlinePlugin? SelectedOnlinePlugin { get => _selectedOnlinePlugin; set { _selectedOnlinePlugin = value; OnPropertyChanged(nameof(SelectedOnlinePlugin)); OnPropertyChanged(nameof(SelectedVersion)); } }
        public string SelectedVersion
        {
            get
            {
                if (SelectedOnlinePlugin == null) return "";
                if (string.IsNullOrEmpty(SelectedOnlinePlugin.FullRepoPath)) return "GitHub情報なし";
                return _versionCache.TryGetValue(SelectedOnlinePlugin.FullRepoPath, out var v) ? v : "取得中...";
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YukkuriMovieMaker4Hub/1.0");
            this.DataContext = this;
            foreach (var i in _settings.LoadInstances())
            {
                i.PropertyChanged += (s, e) => _settings.SaveInstances(Instances.ToList());
                Instances.Add(i);
            }
        }

        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "YukkuriMovieMaker.exe|YukkuriMovieMaker.exe" };
            if (dialog.ShowDialog() == true)
            {
                var newInstance = new InstanceInfo { Name = "新規インスタンス", ExePath = dialog.FileName };
                newInstance.PropertyChanged += (s, ee) => _settings.SaveInstances(Instances.ToList());
                Instances.Add(newInstance);
                _settings.SaveInstances(Instances.ToList());
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInstance == null) return;
            if (MessageBox.Show($"{SelectedInstance.Name} をHubから削除しますか？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Instances.Remove(SelectedInstance);
                _settings.SaveInstances(Instances.ToList());
                SelectedInstance = null;
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedItem is TabItem ti && ti.Header != null && ti.Header.ToString() == "プラグインポータル")
            {
                await LoadOnlinePlugins();
            }
        }

        private async Task LoadOnlinePlugins()
        {
            if (OnlinePlugins.Count > 0) return;
            try
            {
                var ymlContent = await _http.GetStringAsync("https://manjubox.net/ymm4plugins.yml");
                var plugins = ParseYmm4PluginsYml(ymlContent);
                OnlinePlugins.Clear();
                foreach (var p in plugins)
                {
                    var githubUrls = new List<string>();
                    if (!string.IsNullOrEmpty(p.Url)) githubUrls.Add(p.Url);
                    if (p.Links != null) githubUrls.AddRange(p.Links);

                    foreach (var url in githubUrls)
                    {
                        var match = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)");
                        if (match.Success)
                        {
                            p.Owner = match.Groups[1].Value.Trim();
                            p.RepoName = match.Groups[2].Value.Replace(".git", "").TrimEnd('/').Trim();
                            break;
                        }
                    }
                    OnlinePlugins.Add(p);
                }
                _ = PrefetchVersionsAsync();
            }
            catch (Exception ex) { MessageBox.Show("ポータル読み込み失敗: " + ex.Message); }
        }

        private async Task PrefetchVersionsAsync()
        {
            var targets = OnlinePlugins.Where(p => !string.IsNullOrEmpty(p.FullRepoPath)).ToList();
            foreach (var p in targets)
            {
                string? repoPath = p.FullRepoPath;
                if (repoPath == null) continue;
                if (_versionCache.ContainsKey(repoPath)) continue;
                try
                {
                    var response = await _http.GetStringAsync(p.DetailApiUrl);
                    var detail = JsonSerializer.Deserialize<GithubPluginDetail>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (detail?.Releases != null && detail.Releases.Count > 0)
                    {
                        var latest = detail.Releases.OrderByDescending(r => r.PublishedAt).FirstOrDefault();
                        if (latest != null && !string.IsNullOrEmpty(latest.TagName))
                        {
                            _versionCache[repoPath] = latest.TagName;
                        }
                        else
                        {
                            _versionCache[repoPath] = "リリースなし";
                        }
                    }
                    else
                    {
                        _versionCache[repoPath] = "データなし";
                    }
                }
                catch (Exception ex)
                {
                    _versionCache[repoPath] = "取得エラー";
                    Debug.WriteLine($"Error fetching version for {repoPath}: {ex.Message}");
                }

                if (SelectedOnlinePlugin == p) OnPropertyChanged(nameof(SelectedVersion));
                await Task.Delay(200);
            }
        }

        private List<OnlinePlugin> ParseYmm4PluginsYml(string content)
        {
            var list = new List<OnlinePlugin>();
            var blocks = content.Split(new[] { "\n- " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var p = new OnlinePlugin();
                var lines = block.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (trimmed.StartsWith("name:")) p.Name = trimmed.Substring(5).Trim().Trim('\'', '\"');
                    else if (trimmed.StartsWith("author:")) p.Author = trimmed.Substring(7).Trim().Trim('\'', '\"');
                    else if (trimmed.StartsWith("description:")) p.Description = trimmed.Substring(12).Trim().Trim('\'', '\"');
                    else if (trimmed.StartsWith("url:")) p.Url = trimmed.Substring(4).Trim().Trim('\'', '\"');
                    else if (trimmed.StartsWith("- http")) p.Links.Add(trimmed.Substring(1).Trim().Trim('\'', '\"'));
                }
                if (!string.IsNullOrEmpty(p.Name)) list.Add(p);
            }
            return list;
        }

        private void OpenUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void RefreshLocalPlugins()
        {
            LocalPlugins.Clear();
            if (SelectedInstance == null || !Directory.Exists(SelectedInstance.PluginDirectory)) return;
            foreach (var dir in Directory.GetDirectories(SelectedInstance.PluginDirectory))
            {
                var info = new LocalPluginInfo { FullPath = dir, IsDirectory = true };
                info.DisplayName = GetPluginNameFromYml(dir);
                LocalPlugins.Add(info);
            }
            foreach (var file in Directory.GetFiles(SelectedInstance.PluginDirectory, "*.dll*"))
                LocalPlugins.Add(new LocalPluginInfo { FullPath = file, IsDirectory = false });
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
            if (!(sender is Button btn && btn.DataContext is LocalPluginInfo plugin) || !EnsureYmmClosed()) return;
            string? parent = Path.GetDirectoryName(plugin.FullPath);
            if (parent == null) return;
            string oldName = Path.GetFileName(plugin.FullPath);
            string newName = plugin.IsDirectory ? (plugin.IsEnabled ? "_" + oldName : oldName.TrimStart('_'))
                                                : (plugin.IsEnabled ? oldName + ".disabled" : oldName.Replace(".disabled", ""));
            try
            {
                string newPath = Path.Combine(parent, newName);
                if (plugin.IsDirectory) Directory.Move(plugin.FullPath, newPath); else File.Move(plugin.FullPath, newPath);
                RefreshLocalPlugins();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void LaunchYmm(string args = "")
        {
            if (SelectedInstance == null || !File.Exists(SelectedInstance.ExePath)) return;
            Process.Start(new ProcessStartInfo(SelectedInstance.ExePath, args) { WorkingDirectory = SelectedInstance.RootDirectory, UseShellExecute = true });
        }

        private void LaunchInstance_Click(object sender, RoutedEventArgs e) => LaunchYmm();
        private void LaunchLastProject_Click(object sender, RoutedEventArgs e) => LaunchYmm("OpenLatestProject");
        private void CreateNewProject_Click(object sender, RoutedEventArgs e) => LaunchYmm("CreateNewProject");
        private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (SelectedInstance != null) Process.Start("explorer.exe", SelectedInstance.RootDirectory); }
        private void ChangeIcon_Click(object sender, RoutedEventArgs e) { if (SelectedInstance == null) return; var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.ico" }; if (d.ShowDialog() == true) SelectedInstance.IconPath = d.FileName; }
    }
}