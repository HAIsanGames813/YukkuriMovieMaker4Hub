using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YukkuriMovieMaker4Hub
{
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
        public string? IconPath 
        { 
            get => _iconPath; 
            set 
            { 
                _iconPath = value; 
                OnPropertyChanged(nameof(IconPath)); 
                OnPropertyChanged(nameof(IconImage)); 
            } 
        }

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
                    catch { }
                }
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

        // アイコン編集パラメータ
        private double _iconScale = 1.0;
        [JsonPropertyName("iconScale")]
        public double IconScale { get => _iconScale; set { _iconScale = value; OnPropertyChanged(nameof(IconScale)); } }

        private double _iconOffsetX = 0.0;
        [JsonPropertyName("iconOffsetX")]
        public double IconOffsetX { get => _iconOffsetX; set { _iconOffsetX = value; OnPropertyChanged(nameof(IconOffsetX)); } }

        private double _iconOffsetY = 0.0;
        [JsonPropertyName("iconOffsetY")]
        public double IconOffsetY { get => _iconOffsetY; set { _iconOffsetY = value; OnPropertyChanged(nameof(IconOffsetY)); } }

        // 背景種別: "None", "Solid", "Gradient", "Image"
        private string _iconBgType = "None";
        [JsonPropertyName("iconBgType")]
        public string IconBgType { get => _iconBgType; set { _iconBgType = value; OnPropertyChanged(nameof(IconBgType)); NotifyBgChanged(); } }

        private string _iconBgColor1 = "#FF444444";
        [JsonPropertyName("iconBgColor1")]
        public string IconBgColor1 { get => _iconBgColor1; set { _iconBgColor1 = value; OnPropertyChanged(nameof(IconBgColor1)); NotifyBgChanged(); } }

        private string _iconBgColor2 = "#FF222222";
        [JsonPropertyName("iconBgColor2")]
        public string IconBgColor2 { get => _iconBgColor2; set { _iconBgColor2 = value; OnPropertyChanged(nameof(IconBgColor2)); NotifyBgChanged(); } }

        private string? _iconBgImagePath;
        [JsonPropertyName("iconBgImagePath")]
        public string? IconBgImagePath { get => _iconBgImagePath; set { _iconBgImagePath = value; OnPropertyChanged(nameof(IconBgImagePath)); NotifyBgChanged(); } }

        [JsonIgnore]
        public InstanceInfo IconBgBrush => this;

        private double _iconBgGradientAngle = 45.0;
        [JsonPropertyName("iconBgGradientAngle")]
        public double IconBgGradientAngle { get => _iconBgGradientAngle; set { _iconBgGradientAngle = value; OnPropertyChanged(nameof(IconBgGradientAngle)); NotifyBgChanged(); } }

        private void NotifyBgChanged() => OnPropertyChanged(nameof(IconBgBrush));

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
}
