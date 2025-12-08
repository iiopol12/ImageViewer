using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageViewer.Models;
using ImageViewer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageViewer.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ImageService _imageService;
        private readonly FileWatcherService _fileWatcher;
        private readonly DispatcherTimer _slideshowTimer;
        private CancellationTokenSource? _preloadCts;
        private bool _suppressIndexChangeHandling;

        private CancellationTokenSource? _thumbnailCts;
        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(4); // 限制并发数为4

        public MainViewModel()
        {
            _imageService = new ImageService();
            _fileWatcher = new FileWatcherService();
            _slideshowTimer = new DispatcherTimer();
            _slideshowTimer.Tick += SlideshowTimer_Tick;

            Settings = AppSettings.Load();
            CurrentViewMode = Settings.DefaultViewMode;

            _fileWatcher.FileCreated += OnFileCreated;
            _fileWatcher.FileDeleted += OnFileDeleted;
            _fileWatcher.FileRenamed += OnFileRenamed;
        }

        #region Properties

        [ObservableProperty]
        private AppSettings _settings = new();

        [ObservableProperty]
        private ObservableCollection<ImageInfo> _images = new();

        [ObservableProperty]
        private ImageInfo? _currentImage;

        [ObservableProperty]
        private ImageInfo? _secondImage;

        [ObservableProperty]
        private BitmapSource? _displayImage;

        [ObservableProperty]
        private BitmapSource? _secondDisplayImage;

        [ObservableProperty]
        private int _currentIndex = -1;

        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        [ObservableProperty]
        private ViewMode _currentViewMode = ViewMode.Single;

        [ObservableProperty]
        private double _zoomLevel = 1.0;

        [ObservableProperty]
        private double _mangaZoomLevel = 1.0;

        [ObservableProperty]
        private double _panX;

        [ObservableProperty]
        private double _panY;

        [ObservableProperty]
        private bool _isFullScreen;

        [ObservableProperty]
        private bool _isSlideShowActive;

        [ObservableProperty]
        private bool _isBusyLoading;

        [ObservableProperty]
        private bool _isImageLoading;

        [ObservableProperty]
        private string _statusMessage = "就绪";

        [ObservableProperty]
        private bool _fitToWindow = true;

        [ObservableProperty]
        private double _scrollOffset;

        [ObservableProperty]
        private bool _showEmptyState = true;

        public string PositionText => Images.Count > 0 && CurrentIndex >= 0
            ? $"第 {CurrentIndex + 1}/{Images.Count} 张"
            : "无图片";

        public bool HasImages => Images.Count > 0;
        public bool CanGoPrevious => CurrentIndex > 0;
        public bool CanGoNext => CurrentIndex < Images.Count - 1;
        public bool IsDoublePage => CurrentViewMode == ViewMode.DoublePage;
        public bool IsMangaMode => CurrentViewMode == ViewMode.Manga;
        public bool IsSingleMode => CurrentViewMode == ViewMode.Single;
        public double ActiveZoomLevel => IsMangaMode ? MangaZoomLevel : ZoomLevel;

        partial void OnZoomLevelChanged(double value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }

        partial void OnMangaZoomLevelChanged(double value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }

        partial void OnCurrentViewModeChanged(ViewMode value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }

        partial void OnCurrentIndexChanged(int value)
        {
            if (_suppressIndexChangeHandling)
                return;

            if (value < 0 || value >= Images.Count)
                return;

            _ = LoadCurrentImage();
        }

        partial void OnIsFullScreenChanged(bool value)
        {
            if (!value)
            {
                StopSlideShow();
            }
        }

        #endregion

        #region Commands

        [RelayCommand]
        private async Task OpenFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.tiff;*.tif|所有文件|*.*",
                Title = "打开图片"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadImageFromPath(dialog.FileName);
            }
        }

        [RelayCommand]
        private async Task OpenFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择图片文件夹",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await LoadFolder(dialog.SelectedPath);
            }
        }

        [RelayCommand]
        private void GoToFirst()
        {
            if (HasImages)
            {
                CurrentIndex = 0;
            }
        }

        [RelayCommand]
        private void GoToLast()
        {
            if (HasImages)
            {
                CurrentIndex = Images.Count - 1;
            }
        }

        [RelayCommand]
        private void GoPrevious()
        {
            if (CanGoPrevious)
            {
                var step = CurrentViewMode == ViewMode.DoublePage && !CurrentImage?.IsWide == true ? 2 : 1;
                CurrentIndex = Math.Max(0, CurrentIndex - step);
            }
        }

        [RelayCommand]
        private void GoNext()
        {
            if (CanGoNext)
            {
                var step = CurrentViewMode == ViewMode.DoublePage && !CurrentImage?.IsWide == true ? 2 : 1;
                CurrentIndex = Math.Min(Images.Count - 1, CurrentIndex + step);
            }
        }

        [RelayCommand]
        private void GoToIndex(int index)
        {
            if (index >= 0 && index < Images.Count)
            {
                CurrentIndex = index;
            }
        }

        [RelayCommand]
        private void ZoomIn()
        {
            if (IsMangaMode)
            {
                MangaZoomLevel = Math.Min(5.0, MangaZoomLevel * 1.25);
            }
            else
            {
                ZoomLevel = Math.Min(5.0, ZoomLevel * 1.25);
                FitToWindow = false;
            }
        }

        [RelayCommand]
        private void ZoomOut()
        {
            if (IsMangaMode)
            {
                MangaZoomLevel = Math.Max(0.1, MangaZoomLevel / 1.25);
            }
            else
            {
                ZoomLevel = Math.Max(0.1, ZoomLevel / 1.25);
                FitToWindow = false;
            }
        }

        [RelayCommand]
        private void ZoomToFit()
        {
            if (IsMangaMode)
            {
                MangaZoomLevel = 1.0;
            }
            else
            {
                ZoomLevel = 1.0;
                FitToWindow = true;
            }
            PanX = 0;
            PanY = 0;
        }

        [RelayCommand]
        private void ZoomToActual()
        {
            if (IsMangaMode)
            {
                MangaZoomLevel = 1.0;
            }
            else
            {
                ZoomLevel = 1.0;
                FitToWindow = false;
            }
        }

        [RelayCommand]
        private void ToggleFullScreen()
        {
            IsFullScreen = !IsFullScreen;
        }


        // 切换适应窗口/原始尺寸的命令
        [RelayCommand]
        private void ToggleFitToActual()
        {
            if (FitToWindow)
            {
                // 当前是适应窗口，切换到原始尺寸
                ZoomToActual();
            }
            else
            {
                // 当前是原始尺寸，切换到适应窗口
                ZoomToFit();
            }
        }
        [RelayCommand]
        private void ToggleViewMode()
        {
            CurrentViewMode = CurrentViewMode switch
            {
                ViewMode.Single => ViewMode.Manga,
                ViewMode.Manga => ViewMode.DoublePage,
                ViewMode.DoublePage => ViewMode.Single,
                _ => ViewMode.Single
            };

            OnPropertyChanged(nameof(IsDoublePage));
            OnPropertyChanged(nameof(IsMangaMode));
            OnPropertyChanged(nameof(IsSingleMode));

            if (CurrentViewMode == ViewMode.Manga)
            {
                _ = LoadMangaImagesAsync(CancellationToken.None);
            }

            _ = LoadCurrentImage();
        }

        [RelayCommand]
        private void SetViewMode(ViewMode mode)
        {
            CurrentViewMode = mode;
            OnPropertyChanged(nameof(IsDoublePage));
            OnPropertyChanged(nameof(IsMangaMode));
            OnPropertyChanged(nameof(IsSingleMode));

            if (CurrentViewMode == ViewMode.Manga)
            {
                _ = LoadMangaImagesAsync(CancellationToken.None);
            }

            _ = LoadCurrentImage();
        }

        [RelayCommand]
        private void ToggleSlideShow()
        {
            if (IsSlideShowActive)
            {
                StopSlideShow();
            }
            else
            {
                StartSlideShow();
            }
        }

        [RelayCommand]
        private async Task RotateImage(double angle)
        {
            if (DisplayImage != null)
            {
                DisplayImage = _imageService.RotateImage(DisplayImage, angle);
            }
        }

        [RelayCommand]
        private async Task Rotate90()
        {
            // 调用带参数的旋转命令,旋转90度
            await RotateImage(90.0);
        }

        [RelayCommand]
        private void CopyToClipboard()
        {
            if (DisplayImage != null)
            {
                Clipboard.SetImage(DisplayImage);
                StatusMessage = "已复制到剪贴板";
            }
        }

        [RelayCommand]
        private void OpenInExplorer()
        {
            if (CurrentImage != null && File.Exists(CurrentImage.FilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{CurrentImage.FilePath}\"");
            }
        }

        [RelayCommand]
        private void SetAsWallpaper()
        {
            // Implementation would use Windows API
            StatusMessage = "设置壁纸功能暂未实现";
        }

        [RelayCommand]
        private void ToggleBookmark()
        {
            if (CurrentImage != null)
            {
                CurrentImage.IsBookmarked = !CurrentImage.IsBookmarked;

                if (CurrentImage.IsBookmarked)
                {
                    Settings.Bookmarks.Add(new Bookmark
                    {
                        FilePath = CurrentImage.FilePath,
                        Name = CurrentImage.FileName
                    });
                    StatusMessage = "已添加书签";
                }
                else
                {
                    Settings.Bookmarks.RemoveAll(b => b.FilePath == CurrentImage.FilePath);
                    StatusMessage = "已移除书签";
                }

                Settings.Save();
            }
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            Settings.ShowSidebar = !Settings.ShowSidebar;
        }

        [RelayCommand]
        private void ToggleToolbar()
        {
            Settings.ShowToolbar = !Settings.ShowToolbar;
        }

        [RelayCommand]
        private void ToggleStatusBar()
        {
            Settings.ShowStatusBar = !Settings.ShowStatusBar;
        }

        [RelayCommand]
        private async Task DeleteImage()
        {
            if (CurrentImage != null && File.Exists(CurrentImage.FilePath))
            {
                var result = MessageBox.Show(
                    $"确定要删除 {CurrentImage.FileName} 吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var indexToRemove = CurrentIndex;
                        var filePath = CurrentImage.FilePath;

                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            filePath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                        Images.RemoveAt(indexToRemove);

                        if (Images.Count > 0)
                        {
                            try
                            {
                                _suppressIndexChangeHandling = true;
                                CurrentIndex = Math.Min(indexToRemove, Images.Count - 1);
                            }
                            finally
                            {
                                _suppressIndexChangeHandling = false;
                            }

                            await LoadCurrentImage();
                        }
                        else
                        {
                            CurrentImage = null;
                            DisplayImage = null;
                            CurrentIndex = -1;
                            UpdateEmptyState();
                        }

                        StatusMessage = "已删除到回收站";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region Methods

        public async Task LoadImageFromPath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder)) return;

            await LoadFolder(folder);

            var index = Images.ToList().FindIndex(i => i.FilePath == filePath);
            if (index >= 0)
            {
                try
                {
                    _suppressIndexChangeHandling = true;
                    CurrentIndex = index;
                }
                finally
                {
                    _suppressIndexChangeHandling = false;
                }

                await LoadCurrentImage();
            }

            // Add to recent files
            Settings.RecentFiles.Remove(filePath);
            Settings.RecentFiles.Insert(0, filePath);
            if (Settings.RecentFiles.Count > 20)
            {
                Settings.RecentFiles = Settings.RecentFiles.Take(20).ToList();
            }
            Settings.Save();
        }

   
        public async Task LoadFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            ShowEmptyState = false;
            IsBusyLoading = true;
            StatusMessage = "正在扫描文件夹...";

            try
            {
                CurrentFolderPath = folderPath;
                Images.Clear();

                // ✓ 批量添加图片，减少 UI 线程切换
                var imageList = new List<ImageInfo>();
                await Task.Run(() =>
                {
                    imageList.AddRange(_imageService.ScanFolder(folderPath));
                });

                // 一次性添加到 ObservableCollection
                foreach (var img in imageList)
                {
                    Images.Add(img);
                }

                _fileWatcher.WatchFolder(folderPath);

                if (Images.Count > 0)
                {
                    var targetIndex = 0;

                    // Restore last reading position
                    if (Settings.RememberReadingPosition &&
                        Settings.ReadingPositions.TryGetValue(folderPath, out var lastIndex))
                    {
                        targetIndex = Math.Min(lastIndex, Images.Count - 1);
                    }

                    try
                    {
                        _suppressIndexChangeHandling = true;
                        CurrentIndex = targetIndex;
                    }
                    finally
                    {
                        _suppressIndexChangeHandling = false;
                    }

                    // ✓ 加载第一张图后立即放开界面
                    await LoadCurrentImage();

                    // ✓ 提前放开界面，用户可以立即操作
                    IsBusyLoading = false;
                    OnPropertyChanged(nameof(HasImages));
                    OnPropertyChanged(nameof(PositionText));
                    UpdateEmptyState();

                    // ✓ 在后台加载缩略图，不阻塞界面
                    StatusMessage = $"已加载 {Images.Count} 张图片，正在生成缩略图...";
                    _ = LoadThumbnailsAsync(); // 不等待完成
                }
                else
                {
                    StatusMessage = "文件夹中没有图片";
                    IsBusyLoading = false;
                    UpdateEmptyState();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                IsBusyLoading = false;
                UpdateEmptyState();
            }
        }
        private async Task LoadThumbnailsAsync()
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;

            try
            {
                // ✓ 并发加载缩略图，限制同时4个线程
                var tasks = Images.Select(async image =>
                {
                    if (token.IsCancellationRequested) return;
                    if (image.Thumbnail != null) return;

                    await _thumbnailSemaphore.WaitAsync(token);
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        var thumbnail = await _imageService.LoadThumbnailAsync(image, Settings.ThumbnailSize);

                        if (thumbnail != null && !token.IsCancellationRequested)
                        {
                            // 使用 Background 优先级更新 UI，避免阻塞
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                image.Thumbnail = thumbnail;
                            }, DispatcherPriority.Background);
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略单个缩略图加载失败，不影响其他缩略图
                    }
                    finally
                    {
                        _thumbnailSemaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                if (!token.IsCancellationRequested)
                {
                    StatusMessage = $"{Images.Count} 张图片已就绪";
                }
            }
            catch (OperationCanceledException)
            {
                // 加载被取消，正常情况
            }
        }

        private async Task LoadCurrentImage()
        {
            if (CurrentIndex < 0 || CurrentIndex >= Images.Count)
                return;

            var previousDisplay = DisplayImage;
            var previousSecondDisplay = SecondDisplayImage;

            // Update current state
            foreach (var img in Images)
            {
                img.IsCurrent = false;
            }

            CurrentImage = Images[CurrentIndex];
            CurrentImage.IsCurrent = true;
            CurrentImage.IsBookmarked = Settings.Bookmarks.Any(b => b.FilePath == CurrentImage.FilePath);

            IsImageLoading = true;
            StatusMessage = $"正在加载: {CurrentImage.FileName}";

            try
            {
                _preloadCts?.Cancel();
                _preloadCts = new CancellationTokenSource();

                // 在后台加载新图，但不立即替换
                var newDisplay = await _imageService.LoadImageAsync(CurrentImage, null, _preloadCts.Token);

                // 只有成功加载才替换（避免闪烁）
                if (newDisplay != null)
                {
                    DisplayImage = newDisplay;
                }//如果加载失败，保持原有的 DisplayImage 不变

                // 双页模式同理
                if (CurrentViewMode == ViewMode.DoublePage && !CurrentImage.IsWide && CurrentIndex < Images.Count - 1)
                {
                    SecondImage = Images[CurrentIndex + 1];
                    if (!SecondImage.IsWide)
                    {
                        var newSecondDisplay = await _imageService.LoadImageAsync(SecondImage, null, _preloadCts.Token);
                        if (newSecondDisplay != null)
                        {
                            SecondDisplayImage = newSecondDisplay;
                        }
                    }
                    else
                    {
                        SecondImage = null;
                        SecondDisplayImage = null;
                    }
                }
                else
                {
                    SecondImage = null;
                    SecondDisplayImage = null;
                }

                if (IsMangaMode)
                {
                    _ = LoadMangaImagesAsync(_preloadCts.Token);
                }

                // Preload adjacent images
                PreloadAdjacentImages();

                // Save reading position
                if (Settings.RememberReadingPosition && !string.IsNullOrEmpty(CurrentFolderPath))
                {
                    Settings.ReadingPositions[CurrentFolderPath] = CurrentIndex;
                    Settings.Save();
                }

                StatusMessage = $"{CurrentImage.FileName} - {CurrentImage.DimensionsFormatted} - {CurrentImage.FileSizeFormatted}";
            }
            catch (OperationCanceledException)
            {
                // Loading was cancelled
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsImageLoading = false;
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PositionText));
            }
        }

        private void PreloadAdjacentImages()
        {
            if (_preloadCts == null) return;

            var preloadCount = Settings.PreloadCount;
            var imagesToPreload = Images
                .Skip(Math.Max(0, CurrentIndex - preloadCount))
                .Take(preloadCount * 2 + 1)
                .Where(i => i != CurrentImage);

            _imageService.PreloadImages(imagesToPreload, _preloadCts.Token);
        }

        private async Task LoadThumbnails()
        {
            foreach (var image in Images)
            {
                if (image.Thumbnail == null)
                {
                    image.Thumbnail = await _imageService.LoadThumbnailAsync(image, Settings.ThumbnailSize);
                }
            }
        }

        private async Task LoadMangaImagesAsync(CancellationToken cancellationToken)
        {
            var decodeWidth = Math.Max(400, Settings.MangaDecodeWidth);

            foreach (var image in Images)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (image.FullImage != null) continue;

                var full = await _imageService.LoadImageAsync(image, decodeWidth, cancellationToken);
                if (full != null)
                {
                    image.FullImage = full;
                }
            }
        }

        public async Task HandleFileDrop(string[] files)
        {
            if (files.Length == 0) return;

            var first = files[0];

            if (Directory.Exists(first))
            {
                await LoadFolder(first);
            }
            else if (File.Exists(first) && ImageService.IsSupportedImage(first))
            {
                await LoadImageFromPath(first);
            }
        }

        private void UpdateEmptyState()
        {
            ShowEmptyState = !IsBusyLoading && !HasImages;
        }

        public void HandleMouseWheel(int delta, bool ctrlPressed)
        {
            if (Settings.ScrollWheelBehavior == ScrollWheelBehavior.Zoom || ctrlPressed)
            {
                if (delta < 0)
                    ZoomIn();
                else
                    ZoomOut();
            }
            else if (CurrentViewMode == ViewMode.Single || CurrentViewMode == ViewMode.DoublePage)
            {
                if (delta > 0)
                    GoPrevious();
                else
                    GoNext();
            }
        }

        private void StartSlideShow()
        {
            if (!HasImages)
                return;

            IsSlideShowActive = true;

            _slideshowTimer.Interval = TimeSpan.FromSeconds(Settings.SlideshowInterval);
            _slideshowTimer.Start();
            StatusMessage = "幻灯片播放中...";

            if (!IsFullScreen)
            {
                IsFullScreen = true;
            }
        }

        private void StopSlideShow()
        {
            if (!IsSlideShowActive && !_slideshowTimer.IsEnabled)
                return;

            _slideshowTimer.Stop();
            IsSlideShowActive = false;
            StatusMessage = "幻灯片已停止";
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (CanGoNext)
            {
                GoNext();
            }
            else
            {
                GoToFirst();
            }
        }

        private void OnFileCreated(object? sender, FileSystemEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                var newImage = ImageInfo.FromFile(e.FullPath);

                // Find correct position for natural sort
                var index = 0;
                var comparer = new NaturalStringComparer();
                while (index < Images.Count && comparer.Compare(Images[index].FilePath, e.FullPath) < 0)
                {
                    index++;
                }

                Images.Insert(index, newImage);
                newImage.Thumbnail = await _imageService.LoadThumbnailAsync(newImage, Settings.ThumbnailSize);

                OnPropertyChanged(nameof(HasImages));
                OnPropertyChanged(nameof(PositionText));
                UpdateEmptyState();
                StatusMessage = $"已添加: {newImage.FileName}";
            });
        }

        private void OnFileDeleted(object? sender, FileSystemEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var image = Images.FirstOrDefault(i => i.FilePath == e.FullPath);
                if (image != null)
                {
                    var wasCurrentIndex = Images.IndexOf(image);
                    Images.Remove(image);

                    if (wasCurrentIndex == CurrentIndex && Images.Count > 0)
                    {
                        try
                        {
                            _suppressIndexChangeHandling = true;
                            CurrentIndex = Math.Min(wasCurrentIndex, Images.Count - 1);
                        }
                        finally
                        {
                            _suppressIndexChangeHandling = false;
                        }

                        _ = LoadCurrentImage();
                    }
                    else if (Images.Count == 0)
                    {
                        CurrentImage = null;
                        DisplayImage = null;
                        CurrentIndex = -1;
                    }

                    OnPropertyChanged(nameof(HasImages));
                    OnPropertyChanged(nameof(PositionText));
                    UpdateEmptyState();
                    StatusMessage = $"文件已删除: {Path.GetFileName(e.FullPath)}";
                }
            });
        }

        private void OnFileRenamed(object? sender, RenamedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var image = Images.FirstOrDefault(i => i.FilePath == e.OldFullPath);
                if (image != null)
                {
                    // Update the image info
                    var newImage = ImageInfo.FromFile(e.FullPath);
                    newImage.Thumbnail = image.Thumbnail;
                    newImage.IsBookmarked = image.IsBookmarked;
                    newImage.IsCurrent = image.IsCurrent;

                    var index = Images.IndexOf(image);
                    Images[index] = newImage;

                    if (CurrentIndex == index)
                    {
                        CurrentImage = newImage;
                    }

                    StatusMessage = $"文件已重命名: {newImage.FileName}";
                }
            });
        }

        public void SaveSettings()
        {
            Settings.Save();
        }
        public void Dispose()
        {
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();

            // ✓ 添加这三行：取消和释放缩略图加载资源
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _thumbnailSemaphore?.Dispose();

            _slideshowTimer.Stop();
            _imageService.Dispose();
            _fileWatcher.Dispose();
            Settings.Save();
        }

        #endregion
    }
}
