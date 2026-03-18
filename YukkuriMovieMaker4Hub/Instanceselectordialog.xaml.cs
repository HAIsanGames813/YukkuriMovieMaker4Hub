using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YukkuriMovieMaker4Hub
{
    public partial class InstanceSelectorDialog : Window
    {
        public InstanceInfo? SelectedInstance { get; private set; }

        public InstanceSelectorDialog(IReadOnlyList<InstanceInfo> instances)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            InstanceListBox.ItemsSource = instances;
            if (instances.Count > 0)
                InstanceListBox.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (InstanceListBox.SelectedItem is InstanceInfo info)
            {
                SelectedInstance = info;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(Translate.SelectSourceInstance, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ListBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (InstanceListBox.SelectedItem is InstanceInfo)
                Ok_Click(sender, e);
        }
    }
}