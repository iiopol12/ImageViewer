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
using ImageViewer.ViewModels;
using Path = System.IO.Path;

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
        public ObservableCollection<AssociationOption> AssociationNormalOptions { get; } = new();
        public ObservableCollection<AssociationOption> AssociationRawOptions { get; } = new();
        public ObservableCollection<FavoriteItem> FavoriteImageItems { get; } = new();
        public ObservableCollection<FavoriteFolderItem> FavoriteFolderItems { get; } = new();
        public ObservableCollection<FavoriteItem> FavoriteFolderImageItems { get; } = new();

        private FavoriteSortOption _favoriteImageSort = FavoriteSortOption.AddedAtDesc;
        private FavoriteSortOption _favoriteFolderSort = FavoriteSortOption.AddedAtDesc;
        private FavoriteFolderItem? _selectedFavoriteFolder;

        // 定义颜色常量
        private static readonly SolidColorBrush ActiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4A9EFF"));
        private static readonly SolidColorBrush InactiveColor = new SolidColorBrush(Colors.White);
        private static readonly HashSet<string> RawExtensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".rw2", ".orf", ".pef", ".srw"
        };


      


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

            ShowFavoriteFoldersTab();
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
            ShowFavoriteFoldersTab();
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

            // Filters
            EnableFiltersCheckBox.IsChecked = _settings.FiltersEnabled;
            SizeFilterCheckBox.IsChecked = _settings.SizeFilterEnabled;
            MinWidthTextBox.Text = _settings.MinWidth.ToString();
            MinHeightTextBox.Text = _settings.MinHeight.ToString();
            MaxWidthTextBox.Text = _settings.MaxWidth.ToString();
            MaxHeightTextBox.Text = _settings.MaxHeight.ToString();
            FileSizeFilterCheckBox.IsChecked = _settings.FileSizeFilterEnabled;
            MinFileSizeTextBox.Text = _settings.MinFileSizeKB.ToString();
            MaxFileSizeTextBox.Text = _settings.MaxFileSizeMB.ToString();

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

            // Filters
            _settings.FiltersEnabled = EnableFiltersCheckBox.IsChecked ?? false;
            _settings.SizeFilterEnabled = SizeFilterCheckBox.IsChecked ?? false;
            _settings.MinWidth = ParseNonNegativeInt(MinWidthTextBox.Text);
            _settings.MinHeight = ParseNonNegativeInt(MinHeightTextBox.Text);
            _settings.MaxWidth = ParseNonNegativeInt(MaxWidthTextBox.Text);
            _settings.MaxHeight = ParseNonNegativeInt(MaxHeightTextBox.Text);
            _settings.FileSizeFilterEnabled = FileSizeFilterCheckBox.IsChecked ?? false;
            _settings.MinFileSizeKB = ParseNonNegativeInt(MinFileSizeTextBox.Text);
            _settings.MaxFileSizeMB = ParseNonNegativeInt(MaxFileSizeTextBox.Text);

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

            FavoriteImageItems.Clear();
            FavoriteFolderItems.Clear();
            FavoriteFolderImageItems.Clear();

            var imageUnique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bookmark in _settings.Bookmarks
                         .Where(b => b.Type == BookmarkType.Image)
                         .OrderByDescending(b => b.CreatedAt))
            {
                if (string.IsNullOrWhiteSpace(bookmark.FilePath))
                {
                    continue;
                }

                if (!imageUnique.Add(bookmark.FilePath))
                {
                    continue;
                }

                var pathInfo = ResolveBookmarkPath(bookmark.FilePath);
                FavoriteImageItems.Add(new FavoriteItem(bookmark, pathInfo));
            }

            var folderUnique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bookmark in _settings.Bookmarks
                         .Where(b => b.Type == BookmarkType.Folder)
                         .OrderByDescending(b => b.CreatedAt))
            {
                if (string.IsNullOrWhiteSpace(bookmark.FilePath))
                {
                    continue;
                }

                if (!folderUnique.Add(bookmark.FilePath))
                {
                    continue;
                }

                FavoriteFolderItems.Add(new FavoriteFolderItem(bookmark.FilePath, bookmark.Name, bookmark.CreatedAt, bookmark.IsExpanded));
            }

            ApplyFavoriteImageSort();
            ApplyFavoriteFolderSort();
            UpdateFavoriteFolderCounts();
            SyncExpandedFavoriteFolderImages();

            if (_selectedFavoriteFolder != null)
            {
                var refreshedFolder = FavoriteFolderItems.FirstOrDefault(folder =>
                    string.Equals(folder.FolderPath, _selectedFavoriteFolder.FolderPath, StringComparison.OrdinalIgnoreCase));

                if (refreshedFolder != null)
                {
                    _selectedFavoriteFolder = refreshedFolder;
                    RefreshFavoriteFolderImages(refreshedFolder);
                }
                else
                {
                    ShowFavoriteFolderList();
                }
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

            foreach (var item in FavoriteImageItems)
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
                _settings.Bookmarks.RemoveAll(b => b.Type == BookmarkType.Image &&
                                                   string.Equals(b.FilePath, item.BookmarkKey, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                FavoriteImageItems.Remove(item);
                UpdateFavoriteFolderCounts();
                SyncExpandedFavoriteFolderImages();
                if (_selectedFavoriteFolder != null)
                {
                    RefreshFavoriteFolderImages(_selectedFavoriteFolder);
                }
            }
        }

        private void FavoriteFolderSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavoriteFolderSortComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                _favoriteFolderSort = tag == "Name" ? FavoriteSortOption.NameAsc : FavoriteSortOption.AddedAtDesc;
                ApplyFavoriteFolderSort();
            }
        }

        private async void AddFavoriteFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择要收藏的文件夹",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (TryAddFavoriteFolder(dialog.SelectedPath))
                {
                    await RefreshFavoritesAsync();
                }
            }
        }

        private async void AddCurrentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow mainWindow)
            {
                MessageBox.Show("无法获取主窗口。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (mainWindow.DataContext is not MainViewModel viewModel)
            {
                MessageBox.Show("无法获取主窗口数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var folderPath = viewModel.CurrentFolderPath ?? string.Empty;
            if (File.Exists(folderPath))
            {
                folderPath = Path.GetDirectoryName(folderPath) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                MessageBox.Show("当前没有可收藏的文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (TryAddFavoriteFolder(folderPath))
            {
                await RefreshFavoritesAsync();
            }
        }

        private void OpenFavoriteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteFolderItem folder })
            {
                ShowFavoriteFolderImages(folder);
            }
        }

        private void ToggleFavoriteFolderExpanded_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteFolderItem folder })
            {
                folder.IsExpanded = !folder.IsExpanded;

                var bookmark = _settings.Bookmarks.FirstOrDefault(b => b.Type == BookmarkType.Folder &&
                                                                       string.Equals(b.FilePath, folder.FolderPath, StringComparison.OrdinalIgnoreCase));
                if (bookmark != null)
                {
                    bookmark.IsExpanded = folder.IsExpanded;
                    _settings.Save();
                }

                PopulateFavoriteFolderImages(folder);
            }
        }

        private async void OpenFavoriteFolderInViewer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteFolderItem folder } && folder.Exists)
            {
                if (Owner is not MainWindow mainWindow)
                {
                    MessageBox.Show("无法获取主窗口。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (mainWindow.DataContext is not MainViewModel viewModel)
                {
                    MessageBox.Show("无法获取主窗口数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await viewModel.LoadFolder(folder.FolderPath);
            }
        }

        private void OpenFavoriteFolderInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteFolderItem folder } && folder.Exists)
            {
                Process.Start("explorer.exe", $"\"{folder.FolderPath}\"");
            }
        }

        private void BackToFavoriteFolders_Click(object sender, RoutedEventArgs e)
        {
            ShowFavoriteFolderList();
        }

        private async void RemoveFavoriteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FavoriteFolderItem folder })
            {
                _settings.Bookmarks.RemoveAll(b => b.Type == BookmarkType.Folder &&
                                                   string.Equals(b.FilePath, folder.FolderPath, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                await RefreshFavoritesAsync();
            }
        }

        private async void RemoveSelectedFavoriteFolders_Click(object sender, RoutedEventArgs e)
        {
            var selected = FavoriteFolderItems.Where(item => item.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            foreach (var item in selected)
            {
                _settings.Bookmarks.RemoveAll(b => b.Type == BookmarkType.Folder &&
                                                   string.Equals(b.FilePath, item.FolderPath, StringComparison.OrdinalIgnoreCase));
            }

            _settings.Save();
            await RefreshFavoritesAsync();
        }

        private async void ClearFavoriteFolders_Click(object sender, RoutedEventArgs e)
        {
            if (FavoriteFolderItems.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show("确定要清空所有收藏文件夹吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _settings.Bookmarks.RemoveAll(b => b.Type == BookmarkType.Folder);
            _settings.Save();
            await RefreshFavoritesAsync();
        }

        private async void OpenFavoriteInViewer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: FavoriteItem item })
            {
                return;
            }

            if (!item.Exists || string.IsNullOrWhiteSpace(item.FilePath))
            {
                MessageBox.Show("文件不存在或无法打开。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Owner is not MainWindow mainWindow)
            {
                MessageBox.Show("无法获取主窗口。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (mainWindow.DataContext is not MainViewModel viewModel)
            {
                MessageBox.Show("无法获取主窗口数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (item.SourceKind == BookmarkSourceKind.PdfPage)
            {
                await viewModel.LoadPdf(item.FilePath);
                if (item.PageIndex >= 0 && viewModel.Images.Count > 0)
                {
                    viewModel.CurrentIndex = Math.Min(item.PageIndex, viewModel.Images.Count - 1);
                }
                return;
            }

            if (item.SourceKind == BookmarkSourceKind.ZipEntry && !string.IsNullOrWhiteSpace(item.ArchiveEntryPath))
            {
                await viewModel.LoadArchive(item.FilePath);
                var targetIndex = viewModel.Images
                    .Select((image, index) => new { image, index })
                    .FirstOrDefault(pair => pair.image.SourceKind == ImageSourceKind.ZipEntry &&
                                            string.Equals(pair.image.ArchiveEntryPath, item.ArchiveEntryPath, StringComparison.OrdinalIgnoreCase))
                    ?.index ?? -1;

                if (targetIndex >= 0)
                {
                    viewModel.CurrentIndex = targetIndex;
                }
                return;
            }

            await viewModel.LoadImageFromPath(item.FilePath);
        }

        private static void RebuildCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private void ApplyFavoriteImageSort()
        {
            var sorted = _favoriteImageSort switch
            {
                FavoriteSortOption.NameAsc => FavoriteImageItems
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                _ => FavoriteImageItems
                    .OrderByDescending(item => item.CreatedAt)
                    .ToList()
            };

            RebuildCollection(FavoriteImageItems, sorted);
            SyncExpandedFavoriteFolderImages();
        }

        private void ApplyFavoriteFolderSort()
        {
            var sorted = _favoriteFolderSort switch
            {
                FavoriteSortOption.NameAsc => FavoriteFolderItems
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                _ => FavoriteFolderItems
                    .OrderByDescending(item => item.CreatedAt)
                    .ToList()
            };

            RebuildCollection(FavoriteFolderItems, sorted);
        }

        private void SyncExpandedFavoriteFolderImages()
        {
            foreach (var folder in FavoriteFolderItems)
            {
                PopulateFavoriteFolderImages(folder);
            }
        }

        private void PopulateFavoriteFolderImages(FavoriteFolderItem folder)
        {
            if (!folder.IsExpanded)
            {
                folder.Images.Clear();
                return;
            }

            var filtered = FavoriteImageItems.Where(item => IsImageUnderFolder(item, folder.FolderPath));
            IEnumerable<FavoriteItem> sorted = _favoriteImageSort switch
            {
                FavoriteSortOption.NameAsc => filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(item => item.CreatedAt)
            };

            RebuildCollection(folder.Images, sorted);
        }

        private void UpdateFavoriteFolderCounts()
        {
            foreach (var folder in FavoriteFolderItems)
            {
                folder.FavoriteImageCount = FavoriteImageItems.Count(item => IsImageUnderFolder(item, folder.FolderPath));
            }
        }

        private void RefreshFavoriteFolderImages(FavoriteFolderItem folder)
        {
            var filtered = FavoriteImageItems.Where(item => IsImageUnderFolder(item, folder.FolderPath));
            IEnumerable<FavoriteItem> sorted = _favoriteImageSort switch
            {
                FavoriteSortOption.NameAsc => filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(item => item.CreatedAt)
            };

            RebuildCollection(FavoriteFolderImageItems, sorted);

            if (FavoriteFolderImagesTitle != null)
            {
                FavoriteFolderImagesTitle.Text = folder.Name;
            }
        }

        private void ShowFavoriteFoldersTab()
        {
            if (FavoriteFoldersPanel == null)
            {
                return;
            }

            FavoriteFoldersPanel.Visibility = Visibility.Visible;
            ShowFavoriteFolderList();
        }

        private void ShowFavoriteFolderList()
        {
            if (FavoriteFoldersListPanel == null || FavoriteFolderImagesPanel == null)
            {
                return;
            }

            FavoriteFoldersListPanel.Visibility = Visibility.Visible;
            FavoriteFolderImagesPanel.Visibility = Visibility.Collapsed;
            FavoriteFolderImageItems.Clear();
            _selectedFavoriteFolder = null;

            if (FavoriteFolderImagesTitle != null)
            {
                FavoriteFolderImagesTitle.Text = "收藏文件夹";
            }
        }

        private void ShowFavoriteFolderImages(FavoriteFolderItem folder)
        {
            if (FavoriteFoldersListPanel == null || FavoriteFolderImagesPanel == null)
            {
                return;
            }

            _selectedFavoriteFolder = folder;
            FavoriteFoldersListPanel.Visibility = Visibility.Collapsed;
            FavoriteFolderImagesPanel.Visibility = Visibility.Visible;
            RefreshFavoriteFolderImages(folder);
        }

        private bool TryAddFavoriteFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                MessageBox.Show("文件夹不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var normalized = Path.GetFullPath(folderPath);
            if (_settings.Bookmarks.Any(b => b.Type == BookmarkType.Folder &&
                                             string.Equals(b.FilePath, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该文件夹已在收藏中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var displayName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = normalized;
            }

            _settings.Bookmarks.Add(new Bookmark
            {
                FilePath = normalized,
                Name = displayName,
                Type = BookmarkType.Folder,
                SortKey = displayName,
                CreatedAt = DateTime.Now,
                IsExpanded = true
            });

            _settings.Save();
            return true;
        }

        private static bool IsImageUnderFolder(FavoriteItem item, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            try
            {
                var itemPath = Path.GetFullPath(item.FilePath);
                var folderFullPath = Path.GetFullPath(folderPath);
                if (!folderFullPath.EndsWith(Path.DirectorySeparatorChar))
                {
                    folderFullPath += Path.DirectorySeparatorChar;
                }

                return itemPath.StartsWith(folderFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static BookmarkPathInfo ResolveBookmarkPath(string bookmarkKey)
        {
            if (bookmarkKey.StartsWith("pdf:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = bookmarkKey.Substring(4);
                var separatorIndex = payload.IndexOf("|page=", StringComparison.OrdinalIgnoreCase);
                var pdfPath = separatorIndex >= 0 ? payload.Substring(0, separatorIndex) : payload;
                return new BookmarkPathInfo(pdfPath, BookmarkSourceKind.PdfPage, null);
            }

            if (bookmarkKey.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = bookmarkKey.Substring(4);
                var separatorIndex = payload.IndexOf('|');
                if (separatorIndex >= 0)
                {
                    var archivePath = payload.Substring(0, separatorIndex);
                    var entryPath = payload.Substring(separatorIndex + 1);
                    return new BookmarkPathInfo(archivePath, BookmarkSourceKind.ZipEntry, entryPath);
                }

                return new BookmarkPathInfo(payload, BookmarkSourceKind.ZipEntry, null);
            }

            return new BookmarkPathInfo(bookmarkKey, BookmarkSourceKind.File, null);
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

        private static int ParseNonNegativeInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (int.TryParse(text.Trim(), out var value))
            {
                return Math.Max(0, value);
            }

            return 0;
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
            AssociationNormalOptions.Clear();
            AssociationRawOptions.Clear();
            foreach (var ext in ImageService.SupportedExtensions)
            {
                var option = new AssociationOption(ext, true);
                AssociationOptions.Add(option);
                if (IsRawExtension(ext))
                {
                    AssociationRawOptions.Add(option);
                }
                else
                {
                    AssociationNormalOptions.Add(option);
                }
            }
        }

        private static bool IsRawExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            var normalized = extension.StartsWith(".")
                ? extension
                : $".{extension}";

            return RawExtensionSet.Contains(normalized);
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

        private enum FavoriteSortOption
        {
            AddedAtDesc,
            NameAsc
        }

        public enum BookmarkSourceKind
        {
            File,
            PdfPage,
            ZipEntry
        }

        public sealed class BookmarkPathInfo
        {
            public BookmarkPathInfo(string filePath, BookmarkSourceKind sourceKind, string? archiveEntryPath)
            {
                FilePath = filePath;
                SourceKind = sourceKind;
                ArchiveEntryPath = archiveEntryPath;
            }

            public string FilePath { get; }
            public BookmarkSourceKind SourceKind { get; }
            public string? ArchiveEntryPath { get; }
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
            public string BookmarkKey { get; }
            public string FilePath { get; }
            public string DisplayPath { get; }
            public string Name { get; }
            public DateTime CreatedAt { get; }
            public int PageIndex { get; }
            public BookmarkSourceKind SourceKind { get; }
            public string? ArchiveEntryPath { get; }
            public bool Exists { get; }

            [ObservableProperty]
            private BitmapSource? _thumbnail;

            [ObservableProperty]
            private bool _isLoading;

            [ObservableProperty]
            private bool _isSelected;

            [ObservableProperty]
            private bool _hasError;

            [ObservableProperty]
            private string _errorMessage = string.Empty;

            public FavoriteItem(Bookmark bookmark, BookmarkPathInfo pathInfo)
            {
                BookmarkKey = bookmark.FilePath;
                FilePath = pathInfo.FilePath;
                DisplayPath = bookmark.FilePath;
                var displayName = string.IsNullOrWhiteSpace(bookmark.Name)
                    ? System.IO.Path.GetFileName(pathInfo.FilePath)
                    : bookmark.Name;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = pathInfo.FilePath;
                }
                Name = displayName;
                CreatedAt = bookmark.CreatedAt;
                PageIndex = bookmark.PageIndex;
                SourceKind = pathInfo.SourceKind;
                ArchiveEntryPath = pathInfo.ArchiveEntryPath;
                Exists = !string.IsNullOrWhiteSpace(pathInfo.FilePath) && File.Exists(pathInfo.FilePath);
            }
        }

        public sealed partial class FavoriteFolderItem : ObservableObject
        {
            public string FolderPath { get; }
            public string Name { get; }
            public DateTime CreatedAt { get; }
            public bool Exists { get; }
            public ObservableCollection<FavoriteItem> Images { get; } = new();

            [ObservableProperty]
            private int _favoriteImageCount;

            [ObservableProperty]
            private bool _isExpanded;

            [ObservableProperty]
            private bool _isSelected;

            public FavoriteFolderItem(string folderPath, string name, DateTime createdAt, bool isExpanded)
            {
                FolderPath = folderPath;
                var displayName = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(folderPath) : name;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = folderPath;
                }
                Name = displayName;
                CreatedAt = createdAt;
                Exists = Directory.Exists(folderPath);
                _isExpanded = isExpanded;
            }
        }
    }
}
