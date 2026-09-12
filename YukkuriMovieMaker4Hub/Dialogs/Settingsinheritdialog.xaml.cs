using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace YukkuriMovieMaker4Hub
{
    public class SettingsFileItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }
    }

    public partial class SettingsInheritDialog : Window
    {
        private readonly IReadOnlyList<InstanceInfo>? _instances;
        private string _settingsDir = string.Empty;
        private readonly List<SettingsFileItem> _items = new();

        public List<string> SelectedFiles { get; private set; } = new();
        public string SourceExePath { get; private set; } = string.Empty;

        public SettingsInheritDialog(IReadOnlyList<InstanceInfo> instances)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            _instances = instances;

            SourceInstanceComboBox.ItemsSource = _instances;
            if (_instances.Count > 0)
            {
                SourceInstanceComboBox.SelectedIndex = 0;
            }
        }

        public SettingsInheritDialog(string settingsDir)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            _settingsDir = settingsDir;
            LoadFiles();
        }

        private void SourceInstanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceInstanceComboBox.SelectedItem is InstanceInfo inst)
            {
                SourceExePath = inst.ExePath;
                _settingsDir = Path.Combine(Path.GetDirectoryName(inst.ExePath) ?? "", "user", "setting");
                LoadFiles();
            }
        }

        private void LoadFiles()
        {
            _items.Clear();
            try
            {
                string baseDir = _settingsDir;
                if (Directory.Exists(baseDir))
                {
                    var versionDirs = new DirectoryInfo(baseDir).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime).ToArray();
                    if (versionDirs.Length > 0)
                        baseDir = versionDirs[0].FullName;
                }

                if (Directory.Exists(baseDir))
                {
                    var jsonFiles = Directory.GetFiles(baseDir, "*.json")
                        .Select(Path.GetFileName)
                        .Where(f => f != null)
                        .Cast<string>()
                        .OrderBy(f => f)
                        .ToList();

                    foreach (var fn in jsonFiles)
                    {
                        string rawName = Path.GetFileNameWithoutExtension(fn);
                        string displayName = rawName;
                        if (displayName.StartsWith("YukkuriMovieMaker."))
                            displayName = displayName.Substring("YukkuriMovieMaker.".Length);

                        _items.Add(new SettingsFileItem
                        {
                            FileName = fn,
                            DisplayName = displayName,
                            IsChecked = false
                        });
                    }
                }
            }
            catch { }

            FileList.ItemsSource = null;
            FileList.ItemsSource = _items;
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles = _items.Where(i => i.IsChecked).Select(i => i.FileName).ToList();
            DialogResult = true;
        }
    }
}
