using System.Collections.Generic;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    public partial class DownloadProgressWindow : Window
    {
        private List<(string Name, string Content)> _readmes = new List<(string, string)>();
        private int _currentIndex = -1;

        public DownloadProgressWindow()
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
        }

        public void UpdateStatus(string message, double progress, string count)
        {
            StatusText.Text = message;
            DownloadProgressBar.Value = progress;
            CountText.Text = count;
        }

        public void AddReadme(string pluginName, string content)
        {
            _readmes.Add((pluginName, content));
            if (_currentIndex == -1) _currentIndex = 0;
            DisplayCurrentReadme();
        }

        private void DisplayCurrentReadme()
        {
            if (_currentIndex >= 0 && _currentIndex < _readmes.Count)
            {
                var item = _readmes[_currentIndex];
                ReadmeBox.Text = $"【 {item.Name} 】\n\n{item.Content}";
            }
            ReadmePageText.Text = $"{_readmes.Count} 件中 {_currentIndex + 1} 件目";
        }

        private void PrevReadme_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0) { _currentIndex--; DisplayCurrentReadme(); }
        }

        private void NextReadme_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _readmes.Count - 1) { _currentIndex++; DisplayCurrentReadme(); }
        }

        public void ShowFinalClose()
        {
            FinalCloseButton.Visibility = Visibility.Visible;
            StatusText.Text = Translate.DownloadInProgress;
        }

        private void FinalCloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}