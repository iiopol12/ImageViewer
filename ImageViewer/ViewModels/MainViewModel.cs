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

    /// <summary>
    /// 主视图模型 - 处理图片查看器的核心业务逻辑
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ImageService _imageService;// 图片加载和缓存服务
        private readonly FileWatcherService _fileWatcher; // 文件系统监视服务
        private readonly DispatcherTimer _slideshowTimer;// 幻灯片播放定时器

        // === 取消令牌 ===
        private CancellationTokenSource? _preloadCts;   // 预加载取消令牌
        private CancellationTokenSource? _thumbnailCts; // 缩略图加载取消令牌

        // === 状态标志 ===
        private bool _suppressIndexChangeHandling;  // 抑制索引变化处理标志

        // === 并发控制 ===
        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(4); // 限制并发数为4

        public MainViewModel()
        {
            _imageService = new ImageService();
            _fileWatcher = new FileWatcherService();
            _slideshowTimer = new DispatcherTimer();
            _slideshowTimer.Tick += SlideshowTimer_Tick;

            // 加载应用设置
            Settings = AppSettings.Load();
            CurrentViewMode = Settings.DefaultViewMode;

            // 订阅文件系统事件
            _fileWatcher.FileCreated += OnFileCreated;
            _fileWatcher.FileDeleted += OnFileDeleted;
            _fileWatcher.FileRenamed += OnFileRenamed;
        }

        #region 属性定义
        // === 应用设置 ===
        [ObservableProperty]
        private AppSettings _settings = new();
        // === 图片集合 ===
        [ObservableProperty]
        private ObservableCollection<ImageInfo> _images = new();
        // === 当前图片信息 ===
        [ObservableProperty]
        private ImageInfo? _currentImage;
        // === 第二张图片（双页模式使用） ===
        [ObservableProperty]
        private ImageInfo? _secondImage;
        // === 显示的图片位图 ===
        [ObservableProperty]
        private BitmapSource? _displayImage;
        // === 第二张显示的图片位图（双页模式） ===
        [ObservableProperty]
        private BitmapSource? _secondDisplayImage;
        // === 当前图片索引 ===
        [ObservableProperty]
        private int _currentIndex = -1;
        // === 当前文件夹路径 ===
        [ObservableProperty]
        private string _currentFolderPath = string.Empty;
        // === 当前查看模式 ===
        [ObservableProperty]
        private ViewMode _currentViewMode = ViewMode.Single;
        // === 缩放级别 ===
        [ObservableProperty]
        private double _zoomLevel = 1.0;
        // === 漫画模式缩放级别 ===
        [ObservableProperty]
        private double _mangaZoomLevel = 1.0;
        // === 平移 X 坐标 ===
        [ObservableProperty]
        private double _panX;
        // === 平移 Y 坐标 ===
        [ObservableProperty]
        private double _panY;
        // === 是否全屏 ===
        [ObservableProperty]
        private bool _isFullScreen;
        // === 幻灯片是否激活 ===
        [ObservableProperty]
        private bool _isSlideShowActive;
        // === 是否正在繁忙加载 ===
        [ObservableProperty]
        private bool _isBusyLoading;

        // === 是否正在加载图片 ===
        [ObservableProperty]
        private bool _isImageLoading;
        // === 状态栏消息 ===
        [ObservableProperty]
        private string _statusMessage = "就绪";
        // === 是否适应窗口 ===
        [ObservableProperty]
        private bool _fitToWindow = true;
        // === 滚动偏移量（漫画模式使用） ===
        [ObservableProperty]
        private double _scrollOffset;
        // === 是否显示空状态 ===
        [ObservableProperty]
        private bool _showEmptyState = true;
        /// <summary>位置文本 - 显示当前图片位置</summary>
        public string PositionText => Images.Count > 0 && CurrentIndex >= 0
            ? $"第 {CurrentIndex + 1}/{Images.Count} 张"
            : "无图片";
        /// <summary>是否有图片</summary>
        public bool HasImages => Images.Count > 0;
        /// <summary>是否可以前往上一张</summary>
        public bool CanGoPrevious => CurrentIndex > 0;
        /// <summary>是否可以前往下一张</summary>
        public bool CanGoNext => CurrentIndex < Images.Count - 1;

        /// <summary>是否双页模式</summary>
        public bool IsDoublePage => CurrentViewMode == ViewMode.DoublePage;

        /// <summary>是否漫画模式</summary>
        public bool IsMangaMode => CurrentViewMode == ViewMode.Manga;


        /// <summary>是否单图模式</summary>
        public bool IsSingleMode => CurrentViewMode == ViewMode.Single;

        /// <summary>当前激活的缩放级别</summary>
        public double ActiveZoomLevel => IsMangaMode ? MangaZoomLevel : ZoomLevel;


        /// <summary>缩放级别变化时触发</summary>
        partial void OnZoomLevelChanged(double value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }

        /// <summary>漫画模式缩放级别变化时触发</summary>
        partial void OnMangaZoomLevelChanged(double value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }
        /// <summary>当前查看模式变化时触发</summary>
        partial void OnCurrentViewModeChanged(ViewMode value)
        {
            OnPropertyChanged(nameof(ActiveZoomLevel));
        }
        /// <summary>当前索引变化时触发 - 加载对应图片</summary>
        partial void OnCurrentIndexChanged(int value)
        {
            // 如果抑制处理标志为真，则跳过
            if (_suppressIndexChangeHandling)
                return;
            // 检查索引有效性
            if (value < 0 || value >= Images.Count)
                return;
            // 异步加载当前图片
            _ = LoadCurrentImage();
        }

        /// <summary>全屏状态变化时触发</summary>
        partial void OnIsFullScreenChanged(bool value)
        {
            // 退出全屏时停止幻灯片
            if (!value)
            {
                StopSlideShow();
            }
        }

        #endregion

        #region 命令

        /// <summary>打开文件命令</summary>
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
        /// <summary>打开文件夹命令</summary>
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


        /// <summary>跳转到第一张命令</summary>
        [RelayCommand]
        private void GoToFirst()
        {
            if (HasImages)
            {
                CurrentIndex = 0;
            }
        }


        /// <summary>跳转到最后一张命令</summary>
        [RelayCommand]
        private void GoToLast()
        {
            if (HasImages)
            {
                CurrentIndex = Images.Count - 1;
            }
        }


        /// <summary>前往上一张命令</summary>
        [RelayCommand]
        private void GoPrevious()
        {
            if (CanGoPrevious)
            {
                // 双页模式下，如果当前不是宽图，则跳转2张
                var step = CurrentViewMode == ViewMode.DoublePage && !CurrentImage?.IsWide == true ? 2 : 1;
                CurrentIndex = Math.Max(0, CurrentIndex - step);
            }
        }


        /// <summary>前往下一张命令</summary>
        [RelayCommand]
        private void GoNext()
        {
            if (CanGoNext)
            {
                // 双页模式下，如果当前不是宽图，则跳转2张
                var step = CurrentViewMode == ViewMode.DoublePage && !CurrentImage?.IsWide == true ? 2 : 1;
                CurrentIndex = Math.Min(Images.Count - 1, CurrentIndex + step);
            }
        }

        /// <summary>跳转到指定索引命令</summary>
        [RelayCommand]
        private void GoToIndex(int index)
        {
            if (index >= 0 && index < Images.Count)
            {
                CurrentIndex = index;
            }
        }


        /// <summary>放大命令</summary>
        [RelayCommand]
        private void ZoomIn()
        {
            // 漫画模式：最大 5 倍
            if (IsMangaMode)
            {
                MangaZoomLevel = Math.Min(5.0, MangaZoomLevel * 1.25);
            }
            else
            {
                // 普通模式：最大 5 倍，禁用适应窗口
                ZoomLevel = Math.Min(5.0, ZoomLevel * 1.25);
                FitToWindow = false;
            }
        }
        /// <summary>缩小命令</summary>
        [RelayCommand]
        private void ZoomOut()
        {
            if (IsMangaMode)
            {
                // 漫画模式：最小 0.1 倍
                MangaZoomLevel = Math.Max(0.1, MangaZoomLevel / 1.25);
            }
            else
            { // 普通模式：最小 0.1 倍，禁用适应窗口
                ZoomLevel = Math.Max(0.1, ZoomLevel / 1.25);
                FitToWindow = false;
            }
        }

        /// <summary>适应窗口命令</summary>
        [RelayCommand]
        private void ZoomToFit()
        {
            if (IsMangaMode)
            {
                // 漫画模式重置为 1.0
                MangaZoomLevel = 1.0;
            }
            else
            {
                // 普通模式启用适应窗口
                ZoomLevel = 1.0;
                FitToWindow = true;
            }
            PanX = 0;
            PanY = 0;
        }


        /// <summary>实际尺寸命令（100%）</summary>
        [RelayCommand]
        private void ZoomToActual()
        {
            if (IsMangaMode)
            {
                // 漫画模式设为 1.0
                MangaZoomLevel = 1.0;
            }
            else
            {
                // 普通模式设为 1.0，禁用适应窗口
                ZoomLevel = 1.0;
                FitToWindow = false;
            }
        }
        /// <summary>切换全屏命令</summary>
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


        /// <summary>切换查看模式命令</summary>
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
            // 漫画模式需要加载所有图片
            if (CurrentViewMode == ViewMode.Manga)
            {

                // 重新加载当前图片以适应新模式
                _ = LoadMangaModeImages();
            }
            //加载现在图片
            _ = LoadCurrentImage();

            StatusMessage = $"已切换至 {CurrentViewMode switch
            {
                ViewMode.Single => "单图模式",
                ViewMode.Manga => "漫画模式",
                ViewMode.DoublePage => "双页模式",
                _ => "未知模式"
            }}";
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
        /// <summary>切换幻灯片命令</summary>
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
        /// <summary>旋转90度命令</summary>
        [RelayCommand]
        private async Task RotateImage(double angle)
        {
            if (DisplayImage != null)
            {
                DisplayImage = _imageService.RotateImage(DisplayImage, angle);
            }
        }
        /// <summary>旋转90度命令</summary>
        [RelayCommand]
        private async Task Rotate90()
        {
            // 调用带参数的旋转命令,旋转90度
            await RotateImage(90.0);
        }



        /// <summary>复制到剪贴板命令</summary>
        [RelayCommand]
        private void CopyToClipboard()
        {
            if (DisplayImage != null)
            {
                Clipboard.SetImage(DisplayImage);
                StatusMessage = "已复制到剪贴板";
            }
        }


        /// <summary>在资源管理器中显示命令</summary>
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


        /// <summary>切换书签命令</summary>
        [RelayCommand]
        private void ToggleBookmark()
        {
            if (CurrentImage != null)
            {
                CurrentImage.IsBookmarked = !CurrentImage.IsBookmarked;

                if (CurrentImage.IsBookmarked)
                {
                    // 添加书签
                    Settings.Bookmarks.Add(new Bookmark
                    {
                        FilePath = CurrentImage.FilePath,
                        Name = CurrentImage.FileName
                    });
                    StatusMessage = "已添加书签";
                }
                else
                {
                    // 移除书签
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


        /// <summary>删除图片命令</summary>
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

        #region  图片加载


        /// <summary>
        /// 从文件路径加载图片
        /// </summary>
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
            // 查找并跳转到该图片
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

        /// <summary>
        /// 加载文件夹中的所有图片
        /// </summary>
        public async Task LoadFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

          
            try
            {

               
                IsBusyLoading = true;
                StatusMessage = "正在扫描文件夹...";

                // 取消之前的加载任务
                _preloadCts?.Cancel();
                _preloadCts = new CancellationTokenSource();

                // 停止文件监视
                _fileWatcher.StopWatching();
                // 清空当前列表
                Images.Clear();
                CurrentImage = null;
                DisplayImage = null;
                SecondImage = null;
                SecondDisplayImage = null;
                CurrentIndex = -1;

               

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


                CurrentFolderPath = folderPath;

                // 启动文件监视
                _fileWatcher.WatchFolder(folderPath);

                if (Images.Count > 0)
                {
                    var targetIndex = 0;

                    // 恢复上次浏览位置
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

                    // 在后台加载缩略图
                    _ = LoadThumbnailsAsync(); // 不等待完成

                    // 将文件夹添加到最近打开列表
                    if (!Settings.RecentFiles.Contains(folderPath))
                    {
                        Settings.RecentFiles.Insert(0, folderPath);
                        if (Settings.RecentFiles.Count > 10)
                        {
                            Settings.RecentFiles.RemoveAt(Settings.RecentFiles.Count - 1);
                        }
                        Settings.Save();
                    }
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


        /// <summary>
        /// 异步加载所有缩略图
        /// </summary>
        private async Task LoadThumbnailsAsync()
        {


            // 取消之前的缩略图加载
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


        /// <summary>
        /// 加载当前图片
        /// </summary>
        private async Task LoadCurrentImage()
        {
            if (CurrentIndex < 0 || CurrentIndex >= Images.Count)
                return;
     
            try
            {

   
                IsImageLoading = true;
               
                // 取消之前的加载
                _preloadCts?.Cancel();
                _preloadCts = new CancellationTokenSource();

                // 更新所有图片的当前状态
                foreach (var img in Images)
                {
                    img.IsCurrent = false;
                }
                CurrentImage = Images[CurrentIndex];
                CurrentImage.IsCurrent = true;

                // 检查书签状态
                CurrentImage.IsBookmarked = Settings.Bookmarks.Any(b => b.FilePath == CurrentImage.FilePath);


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
                    // 如果当前图不是宽图，尝试加载下一张
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
                { // 单张宽图或单图模式
                    SecondImage = null;
                    SecondDisplayImage = null;
                }
                // 漫画模式
                if (IsMangaMode)
                {
                    _ = LoadMangaImagesAsync(_preloadCts.Token);
                }

                // 预加载相邻图片
                PreloadAdjacentImages();

                // 保存阅读位置
                if (Settings.RememberReadingPosition && !string.IsNullOrEmpty(CurrentFolderPath))
                {
                    Settings.ReadingPositions[CurrentFolderPath] = CurrentIndex;
                    Settings.Save();
                }
                // 更新状态栏
                StatusMessage = $"{CurrentImage.FileName} - {CurrentImage.DimensionsFormatted} - {CurrentImage.FileSizeFormatted}";
            }
            catch (OperationCanceledException)
            {
                // 加载被取消
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

        /// <summary>
        /// 预加载相邻图片
        /// </summary>
        private void PreloadAdjacentImages()
        {
            if (_preloadCts == null) return;

            var preloadCount = Settings.PreloadCount;

            // 选择需要预加载的图片（当前图片前后各 PreloadCount 张）
            var imagesToPreload = Images
                .Skip(Math.Max(0, CurrentIndex - preloadCount))
                .Take(preloadCount * 2 + 1)
                .Where(i => i != CurrentImage);
            // 异步预加载
            _imageService.PreloadImages(imagesToPreload, _preloadCts.Token);
        }


        /// <summary>
        /// 加载漫画模式的所有图片
        /// </summary>
        private async Task LoadMangaModeImages()
        {
            try
            {
                IsBusyLoading = true;
                StatusMessage = "正在加载漫画模式...";

                // 取消之前的加载
                _preloadCts?.Cancel();
                _preloadCts = new CancellationTokenSource();

                // 异步加载所有图片
                await LoadMangaImagesAsync(_preloadCts.Token);

                StatusMessage = "漫画模式已就绪";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "加载已取消";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsBusyLoading = false;
            }
        }

        /// <summary>
        /// 加载漫画模式的图片
        /// </summary>
        private async Task LoadMangaImagesAsync(CancellationToken cancellationToken)
        {
            var decodeWidth = Math.Max(400, Settings.MangaDecodeWidth);

            foreach (var image in Images)
            {
              
                if (cancellationToken.IsCancellationRequested) break;
                if (image.FullImage != null) continue;
                // 异步加载所有图片
                var full = await _imageService.LoadImageAsync(image, decodeWidth, cancellationToken);
                if (full != null)
                {
                    image.FullImage = full;
                }
            }
        }
        /// <summary>
        /// 处理拖放的文件
        /// </summary>
        public async Task HandleFileDrop(string[] files)
        {
            if (files.Length == 0) return;

            var first = files[0];

            if (Directory.Exists(first))
            {
                // 拖入的是文件夹
                await LoadFolder(first);
            }
            else if (File.Exists(first) && ImageService.IsSupportedImage(first))
            {
                // 拖入的是图片文件
                await LoadImageFromPath(first);
            }
        }


        /// <summary>
        /// 更新空状态显示
        /// </summary>
        private void UpdateEmptyState()
        {
            ShowEmptyState = !IsBusyLoading && !HasImages;
        }
        /// <summary>
        /// 处理鼠标滚轮事件
        /// </summary>
        public void HandleMouseWheel(int delta, bool ctrlPressed)
        {

            // 滚轮行为：缩放或导航
            if (Settings.ScrollWheelBehavior == ScrollWheelBehavior.Zoom || ctrlPressed)
            {
                // 缩放模式
                if (delta < 0)
                    ZoomIn();
                else
                    ZoomOut();
            }
            else if (CurrentViewMode == ViewMode.Single || CurrentViewMode == ViewMode.DoublePage)
            {
                // 导航模式（仅在单图和双页模式下）
                if (delta > 0)
                    GoPrevious();
                else
                    GoNext();
            }
        }
        /// <summary>
        /// 开始幻灯片播放
        /// </summary>
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
        /// <summary>
        /// 停止幻灯片播放
        /// </summary>
        private void StopSlideShow()
        {
            if (!IsSlideShowActive && !_slideshowTimer.IsEnabled)
                return;

            _slideshowTimer.Stop();
            IsSlideShowActive = false;
            StatusMessage = "幻灯片已停止";
        }


        /// <summary>
        /// 幻灯片定时器触发
        /// </summary>
        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (CanGoNext)
            {// 播放下一张
                GoNext();
            }
            else
            {// 到达末尾，回到开头循环播放
                GoToFirst();
            }
        }
        /// <summary>
        /// 文件创建事件 - 新图片添加到文件夹
        /// </summary>
        private void OnFileCreated(object? sender, FileSystemEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                var newImage = ImageInfo.FromFile(e.FullPath);

                // 使用自然排序查找正确的插入位置
                var index = 0;
                var comparer = new NaturalStringComparer();
                while (index < Images.Count && comparer.Compare(Images[index].FilePath, e.FullPath) < 0)
                {
                    index++;
                }
                // 插入到正确位置
                Images.Insert(index, newImage);

                // 加载缩略图
                newImage.Thumbnail = await _imageService.LoadThumbnailAsync(newImage, Settings.ThumbnailSize);
                // 更新 UI
                OnPropertyChanged(nameof(HasImages));
                OnPropertyChanged(nameof(PositionText));
                UpdateEmptyState();
                StatusMessage = $"已添加: {newImage.FileName}";
            });
        }


        /// <summary>
        /// 文件删除事件 - 图片从文件夹中删除
        /// </summary>
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
        /// <summary>
        /// 文件重命名事件
        /// </summary>
        private void OnFileRenamed(object? sender, RenamedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var image = Images.FirstOrDefault(i => i.FilePath == e.OldFullPath);
                if (image != null)
                {
                    // 创建新的图片信息
                    var newImage = ImageInfo.FromFile(e.FullPath);
                    newImage.Thumbnail = image.Thumbnail;
                    newImage.IsBookmarked = image.IsBookmarked;
                    newImage.IsCurrent = image.IsCurrent;
                    // 替换旧的
                    var index = Images.IndexOf(image);
                    Images[index] = newImage;


                    // 更新当前图片引用
                    if (CurrentIndex == index)
                    {
                        CurrentImage = newImage;
                    }

                    StatusMessage = $"文件已重命名: {newImage.FileName}";
                }
            });
        }
        /// <summary>
        /// 保存设置
        /// </summary>
        public void SaveSettings()
        {
            Settings.Save();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {

            // 取消并释放预加载资源
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();

            // 取消并释放缩略图加载资源
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _thumbnailSemaphore?.Dispose();
            // 停止定时器
            _slideshowTimer.Stop();

            // 释放服务
            _imageService.Dispose();
            _fileWatcher.Dispose();

            // 保存设置
            Settings.Save();
        }

        #endregion
    }
}
