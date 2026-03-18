using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace YukkuriMovieMaker4Hub
{
    public partial class WpfColorPickerDialog : Window
    {
        private bool _updating = true;   // InitializeComponent 中のイベント発火を防ぐ
        public Color SelectedColor { get; private set; }

        public WpfColorPickerDialog(Color initial)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            SelectedColor = initial;
            SliderR.Value = initial.R;
            SliderG.Value = initial.G;
            SliderB.Value = initial.B;
            _updating = false;
            UpdateAll();
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating || BoxR == null || BoxG == null || BoxB == null
                || SliderR == null || SliderG == null || SliderB == null) return;
            _updating = true;
            BoxR.Text = ((int)SliderR.Value).ToString();
            BoxG.Text = ((int)SliderG.Value).ToString();
            BoxB.Text = ((int)SliderB.Value).ToString();
            _updating = false;
            UpdatePreview();
        }

        private void Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            _updating = true;
            SliderR.Value = Clamp(BoxR.Text);
            SliderG.Value = Clamp(BoxG.Text);
            SliderB.Value = Clamp(BoxB.Text);
            BoxR.Text = ((int)SliderR.Value).ToString();
            BoxG.Text = ((int)SliderG.Value).ToString();
            BoxB.Text = ((int)SliderB.Value).ToString();
            _updating = false;
            UpdatePreview();
        }

        private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();
        private void HexBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyHex(); }

        private void ApplyHex()
        {
            try
            {
                var hex = HexBox.Text.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    _updating = true;
                    SliderR.Value = r; BoxR.Text = r.ToString();
                    SliderG.Value = g; BoxG.Text = g.ToString();
                    SliderB.Value = b; BoxB.Text = b.ToString();
                    _updating = false;
                    UpdatePreview();
                }
            }
            catch { }
        }

        private void UpdatePreview()
        {
            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;
            SelectedColor = Color.FromRgb(r, g, b);
            PreviewBorder.Background = new SolidColorBrush(SelectedColor);
            if (!_updating)
                HexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
        }

        private void UpdateAll()
        {
            _updating = true;
            BoxR.Text = ((int)SliderR.Value).ToString();
            BoxG.Text = ((int)SliderG.Value).ToString();
            BoxB.Text = ((int)SliderB.Value).ToString();
            _updating = false;
            UpdatePreview();
            HexBox.Text = $"#{(byte)SliderR.Value:X2}{(byte)SliderG.Value:X2}{(byte)SliderB.Value:X2}";
        }

        private static double Clamp(string s) =>
            double.TryParse(s, out var v) ? Math.Clamp(v, 0, 255) : 0;

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}