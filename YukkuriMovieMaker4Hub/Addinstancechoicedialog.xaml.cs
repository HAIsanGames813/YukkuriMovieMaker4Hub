using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public enum AddInstanceMode { NewDownload, AddExisting, Cancelled }

    public partial class AddInstanceChoiceDialog : Window
    {
        public AddInstanceMode Mode { get; private set; } = AddInstanceMode.Cancelled;

        public AddInstanceChoiceDialog()
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
        }

        private void NewDownload_Click(object sender, RoutedEventArgs e)
        {
            Mode = AddInstanceMode.NewDownload;
            DialogResult = true;
        }

        private void AddExisting_Click(object sender, RoutedEventArgs e)
        {
            Mode = AddInstanceMode.AddExisting;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mode = AddInstanceMode.Cancelled;
            DialogResult = false;
        }
    }
}