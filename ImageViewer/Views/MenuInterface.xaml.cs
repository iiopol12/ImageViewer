using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Views
{
    public partial class MenuInterface : Window
    {
        private readonly AppSettings _settings;
        private readonly ImageService _imageService = new();
        private CancellationTokenSource? _favoritesCts;



        public string WebsiteUrl = "https://www.52pojie.cn/thread-2079875-1-1.html";

        public string AppVersion { get; } = GetAppVersion();

        private static string GetAppVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                var informationalVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informationalVersion))
                {
                    var plusIndex = informationalVersion.IndexOf('+');
                    return plusIndex >= 0 ? informationalVersion.Substring(0, plusIndex) : informationalVersion;
                }

                var version = assembly.GetName().Version;
                return version?.ToString(3) ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }


        public ObservableCollection<AssociationOption> AssociationOptions { get; } = new();
        public ObservableCollection<FavoriteItem> FavoriteItems { get; } = new();

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
            MangaGapSlider.ValueChanged += (s, e) => MangaGapText.Text = ((int)e.NewValue).ToString();
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
            _ = RefreshFavoritesAsync();
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
            ShuffleSlideshowCheckBox.IsChecked = _settings.SlideshowShuffle;

            // Performance
            PreloadSlider.Value = _settings.PreloadCount;
            PreloadText.Text = _settings.PreloadCount.ToString();
            ThumbnailSlider.Value = _settings.ThumbnailSize;
            ThumbnailText.Text = _settings.ThumbnailSize.ToString();
            MangaGapSlider.Value = _settings.MangaGap;
            MangaGapText.Text = ((int)_settings.MangaGap).ToString();
            MangaDecodeSlider.Value = _settings.MangaDecodeWidth;
            MangaDecodeText.Text = _settings.MangaDecodeWidth.ToString();

            foreach (ComboBoxItem item in ArchiveStrategyComboBox.Items)
            {
                if (item.Tag is ArchiveLoadStrategy strategy && strategy == _settings.ArchiveLoadStrategy)
                {
                    ArchiveStrategyComboBox.SelectedItem = item;
                    break;
                }
            }

             // Behavior
             RememberPositionCheckBox.IsChecked = _settings.RememberWindowPosition;
             RememberReadingCheckBox.IsChecked = _settings.RememberReadingPosition;
             FreezeDuringResizeCheckBox.IsChecked = _settings.FreezeDuringResize;
             ShowStatusBarCheckBox.IsChecked = _settings.ShowStatusBar;

             // Subfolder scan
             ScanSubfoldersCheckBox.IsChecked = _settings.ScanSubfoldersEnabled;
             foreach (ComboBoxItem item in ScanSubfoldersDepthComboBox.Items)
             {
                 if (int.TryParse(item.Tag?.ToString(), out var depth) && depth == _settings.ScanSubfoldersDepth)
                 {
                     ScanSubfoldersDepthComboBox.SelectedItem = item;
                     break;
                 }
             }
 
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
            _settings.SlideshowShuffle = ShuffleSlideshowCheckBox.IsChecked ?? false;

            // Performance
            _settings.PreloadCount = (int)PreloadSlider.Value;
            _settings.ThumbnailSize = (int)ThumbnailSlider.Value;
            _settings.MangaGap = MangaGapSlider.Value;
            _settings.MangaDecodeWidth = (int)MangaDecodeSlider.Value;

            if (ArchiveStrategyComboBox.SelectedItem is ComboBoxItem archiveItem && archiveItem.Tag is ArchiveLoadStrategy strategy)
            {
                _settings.ArchiveLoadStrategy = strategy;
            }

              // Behavior
              _settings.RememberWindowPosition = RememberPositionCheckBox.IsChecked ?? true;
              _settings.RememberReadingPosition = RememberReadingCheckBox.IsChecked ?? true;
              _settings.FreezeDuringResize = FreezeDuringResizeCheckBox.IsChecked ?? true;
              _settings.ShowStatusBar = ShowStatusBarCheckBox.IsChecked ?? true;

              // Subfolder scan
              _settings.ScanSubfoldersEnabled = ScanSubfoldersCheckBox.IsChecked ?? false;
              if (ScanSubfoldersDepthComboBox.SelectedItem is ComboBoxItem depthItem &&
                  int.TryParse(depthItem.Tag?.ToString(), out var depth))
              {
                  _settings.ScanSubfoldersDepth = depth;
              }
 
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _favoritesCts?.Cancel();
            _favoritesCts?.Dispose();
            _imageService.Dispose();
        }

        private async Task RefreshFavoritesAsync()
        {
            _favoritesCts?.Cancel();
            _favoritesCts?.Dispose();
            _favoritesCts = new CancellationTokenSource();
            var token = _favoritesCts.Token;

            FavoriteItems.Clear();

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bookmark in _settings.Bookmarks.OrderByDescending(b => b.CreatedAt))
            {
                if (string.IsNullOrWhiteSpace(bookmark.FilePath))
                {
                    continue;
                }

                if (!unique.Add(bookmark.FilePath))
                {
                    continue;
                }

                FavoriteItems.Add(new FavoriteItem(bookmark.FilePath, bookmark.Name, bookmark.CreatedAt));
            }

            try
            {
                await LoadFavoriteThumbnailsAsync(token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        private async Task LoadFavoriteThumbnailsAsync(CancellationToken token)
        {
            var thumbnailSize = Math.Max(40, _settings.ThumbnailSize);

            foreach (var item in FavoriteItems)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (!item.Exists || item.Thumbnail != null)
                {
                    continue;
                }

                item.IsLoading = true;
                try
                {
                    var imageInfo = ImageInfo.FromFile(item.FilePath);
                    item.Thumbnail = await _imageService.LoadThumbnailAsync(imageInfo, thumbnailSize, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    item.HasError = true;
                    item.ErrorMessage = ex.Message;
                }
                finally
                {
                    item.IsLoading = false;
                }
            }
        }

        private void OpenFavoriteInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteItem item } && item.Exists)
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
        }

        private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteItem item })
            {
                _settings.Bookmarks.RemoveAll(b => string.Equals(b.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                FavoriteItems.Remove(item);
            }
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




        // 打开网站方法
        private void OpenWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(WebsiteUrl))
                {
                    // 确保URL格式正确
                    string url = WebsiteUrl;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
                        url = "https://" + url;
                    }

                    // 使用默认浏览器打开
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show("请先设置网站地址", "提示",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开网站失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        private void InitializeAssociationOptions()
        {
            AssociationOptions.Clear();
            foreach (var ext in ImageService.SupportedExtensions)
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

        public sealed partial class FavoriteItem : ObservableObject
        {
            public string FilePath { get; }
            public string Name { get; }
            public DateTime CreatedAt { get; }
            public bool Exists { get; }

            [ObservableProperty]
            private BitmapSource? _thumbnail;

            [ObservableProperty]
            private bool _isLoading;

            [ObservableProperty]
            private bool _hasError;

            [ObservableProperty]
            private string _errorMessage = string.Empty;

            public FavoriteItem(string filePath, string name, DateTime createdAt)
            {
                FilePath = filePath;
                Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(filePath) : name;
                CreatedAt = createdAt;
                Exists = File.Exists(filePath);
            }
        }
    }
}
