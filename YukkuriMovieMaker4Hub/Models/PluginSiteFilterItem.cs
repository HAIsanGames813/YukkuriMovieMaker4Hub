using System;
using System.ComponentModel;

namespace YukkuriMovieMaker4Hub
{
    public class PluginSiteFilterItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string SiteName { get; set; } = string.Empty;

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTextWithCount)));
            }
        }

        public string DisplayTextWithCount => Count > 0 ? $"{SiteName} ({Count})" : SiteName;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
