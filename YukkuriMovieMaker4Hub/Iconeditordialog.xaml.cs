using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YukkuriMovieMaker4Hub
{
    public partial class IconEditorDialog : Window
    {
        private readonly InstanceInfo _instance;
        private const double PreviewSize = 140.0;
        private const double ScaleStep = 0.1;    // ホイール1ノッチの変化量
        private const double ScaleMin = 1.0;
        private const double ScaleMax = 10.0;

        // Apply 時に返す結果
        public string? ResultIconPath { get; private set; }
        public double ResultScale { get; private set; } = 1.0;
        public double ResultOffsetX { get; private set; }
        public double ResultOffsetY { get; private set; }
        public string ResultBgType { get; private set; } = "None";
        public string ResultBgColor1 { get; private set; } = "#FF444444";
        public string ResultBgColor2 { get; private set; } = "#FF222222";
        public double ResultBgGradientAngle { get; private set; } = 45.0;
        public string? ResultBgImagePath { get; private set; }

        private double _scale = 1.0;
        private double _offsetX;
        private double _offsetY;
        private bool _isDragging;
        private Point _dragStart;
        private bool _suppressAngle;

        private Color _color1 = Color.FromRgb(0x44, 0x44, 0x44);
        private Color _color2 = Color.FromRgb(0x22, 0x22, 0x22);
        private double _gradAngle = 45.0;
        private string? _bgImagePath;

        public IconEditorDialog(InstanceInfo instance)
        {
            InitializeComponent();
            ThemeHelper.Sync(this);
            _instance = instance;

            _scale = Math.Clamp(instance.IconScale, ScaleMin, ScaleMax);
            _offsetX = instance.IconOffsetX;
            _offsetY = instance.IconOffsetY;
            _gradAngle = instance.IconBgGradientAngle;
            _bgImagePath = instance.IconBgImagePath;

            // 角度スライダー
            _suppressAngle = true;
            AngleSlider.Value = _gradAngle;
            AngleBox.Text = ((int)_gradAngle).ToString();
            _suppressAngle = false;

            if (TryParseColor(instance.IconBgColor1, out var c1)) _color1 = c1;
            if (TryParseColor(instance.IconBgColor2, out var c2)) _color2 = c2;
            UpdateColorButtons();

            switch (instance.IconBgType)
            {
                case "Solid": BgSolid.IsChecked = true; break;
                case "Gradient": BgGradient.IsChecked = true; break;
                case "Image": BgImageMode.IsChecked = true; break;
                default: BgNone.IsChecked = true; break;
            }

            if (!string.IsNullOrEmpty(_bgImagePath))
                BgImageLabel.Text = Path.GetFileName(_bgImagePath);

            ClampOffset();
            ApplyTransform();
            LoadCurrentIcon();
            UpdateBackground();
        }

        // ────────────────────────────────────────
        // アイコン画像
        // ────────────────────────────────────────

        private void LoadCurrentIcon()
        {
            if (!string.IsNullOrEmpty(_instance.IconPath) && File.Exists(_instance.IconPath))
            { LoadImageFile(_instance.IconPath); return; }
            LoadExeIcon();
        }

        private void LoadImageFile(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
                IconSourceLabel.Text = Path.GetFileName(path);
                ResetIconButton.IsEnabled = true;
            }
            catch { LoadExeIcon(); }
        }

        private void LoadExeIcon()
        {
            if (!string.IsNullOrEmpty(_instance.ExePath) && File.Exists(_instance.ExePath))
            {
                try
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(_instance.ExePath);
                    if (icon != null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        PreviewImage.Source = bs;
                    }
                }
                catch { }
            }
            IconSourceLabel.Text = Translate.UseExeIcon;
            ResetIconButton.IsEnabled = false;
        }

        private void SelectIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Translate.SelectIconTitle,
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.ico|すべてのファイル|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            _instance.IconPath = dialog.FileName;
            LoadImageFile(dialog.FileName);
        }

        private void ResetIcon_Click(object sender, RoutedEventArgs e)
        {
            _instance.IconPath = null;
            LoadExeIcon();
        }

        // ────────────────────────────────────────
        // ホイールで拡大縮小
        // ────────────────────────────────────────

        private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? ScaleStep : -ScaleStep;
            _scale = Math.Round(Math.Clamp(_scale + delta, ScaleMin, ScaleMax), 2);
            ClampOffset();
            ApplyTransform();
            UpdateScaleLabel();
            e.Handled = true;
        }

        private void UpdateScaleLabel()
        {
            if (ScaleLabel != null)
                ScaleLabel.Text = $"{(int)Math.Round(_scale * 100)}%";
        }

        // ────────────────────────────────────────
        // グラデーション角度スライダー
        // ────────────────────────────────────────

        private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressAngle || AngleBox == null) return;
            _gradAngle = AngleSlider.Value;
            AngleBox.Text = ((int)_gradAngle).ToString();
            UpdateBackground();
        }

        private void AngleBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(AngleBox.Text, out var v))
            {
                _gradAngle = Math.Clamp(v, 0, 360);
                _suppressAngle = true;
                AngleSlider.Value = _gradAngle;
                AngleBox.Text = ((int)_gradAngle).ToString();
                _suppressAngle = false;
                UpdateBackground();
            }
        }

        // ────────────────────────────────────────
        // ドラッグ移動
        // ────────────────────────────────────────

        private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(PreviewBorder);
            if (sender is IInputElement el) el.CaptureMouse();
        }

        private void Preview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(PreviewBorder);
            _offsetX += pos.X - _dragStart.X;
            _offsetY += pos.Y - _dragStart.Y;
            ClampOffset();
            ApplyTransform();
            _dragStart = pos;
        }

        private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            if (sender is IInputElement el) el.ReleaseMouseCapture();
        }

        /// <summary>移動範囲 = PreviewSize/2 × (scale − 1)</summary>
        private void ClampOffset()
        {
            double maxOffset = (PreviewSize / 2.0) * (_scale - 1.0);
            _offsetX = Math.Clamp(_offsetX, -maxOffset, maxOffset);
            _offsetY = Math.Clamp(_offsetY, -maxOffset, maxOffset);
        }

        private void ApplyTransform()
        {
            if (ScaleXform == null || TranslateXform == null) return;
            ScaleXform.ScaleX = _scale;
            ScaleXform.ScaleY = _scale;
            TranslateXform.X = _offsetX;
            TranslateXform.Y = _offsetY;
            UpdateScaleLabel();
        }

        // ────────────────────────────────────────
        // 背景設定
        // ────────────────────────────────────────

        private void BgType_Changed(object sender, RoutedEventArgs e)
        {
            if (ColorPanel == null) return;
            bool isSolid = BgSolid?.IsChecked == true;
            bool isGradient = BgGradient?.IsChecked == true;
            bool isImage = BgImageMode?.IsChecked == true;

            BgImagePanel.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
            ColorPanel.Visibility = (isSolid || isGradient) ? Visibility.Visible : Visibility.Collapsed;
            Color2Label.Visibility = isGradient ? Visibility.Visible : Visibility.Collapsed;
            Color2Button.Visibility = isGradient ? Visibility.Visible : Visibility.Collapsed;
            GradientAnglePanel.Visibility = isGradient ? Visibility.Visible : Visibility.Collapsed;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (BgRect == null || BgImage == null) return;
            BgImage.Visibility = Visibility.Collapsed;

            if (BgSolid?.IsChecked == true)
            {
                BgRect.Fill = new SolidColorBrush(_color1);
            }
            else if (BgGradient?.IsChecked == true)
            {
                BgRect.Fill = MakeCenteredGradient(_color1, _color2, _gradAngle);
            }
            else if (BgImageMode?.IsChecked == true)
            {
                BgRect.Fill = Brushes.Transparent;
                if (!string.IsNullOrEmpty(_bgImagePath) && File.Exists(_bgImagePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(_bgImagePath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        BgImage.Source = bmp;
                        BgImage.Visibility = Visibility.Visible;
                    }
                    catch { }
                }
            }
            else
            {
                BgRect.Fill = Brushes.Transparent;
            }
        }

        private void UpdateColorButtons()
        {
            if (Color1Button != null) Color1Button.Background = new SolidColorBrush(_color1);
            if (Color2Button != null) Color2Button.Background = new SolidColorBrush(_color2);
        }

        private void Color1_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_color1, out var c)) { _color1 = c; UpdateColorButtons(); UpdateBackground(); }
        }

        private void Color2_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_color2, out var c)) { _color2 = c; UpdateColorButtons(); UpdateBackground(); }
        }

        private void SelectBgImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Translate.SelectBgImage,
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            _bgImagePath = dialog.FileName;
            BgImageLabel.Text = Path.GetFileName(dialog.FileName);
            UpdateBackground();
        }

        private bool PickColor(Color current, out Color result)
        {
            var dlg = new WpfColorPickerDialog(current) { Owner = this };
            if (dlg.ShowDialog() == true) { result = dlg.SelectedColor; return true; }
            result = current;
            return false;
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
            catch { color = Colors.Transparent; return false; }
        }

        // ────────────────────────────────────────
        // 適用 / キャンセル
        // ────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ResultIconPath = _instance.IconPath;
            ResultScale = _scale;
            ResultOffsetX = _offsetX;
            ResultOffsetY = _offsetY;
            ResultBgType = BgSolid.IsChecked == true ? "Solid"
                                   : BgGradient.IsChecked == true ? "Gradient"
                                   : BgImageMode.IsChecked == true ? "Image"
                                   : "None";
            ResultBgColor1 = _color1.ToString();
            ResultBgColor2 = _color2.ToString();
            ResultBgGradientAngle = _gradAngle;
            ResultBgImagePath = _bgImagePath;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static LinearGradientBrush MakeCenteredGradient(Color c1, Color c2, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad) * 0.5;
            double dy = Math.Sin(rad) * 0.5;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 - dx, 0.5 - dy),
                EndPoint = new Point(0.5 + dx, 0.5 + dy),
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            brush.GradientStops.Add(new GradientStop(c1, 0.0));
            brush.GradientStops.Add(new GradientStop(c2, 1.0));
            brush.Freeze();
            return brush;
        }
    }
}