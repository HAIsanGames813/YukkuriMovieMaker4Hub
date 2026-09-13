using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public class YmmUpdateDisplayItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ArticleUrl { get; set; } = string.Empty;
        public Version? Version { get; set; }

        public string CleanDescription
        {
            get
            {
                if (string.IsNullOrEmpty(Description)) return string.Empty;
                string clean = Regex.Replace(Description, "<.*?>", string.Empty);
                clean = clean.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
                return clean.Trim();
            }
        }
    }

    public partial class YmmUpdateDialog : Window
    {
        public YmmUpdateDialog(string instanceName, string localVersion, Version? latestVersion, List<YmmUpdateItem> updates)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);

            InstanceNameText.Text = instanceName;
            CurrentVersionText.Text = $"v{localVersion}";
            LatestVersionText.Text = latestVersion != null ? $"v{latestVersion}" : "最新版";

            var displayList = new List<YmmUpdateDisplayItem>();
            foreach (var u in updates)
            {
                string url = u.ArticleUrl;
                if (string.IsNullOrEmpty(url) && u.Version != null)
                {
                    url = $"https://manjubox.net/ymm4/release/{u.Version}/";
                }

                displayList.Add(new YmmUpdateDisplayItem
                {
                    Title = u.Title,
                    Description = u.Description,
                    ArticleUrl = url,
                    Version = u.Version
                });
            }

            UpdatesItemsControl.ItemsSource = displayList;
        }

        private void OpenReleaseUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void OpenManjubox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://manjubox.net/ymm4/") { UseShellExecute = true });
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}