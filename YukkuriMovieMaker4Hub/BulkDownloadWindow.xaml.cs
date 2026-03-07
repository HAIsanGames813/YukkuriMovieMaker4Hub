using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public partial class BulkDownloadWindow : Window
    {
        public List<PluginCatalogItem> TargetPlugins { get; set; }
        public List<InstanceInfo> Instances { get; set; }
        public List<InstanceInfo> SelectedInstances => Instances.Where(i => i.IsSelected).ToList();
        public List<PluginCatalogItem> SelectedPlugins => TargetPlugins.Where(p => p.IsSelected).ToList();

        public BulkDownloadWindow(List<PluginCatalogItem> targetPlugins, List<InstanceInfo> instances)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);

            TargetPlugins = targetPlugins;
            Instances = instances.Select(i => new InstanceInfo
            {
                Id = i.Id,
                Name = i.Name,
                ExePath = i.ExePath,
                IsSelected = false
            }).ToList();

            this.DataContext = this;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedPlugins.Any())
            {
                MessageBox.Show(Translate.BulkDownloadInstance);
                return;
            }
            if (!SelectedInstances.Any())
            {
                MessageBox.Show(Translate.BulkDownloadSelectInstance);
                return;
            }
            this.DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}