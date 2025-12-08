using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ImageViewer.Models;
using ImageViewer.ViewModels;
using ImageViewer.Helpers;

namespace ImageViewer.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private Point _lastMousePosition;
        private bool _isDragging;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;
        private bool _previousShowStatusBar;
        private bool _previousShowSidebar;

        private bool _hasRestoredWindowPlacement;

        // 全局快捷键管理器
        private HotKeyManager _hotKeyManager;
        private readonly DispatcherTimer _cursorHideTimer;
        private bool _isCursorHidden;
        private readonly TimeSpan _cursorHideDelay = TimeSpan.FromSeconds(3);

        public MainWindow()
        {
            InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 初始化快捷键管理器
            _hotKeyManager = new HotKeyManager(this);

            // 初始化鼠标隐藏计时器（用于幻灯片模式）
            _cursorHideTimer = new DispatcherTimer
            {
                Interval = _cursorHideDelay
            };
            _cursorHideTimer.Tick += CursorHideTimer_Tick;

            // Restore window position and size
            Loaded += async (s, e) =>
            {
                // 注册全局快捷键
                RegisterGlobalHotKeys();

                FadeInWindow();

                // Load startup file if provided
                if (Application.Current.Properties["StartupFile"] is string filePath)
                {
                    await ViewModel.LoadImageFromPath(filePath);
                }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (_hasRestoredWindowPlacement)
                return;

            RestoreWindowPlacement();
            _hasRestoredWindowPlacement = true;
        }

        private void RestoreWindowPlacement()
        {
            if (!ViewModel.Settings.RememberWindowPosition)
                return;

            if (ViewModel.Settings.WindowWidth > 0)
                Width = ViewModel.Settings.WindowWidth;
            if (ViewModel.Settings.WindowHeight > 0)
                Height = ViewModel.Settings.WindowHeight;
            if (ViewModel.Settings.WindowLeft >= 0)
                Left = ViewModel.Settings.WindowLeft;
            if (ViewModel.Settings.WindowTop >= 0)
                Top = ViewModel.Settings.WindowTop;
            if (ViewModel.Settings.IsMaximized)
                WindowState = WindowState.Maximized;
        }

        private void FadeInWindow()
        {
            const double durationMs = 140;

            BeginAnimation(OpacityProperty, null);
            var animation = new DoubleAnimation
            {
                From = Opacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase()
            };
            BeginAnimation(OpacityProperty, animation);
        }

        #region 全局快捷键注册

        /// <summary>
        /// 注册全局快捷键 - 替代 XAML 中的 InputBindings
        /// </summary>
        private void RegisterGlobalHotKeys()
        {
            // 先清理旧的，再注册当前窗口激活时需要的快捷键，避免失去焦点后仍抢占按键
            _hotKeyManager?.UnregisterAll();

            try
            {
                // === 文件操作 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.O,
                    () => ViewModel.OpenFileCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.F,
                    () => ViewModel.OpenFolderCommand?.Execute(null));

                // === 导航 - 方向键 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Left,
                    () => ViewModel.GoPreviousCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Right,
                    () => ViewModel.GoNextCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Up,
                    () => ViewModel.GoPreviousCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Down,
                    () => ViewModel.GoNextCommand?.Execute(null));

                // === 导航 - 字母键 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.A,
                    () => ViewModel.GoPreviousCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.D,
                    () => ViewModel.GoNextCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Space,
                    () => ViewModel.GoNextCommand?.Execute(null));

                // === 导航 - 翻页键 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Home,
                    () => ViewModel.GoToFirstCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.End,
                    () => ViewModel.GoToLastCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.PageUp,
                    () => ViewModel.GoPreviousCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.PageDown,
                    () => ViewModel.GoNextCommand?.Execute(null));

                // === 视图控制 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.F11,
                    () => ViewModel.ToggleFullScreenCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.M,
                    () => ViewModel.ToggleViewModeCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.B,
                    () => ViewModel.ToggleBookmarkCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.S,
                    () => ViewModel.ToggleSlideShowCommand?.Execute(null));

                // === 缩放控制 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Add,
                    () => ViewModel.ZoomInCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.OemPlus,
                    () => ViewModel.ZoomInCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Subtract,
                    () => ViewModel.ZoomOutCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.OemMinus,
                    () => ViewModel.ZoomOutCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.D0,
                    () => ViewModel.ZoomToFitCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.D1,
                    () => ViewModel.ZoomToActualCommand?.Execute(null));

                // === 其他操作 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.C,
                    () => ViewModel.CopyToClipboardCommand?.Execute(null));

                _hotKeyManager.RegisterHotKey(ModifierKeys.None, Key.Delete,
                    () => ViewModel.DeleteImageCommand?.Execute(null));

                System.Diagnostics.Debug.WriteLine("✓ 全局快捷键注册完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 注册全局快捷键失败: {ex.Message}");
            }
        }

        #endregion

        #region Window Chrome Events

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeButton.Content = "☐";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeButton.Content = "❐";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Window Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Additional initialization
            ScheduleFitToWindowUpdate();
        }

        private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(ViewModel.Settings)
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ViewModel.IsFullScreen)
            {
                ExitFullScreen();
            }

            // 注销所有快捷键
            _hotKeyManager?.Dispose();

            // Save window state
            if (ViewModel.Settings.RememberWindowPosition)
            {
                ViewModel.Settings.WindowWidth = Width;
                ViewModel.Settings.WindowHeight = Height;
                ViewModel.Settings.WindowLeft = Left;
                ViewModel.Settings.WindowTop = Top;
                ViewModel.Settings.IsMaximized = WindowState == WindowState.Maximized;
            }

            ViewModel.SaveSettings();
            ViewModel.Dispose();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MaximizeButton.Content = "❐";
            }
            else
            {
                MaximizeButton.Content = "☐";
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // 只保留 Escape 键的特殊处理
            // 其他快捷键已通过 HotKeyManager 处理
            if (e.Key == Key.Escape)
            {
                if (ViewModel.IsFullScreen)
                {
                    ViewModel.IsFullScreen = false;
                    e.Handled = true;
                }
                else if (ViewModel.IsSlideShowActive)
                {
                    ViewModel.ToggleSlideShowCommand.Execute(null);
                    e.Handled = true;
                }
                else
                {
                    Close();
                }
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // 窗口激活时重新注册快捷键，避免在后台抢占其它软件按键
            RegisterGlobalHotKeys();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 窗口失焦时释放全局热键
            _hotKeyManager?.UnregisterAll();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            // 移动鼠标时立即显示，并在幻灯片模式下重启隐藏计时器
            ShowCursor();

            if (ViewModel.IsSlideShowActive)
            {
                RestartCursorHideTimer();
            }
            else
            {
                _cursorHideTimer.Stop();
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 在隧道路由阶段优先处理 Ctrl + 滚轮缩放，防止 ScrollViewer 抢占事件
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            if (ViewModel.IsMangaMode)
            {
                HandleMangaZoomWithMouseWheel(e);
            }
            else
            {
                HandleZoomWithMouseWheel(e);
            }

            e.Handled = true;
        }

        private void CursorHideTimer_Tick(object? sender, EventArgs e)
        {
            if (!ViewModel.IsSlideShowActive)
            {
                _cursorHideTimer.Stop();
                ShowCursor();
                return;
            }

            HideCursor();
        }

        private void RestartCursorHideTimer()
        {
            _cursorHideTimer.Stop();
            _cursorHideTimer.Start();
        }

        private void HideCursor()
        {
            if (_isCursorHidden)
                return;

            Mouse.OverrideCursor = Cursors.None;
            _isCursorHidden = true;
        }

        private void ShowCursor()
        {
            if (!_isCursorHidden)
                return;

            Mouse.OverrideCursor = null;
            _isCursorHidden = false;
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool forceZoom = ctrlPressed || ViewModel.Settings.ScrollWheelBehavior == ScrollWheelBehavior.Zoom;

            if (forceZoom)
            {
                // Ctrl + 滚轮 或设置为缩放模式时进行缩放
                if (ViewModel.IsMangaMode)
                {
                    HandleMangaZoomWithMouseWheel(e);
                }
                else
                {
                    HandleZoomWithMouseWheel(e);
                }
                e.Handled = true; // 阻止滚动查看器处理事件
            }
            else
            {
                // 默认行为:导航到上一张/下一张
                ViewModel.HandleMouseWheel(e.Delta, ctrlPressed);
            }
        }

        /// <summary>
        /// 处理 Ctrl + 鼠标滚轮缩放
        /// </summary>
        private void HandleZoomWithMouseWheel(MouseWheelEventArgs e)
        {
            const double ZOOM_FACTOR = 0.1; // 每次缩放 10%
            const double MIN_ZOOM = 0.1;    // 最小缩放 10%
            const double MAX_ZOOM = 10.0;   // 最大缩放 1000%

            if (ViewModel.DisplayImage == null)
                return;

            // 获取鼠标位置相对于图片容器的坐标
            Point mousePos = e.GetPosition(ImageScrollViewer);

            // 计算滚动条在视口中的位置比例(缩放前)
            double horizontalRatio = 0;
            double verticalRatio = 0;

            if (ImageScrollViewer.ViewportWidth > 0 && ImageScrollViewer.ViewportHeight > 0)
            {
                horizontalRatio = (ImageScrollViewer.HorizontalOffset + mousePos.X) / ImageScrollViewer.ExtentWidth;
                verticalRatio = (ImageScrollViewer.VerticalOffset + mousePos.Y) / ImageScrollViewer.ExtentHeight;
            }

            // 计算新的缩放级别
            double currentZoom = ViewModel.ZoomLevel;
            double delta = e.Delta < 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
            double newZoom = currentZoom * (1 + delta);

            // 限制缩放范围
            newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, newZoom));

            // 如果缩放级别没有变化,直接返回
            if (Math.Abs(newZoom - currentZoom) < 0.001)
                return;

            // 应用新的缩放级别
            ViewModel.FitToWindow = false;
            ViewModel.ZoomLevel = newZoom;

            // 延迟调整滚动位置以保持鼠标位置下的内容不变
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ImageScrollViewer.ExtentWidth > 0 && ImageScrollViewer.ExtentHeight > 0)
                {
                    // 根据之前的位置比例计算新的滚动位置
                    double newHorizontalOffset = horizontalRatio * ImageScrollViewer.ExtentWidth - mousePos.X;
                    double newVerticalOffset = verticalRatio * ImageScrollViewer.ExtentHeight - mousePos.Y;

                    // 确保滚动位置在有效范围内
                    newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, ImageScrollViewer.ScrollableWidth));
                    newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, ImageScrollViewer.ScrollableHeight));

                    ImageScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                    ImageScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 漫画模式下的滚轮缩放(支持 Ctrl + 滚轮)
        /// </summary>
        private void HandleMangaZoomWithMouseWheel(MouseWheelEventArgs e)
        {
            const double ZOOM_FACTOR = 0.1;
            const double MIN_ZOOM = 0.1;
            const double MAX_ZOOM = 10.0;

            if (ViewModel.Images.Count == 0)
                return;

            Point mousePos = e.GetPosition(MangaScrollViewer);

            double horizontalRatio = 0;
            double verticalRatio = 0;

            if (MangaScrollViewer.ViewportWidth > 0 && MangaScrollViewer.ViewportHeight > 0)
            {
                horizontalRatio = (MangaScrollViewer.HorizontalOffset + mousePos.X) / MangaScrollViewer.ExtentWidth;
                verticalRatio = (MangaScrollViewer.VerticalOffset + mousePos.Y) / MangaScrollViewer.ExtentHeight;
            }

            double currentZoom = ViewModel.MangaZoomLevel;
            double delta = e.Delta < 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
            double newZoom = currentZoom * (1 + delta);

            newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, newZoom));

            if (Math.Abs(newZoom - currentZoom) < 0.001)
                return;

            ViewModel.MangaZoomLevel = newZoom;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MangaScrollViewer.ExtentWidth > 0 && MangaScrollViewer.ExtentHeight > 0)
                {
                    double newHorizontalOffset = horizontalRatio * MangaScrollViewer.ExtentWidth - mousePos.X;
                    double newVerticalOffset = verticalRatio * MangaScrollViewer.ExtentHeight - mousePos.Y;

                    newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, MangaScrollViewer.ScrollableWidth));
                    newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, MangaScrollViewer.ScrollableHeight));

                    MangaScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                    MangaScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
            }), DispatcherPriority.Loaded);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                await ViewModel.HandleFileDrop(files);
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        #endregion

        #region Full Screen

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsFullScreen):
                    HandleFullScreenChange();
                    break;
                case nameof(MainViewModel.DisplayImage):
                case nameof(MainViewModel.SecondDisplayImage):
                    StartImageFadeIn();
                    goto case nameof(MainViewModel.FitToWindow);
                case nameof(MainViewModel.CurrentViewMode):
                case nameof(MainViewModel.FitToWindow):
                case nameof(MainViewModel.ZoomLevel):
                    ScheduleFitToWindowUpdate();
                    break;
                case nameof(MainViewModel.IsSlideShowActive):
                    HandleSlideShowChange();
                    break;
            }
        }

        private void HandleFullScreenChange()
        {
            if (ViewModel.IsFullScreen)
            {
                EnterFullScreen();
            }
            else
            {
                ExitFullScreen();
            }
        }

        private void ToggleFullScreen()
        {
            ViewModel.IsFullScreen = !ViewModel.IsFullScreen;
        }

        private void HandleSlideShowChange()
        {
            if (ViewModel.IsSlideShowActive)
            {
                ShowCursor();
                RestartCursorHideTimer();
            }
            else
            {
                _cursorHideTimer.Stop();
                ShowCursor();
            }
        }

        private void EnterFullScreen()
        {
            // 保存当前窗口状态
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;

            // 保存当前界面显示状态
            _previousShowStatusBar = ViewModel.Settings.ShowStatusBar;
            _previousShowSidebar = ViewModel.Settings.ShowSidebar;

            // 设置全屏模式
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;

            // 隐藏所有界面元素,只显示图片
            ViewModel.Settings.ShowStatusBar = false;
            ViewModel.Settings.ShowSidebar = false;
        }

        private void ExitFullScreen()
        {
            // 恢复窗口状态
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousWindowState;

            // 恢复界面显示状态
            ViewModel.Settings.ShowStatusBar = _previousShowStatusBar;
            ViewModel.Settings.ShowSidebar = _previousShowSidebar;
        }

        #endregion

        #region Image Interaction

        private void ImageContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                return;
            }

            _isDragging = true;
            _lastMousePosition = e.GetPosition(ImageScrollViewer);
            ImageContainer.CaptureMouse();
        }

        private void ImageContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageContainer.ReleaseMouseCapture();
        }

        private void ImageContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ImageScrollViewer);
                var delta = currentPosition - _lastMousePosition;

                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - delta.X);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - delta.Y);

                _lastMousePosition = currentPosition;
            }
        }

        private void PreviousZone_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                ViewModel.GoPreviousCommand.Execute(null);
            }
        }

        private void NextZone_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                ViewModel.GoNextCommand.Execute(null);
            }
        }

        #endregion

        #region Zoom & Fit

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleFitToWindowUpdate();
        }

        private void ScheduleFitToWindowUpdate()
        {
            Dispatcher.BeginInvoke(new Action(UpdateFitToWindowZoom), DispatcherPriority.Background);
        }

        private void UpdateFitToWindowZoom()
        {
            if (ViewModel.IsMangaMode)
                return;

            if (!ViewModel.FitToWindow || ViewModel.DisplayImage == null)
                return;

            var viewportWidth = ImageScrollViewer.ViewportWidth;
            var viewportHeight = ImageScrollViewer.ViewportHeight;

            if (viewportWidth <= 0 || viewportHeight <= 0)
                return;

            double imageWidth = ViewModel.DisplayImage.PixelWidth;
            double imageHeight = ViewModel.DisplayImage.PixelHeight;

            if (ViewModel.IsDoublePage && ViewModel.SecondDisplayImage != null)
            {
                imageWidth += ViewModel.SecondDisplayImage.PixelWidth + 8; // include gap between pages
                imageHeight = Math.Max(imageHeight, ViewModel.SecondDisplayImage.PixelHeight);
            }

            if (imageWidth <= 0 || imageHeight <= 0)
                return;

            var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);

            if (double.IsFinite(scale) && scale > 0)
            {
                ViewModel.ZoomLevel = scale;
            }
        }

        private void StartImageFadeIn()
        {
            if (ViewModel.DisplayImage == null && ViewModel.SecondDisplayImage == null)
                return;

            const double startOpacity = 1.0; // start at full opacity to avoid black flash
            const double durationMs = 140;

            void Fade(UIElement element)
            {
                if (element == null) return;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = startOpacity;
                var animation = new DoubleAnimation
                {
                    From = startOpacity,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new QuadraticEase()
                };
                element.BeginAnimation(UIElement.OpacityProperty, animation);
            }

            Fade(MainImage);
            Fade(SecondImageControl);
        }

        #endregion

        #region Thumbnail List

        private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox == null) return;

            // 确保选中项滚动到可见区域
            if (listBox.SelectedItem != null)
            {
                listBox.ScrollIntoView(listBox.SelectedItem);
            }

            // 更新 ViewModel 的 CurrentIndex 以切换显示的图片
            // 注意: 由于 XAML 中已有 SelectedIndex="{Binding CurrentIndex}" 绑定
            // 这个事件主要用于确保滚动到选中项
            // 但我们添加手动更新以确保绑定正常工作
            if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < ViewModel.Images.Count)
            {
                // 如果 ViewModel 的 CurrentIndex 与 ListBox 的 SelectedIndex 不同步
                // 手动更新 ViewModel
                if (ViewModel.CurrentIndex != listBox.SelectedIndex)
                {
                    ViewModel.CurrentIndex = listBox.SelectedIndex;
                }
            }
        }

        #endregion

        #region Manga Mode

        private void MangaScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 冒泡阶段的双击处理
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void MangaScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 隧道阶段也处理双击，确保漫画模式下双击能够进入/退出全屏
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void MangaScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!ViewModel.IsMangaMode || ViewModel.Images.Count == 0)
                return;

            // Find the image that's most visible in the viewport
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            var viewportTop = scrollViewer.VerticalOffset;
            var viewportBottom = viewportTop + scrollViewer.ViewportHeight;
            var viewportCenter = viewportTop + scrollViewer.ViewportHeight / 2;

            // Update scroll offset for ViewModel
            ViewModel.ScrollOffset = viewportTop;
        }

        #endregion
    }
}
