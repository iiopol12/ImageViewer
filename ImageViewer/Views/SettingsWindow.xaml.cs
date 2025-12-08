using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Models;

namespace ImageViewer.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        
        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            LoadSettings();
            
            // Bind slider value changes
            IntervalSlider.ValueChanged += (s, e) => IntervalText.Text = ((int)e.NewValue).ToString();
            PreloadSlider.ValueChanged += (s, e) => PreloadText.Text = ((int)e.NewValue).ToString();
            ThumbnailSlider.ValueChanged += (s, e) => ThumbnailText.Text = ((int)e.NewValue).ToString();
            MangaDecodeSlider.ValueChanged += (s, e) => MangaDecodeText.Text = ((int)e.NewValue).ToString();
        }
        
        private void LoadSettings()
        {
            // View Mode
            foreach (ComboBoxItem item in ViewModeComboBox.Items)
            {
                if (item.Tag is ViewMode mode && mode == _settings.DefaultViewMode)
                {
                    ViewModeComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Reading Direction
            foreach (ComboBoxItem item in DirectionComboBox.Items)
            {
                if (item.Tag is ReadingDirection dir && dir == _settings.ReadingDirection)
                {
                    DirectionComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Background Color
            foreach (ComboBoxItem item in BackgroundComboBox.Items)
            {
                if (item.Tag is BackgroundColor color && color == _settings.BackgroundColor)
                {
                    BackgroundComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Scroll Wheel Behavior
            foreach (ComboBoxItem item in ScrollWheelComboBox.Items)
            {
                if (item.Tag is ScrollWheelBehavior behavior && behavior == _settings.ScrollWheelBehavior)
                {
                    ScrollWheelComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Slideshow
            IntervalSlider.Value = _settings.SlideshowInterval;
            IntervalText.Text = _settings.SlideshowInterval.ToString();
            
            // Performance
            PreloadSlider.Value = _settings.PreloadCount;
            PreloadText.Text = _settings.PreloadCount.ToString();
            ThumbnailSlider.Value = _settings.ThumbnailSize;
            ThumbnailText.Text = _settings.ThumbnailSize.ToString();
            MangaDecodeSlider.Value = _settings.MangaDecodeWidth;
            MangaDecodeText.Text = _settings.MangaDecodeWidth.ToString();
            
            // Behavior
            RememberPositionCheckBox.IsChecked = _settings.RememberWindowPosition;
            RememberReadingCheckBox.IsChecked = _settings.RememberReadingPosition;
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // View Mode
            if (ViewModeComboBox.SelectedItem is ComboBoxItem viewItem && viewItem.Tag is ViewMode mode)
            {
                _settings.DefaultViewMode = mode;
            }
            
            // Reading Direction
            if (DirectionComboBox.SelectedItem is ComboBoxItem dirItem && dirItem.Tag is ReadingDirection dir)
            {
                _settings.ReadingDirection = dir;
            }
            
            // Background Color
            if (BackgroundComboBox.SelectedItem is ComboBoxItem bgItem && bgItem.Tag is BackgroundColor color)
            {
                _settings.BackgroundColor = color;
            }
            
            // Scroll Wheel Behavior
            if (ScrollWheelComboBox.SelectedItem is ComboBoxItem swItem && swItem.Tag is ScrollWheelBehavior behavior)
            {
                _settings.ScrollWheelBehavior = behavior;
            }
            
            // Slideshow
            _settings.SlideshowInterval = (int)IntervalSlider.Value;
            
            // Performance
            _settings.PreloadCount = (int)PreloadSlider.Value;
            _settings.ThumbnailSize = (int)ThumbnailSlider.Value;
            _settings.MangaDecodeWidth = (int)MangaDecodeSlider.Value;
            
            // Behavior
            _settings.RememberWindowPosition = RememberPositionCheckBox.IsChecked ?? true;
            _settings.RememberReadingPosition = RememberReadingCheckBox.IsChecked ?? true;
            
            _settings.Save();
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OpenDefaultAppsButton_Click(object sender, RoutedEventArgs e)
        {
            // 打开 Windows 默认应用设置，引导用户手动关联 PNG/JPG 等图片到本程序
            if (TryOpenSettingsUri("ms-settings:defaultapps"))
                return;

            if (TryOpenSettingsUri("ms-settings:defaultappsfileassociations"))
                return;

            MessageBox.Show(
                "无法自动打开默认应用设置，请手动前往“设置 > 应用 > 默认应用”将图片类型关联到 ImageViewer。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool TryOpenSettingsUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
