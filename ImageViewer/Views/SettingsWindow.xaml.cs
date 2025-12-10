using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
            var result = TryRegisterAsJpgDefault();
            if (result.success)
            {
                MessageBox.Show(
                    "已写入注册表，将 JPG/JPEG 默认打开方式指向 ImageViewer。\n如果资源管理器未立即生效，可重新打开资源管理器或重启系统。",
                    "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"部分注册表项未能写入：{result.errorMessage}\n可尝试以管理员身份运行或手动在默认应用中设置。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 将本程序注册为 JPG/JPEG 的默认查看器（用户范围，不需要管理员权限）。
        /// </summary>
        private (bool success, string? errorMessage) TryRegisterAsJpgDefault()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return (false, "无法确定程序路径");
                }

                const string progId = "ImageViewer.jpg";
                const string description = "ImageViewer JPG";

                // 写入 ProgID
                using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                {
                    progIdKey?.SetValue(string.Empty, description);
                    progIdKey?.CreateSubKey("DefaultIcon")?.SetValue(string.Empty, $"\"{exePath}\",0");
                    progIdKey?.CreateSubKey(@"shell\open\command")?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
                }

                // 关联扩展名（两种写法都覆盖）
                using (var jpgKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.jpg"))
                {
                    jpgKey?.SetValue(string.Empty, progId, RegistryValueKind.String);
                }
                using (var jpegKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.jpeg"))
                {
                    jpegKey?.SetValue(string.Empty, progId, RegistryValueKind.String);
                }

                // 填充 OpenWithProgids，增加兼容性
                using (var openWith = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jpg\OpenWithProgids"))
                {
                    openWith?.SetValue(progId, string.Empty, RegistryValueKind.String);
                }
                using (var openWith = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jpeg\OpenWithProgids"))
                {
                    openWith?.SetValue(progId, string.Empty, RegistryValueKind.String);
                }

                // 清理 UserChoice，让系统回退到我们写入的关联
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jpg\UserChoice", false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jpeg\UserChoice", false);

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
