using System;
using System.ComponentModel;
using System.IO;

namespace YukkuriMovieMaker4Hub
{
    public class LocalPluginInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string FullPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }

        private bool? _isEnabledOverride;
        public bool IsEnabled
        {
            get
            {
                if (_isEnabledOverride.HasValue) return _isEnabledOverride.Value;
                if (IsDirectory)
                {
                    string dirName = Path.GetFileName(FullPath);
                    if (dirName.StartsWith("_")) return false;

                    if (Directory.Exists(FullPath))
                    {
                        var enabledDlls = Directory.GetFiles(FullPath, "*.dll", SearchOption.AllDirectories);
                        return enabledDlls.Length > 0;
                    }
                    return true;
                }
                else
                {
                    return !FullPath.EndsWith(".disabled");
                }
            }
            set
            {
                _isEnabledOverride = value;
                OnPropertyChanged(nameof(IsEnabled));
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

        public string DisplayType => PluginTypeHelper.GetDisplayName(Type);

        public DateTime? PublishedAt { get; set; }
        public DateTime? DownloadedAt { get; set; }

        public string DisplayPublishedAt => PublishedAt.HasValue ? PublishedAt.Value.ToString("yyyy/MM/dd") : "-";
        public string DisplayDownloadedAt => DownloadedAt.HasValue ? DownloadedAt.Value.ToString("yyyy/MM/dd HH:mm") : "-";

        public string DisplayNameSortKey => DisplayName.TrimStart('_', '.', ' ');

        public void NotifyPathChanged()
        {
            _isEnabledOverride = null;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public class FontItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public System.Windows.Media.FontFamily Family { get; set; } = new System.Windows.Media.FontFamily("Segoe UI");
    }
}
