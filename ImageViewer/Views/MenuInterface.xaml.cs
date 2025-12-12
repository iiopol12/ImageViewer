using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using ImageViewer.Models;

namespace ImageViewer.Views
{
    public partial class MenuInterface : Window
    {
        private readonly AppSettings _settings;

        public ObservableCollection<AssociationOption> AssociationOptions { get; } = new();

        private static readonly string[] SupportedExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
        };

        // 定义颜色常量
        private static readonly SolidColorBrush ActiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4A9EFF"));
        private static readonly SolidColorBrush InactiveColor = new SolidColorBrush(Colors.White);

        public MenuInterface(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            DataContext = this;

            LoadSettings();
            InitializeAssociationOptions();
            ShowPage(MenuPage.Settings);

            IntervalSlider.ValueChanged += (s, e) => IntervalText.Text = ((int)e.NewValue).ToString();
            PreloadSlider.ValueChanged += (s, e) => PreloadText.Text = ((int)e.NewValue).ToString();
            ThumbnailSlider.ValueChanged += (s, e) => ThumbnailText.Text = ((int)e.NewValue).ToString();
            MangaDecodeSlider.ValueChanged += (s, e) => MangaDecodeText.Text = ((int)e.NewValue).ToString();
        }

        private enum MenuPage
        {
            Settings,
            Favorites,
            Associations
        }

        private void ShowPage(MenuPage page)
        {
            if (SettingsPage == null || FavoritesPage == null || AssociationsPage == null)
            {
                return;
            }

            // 切换页面可见性
            SettingsPage.Visibility = page == MenuPage.Settings ? Visibility.Visible : Visibility.Collapsed;
            FavoritesPage.Visibility = page == MenuPage.Favorites ? Visibility.Visible : Visibility.Collapsed;
            AssociationsPage.Visibility = page == MenuPage.Associations ? Visibility.Visible : Visibility.Collapsed;

            // 更新图标颜色
            UpdateIconColors(page);

            // 更新标题栏文字
            UpdatePageTitle(page);
        }

        // 更新标题栏文字
        private void UpdatePageTitle(MenuPage page)
        {
            switch (page)
            {
                case MenuPage.Settings:
                    PageTitleText.Text = "常规设置";
                    break;
                case MenuPage.Favorites:
                    PageTitleText.Text = "收藏";
                    break;
                case MenuPage.Associations:
                    PageTitleText.Text = "关联设置";
                    break;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 自动保存设置
            SaveButton_Click(sender, e);
        }
        private void UpdateIconColors(MenuPage activePage)
        {
            // 重置所有图标为白色
            SettingsIcon.Fill = InactiveColor;
            FavoritesIcon.Fill = InactiveColor;
            AssociationsIcon.Fill = InactiveColor;

            // 将当前激活页面的图标设置为蓝色
            switch (activePage)
            {
                case MenuPage.Settings:
                    SettingsIcon.Fill = ActiveColor;
                    break;
                case MenuPage.Favorites:
                    FavoritesIcon.Fill = ActiveColor;
                    break;
                case MenuPage.Associations:
                    AssociationsIcon.Fill = ActiveColor;
                    break;
            }
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(MenuPage.Settings);
        }

        private void FavoritesNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(MenuPage.Favorites);
        }

        private void AssociationsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(MenuPage.Associations);
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

            // LocalSend
            LocalSendPathTextBox.Text = _settings.LocalSendPath;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // View Mode
            if (ViewModeComboBox.SelectedItem is ComboBoxItem viewItem && viewItem.Tag is ViewMode mode)
            {
                _settings.DefaultViewMode = mode;
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

            // LocalSend
            _settings.LocalSendPath = LocalSendPathTextBox.Text ?? string.Empty;

            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseLocalSendButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "LocalSend 可执行文件|LocalSend.exe;localsend.exe;localsend_app.exe|可执行文件|*.exe|所有文件|*.*",
                Title = "选择 LocalSend 可执行文件"
            };

            if (dialog.ShowDialog() == true)
            {
                LocalSendPathTextBox.Text = dialog.FileName;
            }
        }

        private void InitializeAssociationOptions()
        {
            AssociationOptions.Clear();
            foreach (var ext in SupportedExtensions)
            {
                AssociationOptions.Add(new AssociationOption(ext, true));
            }
        }

        private void SelectAllAssociations_Click(object sender, RoutedEventArgs e)
        {
            foreach (var opt in AssociationOptions)
            {
                opt.IsSelected = true;
            }
        }

        private void ClearAllAssociations_Click(object sender, RoutedEventArgs e)
        {
            foreach (var opt in AssociationOptions)
            {
                opt.IsSelected = false;
            }
        }

        private void ApplyAssociationsButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AssociationOptions.Where(o => o.IsSelected).Select(o => o.Extension).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("请至少选择一种格式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = TryRegisterAsDefaultViewer(selected);
            if (result.success)
            {
                var formats = string.Join("/", selected.Select(e2 => e2.TrimStart('.').ToUpperInvariant()));
                MessageBox.Show(
                    $"已写入注册表,将以下格式默认打开方式指向 ImageViewer:{formats}。\n如果资源管理器未立即生效,可重新打开资源管理器或重启系统。",
                    "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"部分注册表项未能写入:{result.errorMessage}\n可尝试以管理员身份运行或手动在默认应用中设置。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 将本程序注册为支持格式的默认查看器(用户范围,不需要管理员权限)。
        /// </summary>
        private (bool success, string? errorMessage) TryRegisterAsDefaultViewer(IReadOnlyList<string> extensions)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return (false, "无法确定程序路径");
                }

                const string progId = "ImageViewer.image";
                const string description = "ImageViewer Image";

                using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                {
                    progIdKey?.SetValue(string.Empty, description);
                    progIdKey?.CreateSubKey("DefaultIcon")?.SetValue(string.Empty, $"\"{exePath}\",0");
                    progIdKey?.CreateSubKey(@"shell\open\command")?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
                }

                foreach (var ext in extensions)
                {
                    using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}"))
                    {
                        extKey?.SetValue(string.Empty, progId, RegistryValueKind.String);
                    }

                    using (var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithProgids"))
                    {
                        openWith?.SetValue(progId, string.Empty, RegistryValueKind.String);
                    }

                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice", false);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public sealed partial class AssociationOption : ObservableObject
        {
            public string Extension { get; }
            public string DisplayName { get; }

            [ObservableProperty]
            private bool _isSelected;

            public AssociationOption(string extension, bool isSelected)
            {
                Extension = extension;
                DisplayName = extension.TrimStart('.').ToUpperInvariant();
                _isSelected = isSelected;
            }
        }
    }
}