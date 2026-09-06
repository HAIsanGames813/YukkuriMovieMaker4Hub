using System;
using System.ComponentModel;

namespace YukkuriMovieMaker4Hub
{
    public class PluginTypeFilterItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string InternalName { get; set; } = string.Empty;

        public string DisplayName => PluginTypeHelper.GetDisplayName(InternalName);

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

        public string DisplayTextWithCount => Count > 0 ? $"{DisplayName} ({Count})" : DisplayName;

        private bool _isSelected;
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
