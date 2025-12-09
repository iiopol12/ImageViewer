using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;
using ImageViewer.Models;
using ImageViewer.ViewModels;
using ImageViewer.Helpers;

namespace ImageViewer.Views
{
    public partial class MainWindow : Window
    {

        // ViewModel 快捷访问属性
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        // 鼠标拖拽相关字段
        private Point _lastMousePosition;   // 上次鼠标位置
        private bool _isDragging;           // 是否正在拖拽图片

        // 全屏模式前的窗口状态保存
        private WindowState _previousWindowState; // 之前的窗口状态（最大化/正常）
        private WindowStyle _previousWindowStyle; // 之前的窗口样式
        private ResizeMode _previousResizeMode;   // 之前的调整大小模式
        private bool _previousShowStatusBar;      // 之前是否显示状态栏
        private bool _previousShowSidebar;        // 之前是否显示侧边栏
        private bool _previousTopmost;            // 之前是否置顶
        private double _previousLeft;             // 进入全屏前的 Left
        private double _previousTop;              // 进入全屏前的 Top
        private double _previousWidth;            // 进入全屏前的宽度
        private double _previousHeight;           // 进入全屏前的高度


        // 窗口位置恢复标志
        private bool _hasRestoredWindowPlacement;

        // 全局快捷键管理器
        private HotKeyManager _hotKeyManager;

        // 鼠标光标自动隐藏相关
        private readonly DispatcherTimer _cursorHideTimer;// 光标隐藏计时器
        private bool _isCursorHidden;                     // 光标是否已隐藏
        private readonly TimeSpan _cursorHideDelay = TimeSpan.FromSeconds(3);

        // 非客户区命中测试常量
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        // 瀑布流虚拟化相关
        private readonly List<Rect> _masonryRects = new();
        private readonly Dictionary<int, FrameworkElement> _masonryActive = new();
        private readonly Stack<FrameworkElement> _masonryPool = new();
        private const double MasonryTargetColumnWidth = 180;
        private const double MasonryGap = 8;

        public MainWindow()
        {
            InitializeComponent();

            // 订阅 ViewModel 属性变化事件
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Images.CollectionChanged += Images_CollectionChanged;
            // 初始化全局快捷键管理器
            _hotKeyManager = new HotKeyManager(this);
            // 初始化光标自动隐藏计时器
            _cursorHideTimer = new DispatcherTimer
            {
                Interval = _cursorHideDelay
            };
            _cursorHideTimer.Tick += CursorHideTimer_Tick;

            // 使用 ContentRendered 代替 Loaded，确保内容已渲染完成
            ContentRendered += async (s, e) =>
            {
                // 注册全局快捷键
                RegisterGlobalHotKeys();

                Opacity = 1;
                // 如果有启动参数传入的文件，则加载该文件
                if (Application.Current.Properties["StartupFile"] is string filePath)
                {
                    await ViewModel.LoadImageFromPath(filePath);
                }
              
            };
        }


        /// <summary>
        /// 窗口源初始化完成时触发 - 这是窗口句柄创建后的最早时机
        /// 用于恢复窗口位置和显示窗口
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 防止重复恢复窗口位置
            if (_hasRestoredWindowPlacement)
                return;
            // 恢复上次保存的窗口位置和大小
            RestoreWindowPlacement();
            _hasRestoredWindowPlacement = true;


            // 使用淡入动画显示窗口，而不是直接设置 Opacity = 1
            //FadeInWindow();

            // 处理非客户区命中，确保上下边框可缩放
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WindowProc);
            }

        }


        /// <summary>
        /// 恢复窗口位置和大小 - 从设置中读取上次保存的窗口状态
        /// </summary>
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

            // 恢复窗口顶部位置
            if (ViewModel.Settings.WindowTop >= 0)
                Top = ViewModel.Settings.WindowTop;
            // 恢复窗口最大化状态
            if (ViewModel.Settings.IsMaximized)
                WindowState = WindowState.Maximized;
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

        #region 窗口标题栏事件处理
        /// <summary>
        /// 标题栏鼠标左键按下事件 - 处理拖动和双击最大化
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {// 双击标题栏 - 切换最大化/还原
                MaximizeButton_Click(sender, e);
            }
            else
            { // 单击拖动 - 移动窗口
                DragMove();
            }
        }
        /// <summary>
        /// 最小化按钮点击事件
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        /// <summary>
        /// 最大化/还原按钮点击事件
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            // 切换窗口状态
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


        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region  窗口事件处理

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
        /// <summary>
        /// 窗口关闭事件 - 保存设置和清理资源
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ViewModel.IsFullScreen)
            {
                ExitFullScreen();
            }

            // 注销所有快捷键
            _hotKeyManager?.Dispose();

            ViewModel.Images.CollectionChanged -= Images_CollectionChanged;
            foreach (var img in ViewModel.Images)
            {
                img.PropertyChanged -= Image_PropertyChanged;
            }

            // 保存当前窗口位置和大小
            if (ViewModel.Settings.RememberWindowPosition)
            {// 如果窗口不是最大化状态，保存实际位置和大小
                ViewModel.Settings.WindowWidth = Width;
                ViewModel.Settings.WindowHeight = Height;
                ViewModel.Settings.WindowLeft = Left;
                ViewModel.Settings.WindowTop = Top;
                ViewModel.Settings.IsMaximized = WindowState == WindowState.Maximized;
            }
            // 保存设置到文件
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

        /// <summary>
        /// 窗口激活事件 - 窗口获得焦点时触发
        /// </summary>
        private void Window_Activated(object sender, EventArgs e)
        {
            // 窗口激活时重新注册快捷键，避免在后台抢占其它软件按键
            RegisterGlobalHotKeys();
        }


        /// <summary>
        /// 窗口失活（失去焦点）
        /// </summary>
        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 窗口失焦时释放全局热键
            _hotKeyManager?.UnregisterAll();
        }

        /// <summary>
        /// 鼠标移动事件 - 用于自动隐藏光标
        /// </summary>
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
        /// <summary>
        /// 鼠标滚轮预览事件
        /// </summary>
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
        /// <summary>
        /// 光标隐藏计时器触发事件 - 在全屏模式下自动隐藏光标
        /// </summary>
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
        /// <summary>
        /// 鼠标滚轮事件 - 处理缩放或翻页
        /// </summary>
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {

            // 判断是否按下 Ctrl 键
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
            // 鼠标滚轮向上（Delta > 0）应放大，向下缩小
            double delta = e.Delta > 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
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
                    double newHorizontalOffset = mousePos.X -horizontalRatio * ImageScrollViewer.ExtentWidth;
                    double newVerticalOffset = mousePos.Y- verticalRatio * ImageScrollViewer.ExtentHeight ;

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
            // 鼠标滚轮向上（Delta > 0）应放大，向下缩小
            double delta = e.Delta > 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
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

        #region  ViewModel 属性变化处理
        /// <summary>
        /// ViewModel 属性变化事件处理
        /// </summary>
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsFullScreen):
                    // 处理全屏模式切换
                    HandleFullScreenChange();
                    break;
                case nameof(MainViewModel.DisplayImage):
                case nameof(MainViewModel.SecondDisplayImage):
                    // 图片加载完成后，触发淡入动画
                    StartImageFadeIn();
                    goto case nameof(MainViewModel.FitToWindow);
                case nameof(MainViewModel.CurrentViewMode):
                case nameof(MainViewModel.FitToWindow):
                case nameof(MainViewModel.ZoomLevel):
                    // 查看模式改变时，重新计算缩放
                    ScheduleFitToWindowUpdate();
                    break;
                case nameof(MainViewModel.IsSlideShowActive):
                    // 适应窗口选项改变时，重新计算缩放
                    HandleSlideShowChange();
                    break;
                case nameof(MainViewModel.IsMasonryMode):
                    OnMasonryModeChanged();
                    break;
                case nameof(MainViewModel.CurrentIndex):
                    // 漫画模式下切换页时，确保滚动到当前项
                    ScrollMangaToCurrent();
                    RefreshActiveMasonryItems();
                    break;
            }
        }


        // 处理全屏模式切换
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
        /// <summary>
        /// 切换全屏模式（快捷方法）
        /// </summary>
        private void ToggleFullScreen()
        {
            ViewModel.IsFullScreen = !ViewModel.IsFullScreen;
        }

        // 适应窗口选项改变时，重新计算缩放
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


        /// <summary>
        /// 进入全屏模式
        /// </summary>
        private void EnterFullScreen()
        {
            // 保存当前窗口状态
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousTopmost = Topmost;
            _previousLeft = Left;
            _previousTop = Top;
            _previousWidth = Width;
            _previousHeight = Height;

            // 保存当前界面显示状态
            _previousShowStatusBar = ViewModel.Settings.ShowStatusBar;
            _previousShowSidebar = ViewModel.Settings.ShowSidebar;

            // 设置全屏模式
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            // 使用当前屏幕的完整大小，覆盖任务栏
            var screen = WinForms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            WindowState = WindowState.Normal; // 先切回正常，以便按屏幕尺寸覆盖
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            // 隐藏所有界面元素,只显示图片
            ViewModel.Settings.ShowStatusBar = false;
            ViewModel.Settings.ShowSidebar = false;
        }


        /// <summary>
        /// 退出全屏模式
        /// </summary>
        private void ExitFullScreen()
        {
            // 恢复窗口状态
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            Topmost = _previousTopmost;
            WindowState = WindowState.Normal;
            Left = _previousLeft;
            Top = _previousTop;
            Width = _previousWidth;
            Height = _previousHeight;
            WindowState = _previousWindowState;

            // 恢复界面显示状态
            ViewModel.Settings.ShowStatusBar = _previousShowStatusBar;
            ViewModel.Settings.ShowSidebar = _previousShowSidebar;
        }

        #endregion

        #region  图片交互处理
        /// <summary>
        /// 图片容器鼠标左键按下事件 - 开始拖拽或双击全屏
        /// </summary>
        private void ImageContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击图片 - 切换全屏
                ToggleFullScreen();
                return;
            }
            // 开始拖拽图片
            _isDragging = true;
            _lastMousePosition = e.GetPosition(ImageScrollViewer);
            ImageContainer.CaptureMouse();
        }
        /// <summary>
        /// 图片容器鼠标左键释放事件 - 结束拖拽
        /// </summary>
        private void ImageContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageContainer.ReleaseMouseCapture();
        }
        /// <summary>
        /// 图片容器鼠标移动事件 - 处理图片拖拽
        /// </summary>
        private void ImageContainer_MouseMove(object sender, MouseEventArgs e)
        {
            // 只有在拖拽状态下才处理移动
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ImageScrollViewer);
                var delta = currentPosition - _lastMousePosition;
                // 滚动 ScrollViewer 以移动图片
                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - delta.X);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - delta.Y);
                // 更新上次鼠标位置
                _lastMousePosition = currentPosition;
            }
        }
        /// <summary>
        /// 上一张区域点击事件 - 点击图片左侧区域切换到上一张
        /// </summary>
        private void PreviousZone_Click(object sender, MouseButtonEventArgs e)
        {
            // 只有在非拖拽状态下才切换图片
            if (!_isDragging)
            {
                ViewModel.GoPreviousCommand.Execute(null);
            }
        }
        /// <summary>
        /// 下一张区域点击事件 - 点击图片右侧区域切换到下一张
        /// </summary>
        private void NextZone_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                ViewModel.GoNextCommand.Execute(null);
            }
        }

        #endregion

        #region  缩放和适应窗口 Zoom & Fit
        /// <summary>
        /// 图片滚动视图大小改变事件 - 窗口大小改变时重新计算适应窗口的缩放
        /// </summary>
        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleFitToWindowUpdate();
        }

        /// <summary>
        /// 延迟更新适应窗口缩放 - 避免频繁计算
        /// </summary>
        private void ScheduleFitToWindowUpdate()
        {// 使用 Dispatcher 在后台优先级执行，避免阻塞 UI
            Dispatcher.BeginInvoke(new Action(UpdateFitToWindowZoom), DispatcherPriority.Background);
        }


        /// <summary>
        /// 更新适应窗口的缩放级别
        /// </summary>
        private void UpdateFitToWindowZoom()
        {
            // 漫画模式不需要自动适应窗口
            if (ViewModel.IsMangaMode)
                return;
            // 如果未启用适应窗口或没有图片，则不处理
            if (!ViewModel.FitToWindow || ViewModel.DisplayImage == null)
                return;
            // 获取视口大小
            var viewportWidth = ImageScrollViewer.ViewportWidth;
            var viewportHeight = ImageScrollViewer.ViewportHeight;

            if (viewportWidth <= 0 || viewportHeight <= 0)
                return;
            // 获取图片实际大小
            double imageWidth = ViewModel.DisplayImage.PixelWidth;
            double imageHeight = ViewModel.DisplayImage.PixelHeight;
            // 如果是双页模式，需要考虑第二张图片
            if (ViewModel.IsDoublePage && ViewModel.SecondDisplayImage != null)
            {
                imageWidth += ViewModel.SecondDisplayImage.PixelWidth + 8; // include gap between pages
                imageHeight = Math.Max(imageHeight, ViewModel.SecondDisplayImage.PixelHeight);
            }

            if (imageWidth <= 0 || imageHeight <= 0)
                return;
            // 计算适应窗口的缩放比例（取宽度和高度缩放比例的较小值）
            var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
            // 设置缩放级别
            if (double.IsFinite(scale) && scale > 0)
            {
                ViewModel.ZoomLevel = scale;
            }
        }
        /// <summary>
        /// 开始图片淡入动画 - 使图片切换更平滑
        /// </summary>
        private void StartImageFadeIn()
        {
            // 如果没有图片，则不执行动画
            if (ViewModel.DisplayImage == null && ViewModel.SecondDisplayImage == null)
                return;

            const double startOpacity = 1.0;// 从完全不透明开始，避免黑色闪烁
            const double durationMs = 140;// 动画持续时间
            // 淡入动画辅助方法
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
            // 对主图片和第二张图片应用淡入效果
            Fade(MainImage);
            Fade(SecondImageControl);
        }

        #endregion

        #region 瀑布流 Masonry
        private void Images_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ImageInfo img in e.OldItems)
                {
                    img.PropertyChanged -= Image_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (ImageInfo img in e.NewItems)
                {
                    img.PropertyChanged += Image_PropertyChanged;
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var img in ViewModel.Images)
                {
                    img.PropertyChanged -= Image_PropertyChanged;
                    img.PropertyChanged += Image_PropertyChanged;
                }
            }

            if (ViewModel.IsMasonryMode)
            {
                RefreshMasonryLayout();
            }
        }

        private void Image_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageInfo.IsBookmarked)
                || e.PropertyName == nameof(ImageInfo.IsCurrent)
                || e.PropertyName == nameof(ImageInfo.Thumbnail))
            {
                RefreshActiveMasonryItems();
            }
        }

        private void OnMasonryModeChanged()
        {
            if (ViewModel.IsMasonryMode)
            {
                MasonryScrollViewer?.ScrollToVerticalOffset(0);
                RefreshMasonryLayout();
            }
            else
            {
                ClearMasonryVisuals();
            }
        }

        private void RefreshMasonryLayout()
        {
            if (!ViewModel.IsMasonryMode || MasonryScrollViewer == null || MasonryCanvas == null)
                return;

            BuildMasonryRects();
            UpdateMasonryRealization();
        }

        private void BuildMasonryRects()
        {
            _masonryRects.Clear();
            if (!ViewModel.IsMasonryMode || MasonryScrollViewer == null || MasonryCanvas == null)
                return;

            var availableWidth = MasonryScrollViewer.ViewportWidth > 0
                ? MasonryScrollViewer.ViewportWidth
                : MasonryScrollViewer.ActualWidth;

            if (availableWidth <= 0) return;

            var columnWidth = Math.Max(120, MasonryTargetColumnWidth);
            var columnCount = Math.Max(1, (int)Math.Floor((availableWidth + MasonryGap) / (columnWidth + MasonryGap)));
            var actualWidth = Math.Max(80, (availableWidth - MasonryGap * (columnCount - 1)) / columnCount);

            var heights = new double[columnCount];
            for (int i = 0; i < ViewModel.Images.Count; i++)
            {
                var info = ViewModel.Images[i];
                double aspect = (info.Width > 0 && info.Height > 0) ? (double)info.Height / info.Width : 0.75;
                double itemHeight = actualWidth * aspect;

                int targetCol = 0;
                double minHeight = heights[0];
                for (int col = 1; col < columnCount; col++)
                {
                    if (heights[col] < minHeight)
                    {
                        minHeight = heights[col];
                        targetCol = col;
                    }
                }

                double x = targetCol * (actualWidth + MasonryGap);
                double y = heights[targetCol];
                _masonryRects.Add(new Rect(x, y, actualWidth, itemHeight));
                heights[targetCol] += itemHeight + MasonryGap;
            }

            MasonryCanvas.Width = availableWidth;
            MasonryCanvas.Height = heights.Length > 0 ? heights.Max() : 0;
        }

        private void UpdateMasonryRealization()
        {
            if (!ViewModel.IsMasonryMode || MasonryScrollViewer == null || MasonryCanvas == null)
                return;

            if (_masonryRects.Count != ViewModel.Images.Count)
            {
                BuildMasonryRects();
            }

            if (_masonryRects.Count == 0)
            {
                ClearMasonryVisuals();
                return;
            }

            double viewportTop = MasonryScrollViewer.VerticalOffset;
            double viewportBottom = viewportTop + MasonryScrollViewer.ViewportHeight;
            const double buffer = 400;

            var visible = new HashSet<int>();
            for (int i = 0; i < _masonryRects.Count; i++)
            {
                var rect = _masonryRects[i];
                if (rect.Bottom >= viewportTop - buffer && rect.Top <= viewportBottom + buffer)
                {
                    visible.Add(i);
                }
            }

            var remove = _masonryActive.Keys.Where(k => !visible.Contains(k)).ToList();
            foreach (var idx in remove)
            {
                var el = _masonryActive[idx];
                MasonryCanvas.Children.Remove(el);
                _masonryPool.Push(el);
                _masonryActive.Remove(idx);
            }

            foreach (var idx in visible)
            {
                if (_masonryActive.ContainsKey(idx))
                    continue;

                var element = _masonryPool.Count > 0 ? _masonryPool.Pop() : CreateMasonryItem();
                BindMasonryItem(element, idx);

                var rect = _masonryRects[idx];
                element.Width = rect.Width;
                element.Height = rect.Height;
                Canvas.SetLeft(element, rect.X);
                Canvas.SetTop(element, rect.Y);

                _masonryActive[idx] = element;
                MasonryCanvas.Children.Add(element);
            }
        }

        private FrameworkElement CreateMasonryItem()
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BackgroundDarkBrush"),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            var image = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var bookmark = new TextBlock
            {
                Text = "★",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                FontSize = 14,
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };

            grid.Children.Add(image);
            grid.Children.Add(bookmark);
            border.Child = grid;

            border.Tag = new MasonryItemVisual
            {
                Image = image,
                Bookmark = bookmark,
                Index = -1
            };

            border.MouseLeftButtonUp += MasonryItem_MouseLeftButtonUp;
            return border;
        }

        private void BindMasonryItem(FrameworkElement element, int index)
        {
            if (element is not Border border || border.Tag is not MasonryItemVisual visual)
                return;

            if (index < 0 || index >= ViewModel.Images.Count)
                return;

            visual.Index = index;
            var info = ViewModel.Images[index];

            visual.Image.Source = info.Thumbnail;
            visual.Bookmark.Visibility = info.IsBookmarked ? Visibility.Visible : Visibility.Collapsed;
            border.BorderThickness = info.IsCurrent ? new Thickness(2) : new Thickness(0);
            border.BorderBrush = info.IsCurrent ? (Brush)FindResource("PrimaryBrush") : Brushes.Transparent;
        }

        private void RefreshActiveMasonryItems()
        {
            foreach (var kvp in _masonryActive.ToList())
            {
                BindMasonryItem(kvp.Value, kvp.Key);
            }
        }

        private void ClearMasonryVisuals()
        {
            MasonryCanvas.Children.Clear();
            _masonryActive.Clear();
            _masonryRects.Clear();
            _masonryPool.Clear();
        }

        private void MasonryScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateMasonryRealization();
        }

        private void MasonryScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshMasonryLayout();
        }

        private void MasonryItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.Tag is not MasonryItemVisual visual)
                return;

            var index = visual.Index;
            if (index < 0 || index >= ViewModel.Images.Count)
                return;

            ViewModel.CurrentIndex = index;

            if (ViewModel.ToggleMasonryModeCommand.CanExecute(null))
            {
                ViewModel.ToggleMasonryModeCommand.Execute(null);
            }
            else
            {
                ViewModel.SetViewModeCommand?.Execute(ViewMode.Single);
            }
        }

        private class MasonryItemVisual
        {
            public Image Image { get; set; } = null!;
            public TextBlock Bookmark { get; set; } = null!;
            public int Index { get; set; }
        }
        #endregion

        #region 缩略图列表Thumbnail List
        /// <summary>
        /// 缩略图列表选择改变事件 - 切换到选中的图片
        /// </summary>
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

        #region 漫画模式Manga Mode
        /// <summary>
        /// 漫画滚动视图鼠标左键按下事件（冒泡阶段）
        /// </summary>
        private void MangaScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 冒泡阶段的双击处理
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }
        /// <summary>
        /// 漫画滚动视图鼠标左键按下预览事件（隧道阶段）
        /// </summary>
        private void MangaScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 隧道阶段也处理双击，确保漫画模式下双击能够进入/退出全屏
            if (e.ClickCount == 2)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }
        /// <summary>
        /// 漫画滚动视图滚动改变事件 - 更新当前滚动位置
        /// </summary>
        private void MangaScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 只在漫画模式下处理
            if (!ViewModel.IsMangaMode || ViewModel.Images.Count == 0)
                return;

            // 查找在视口中最可见的图片
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;
            // 计算视口范围
            var viewportTop = scrollViewer.VerticalOffset;
            var viewportBottom = viewportTop + scrollViewer.ViewportHeight;
            var viewportCenter = viewportTop + scrollViewer.ViewportHeight / 2;

            // 更新 ViewModel 的滚动偏移量
            ViewModel.ScrollOffset = viewportTop;
        }

        /// <summary>
        /// 漫画模式：滚动到当前索引对应的项
        /// </summary>
        private void ScrollMangaToCurrent()
        {
            if (!ViewModel.IsMangaMode || ViewModel.CurrentIndex < 0)
                return;

            // 等待布局完成后再定位，确保容器已生成并尺寸可用
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 尝试获取当前索引对应的容器
                if (MangaItemsControl.ItemContainerGenerator.ContainerFromIndex(ViewModel.CurrentIndex)
                    is not FrameworkElement element)
                {
                    MangaItemsControl.UpdateLayout();
                    element = MangaItemsControl.ItemContainerGenerator.ContainerFromIndex(ViewModel.CurrentIndex) as FrameworkElement;
                }

                if (element == null || MangaScrollViewer.ViewportHeight <= 0)
                    return;

                // 计算元素在 ScrollViewer 内的变换后边界，考虑缩放
                var bounds = element.TransformToVisual(MangaScrollViewer)
                                    .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
                double elementCenter = bounds.Top + bounds.Height / 2.0 + MangaScrollViewer.VerticalOffset;
                double targetOffset = elementCenter - MangaScrollViewer.ViewportHeight / 2.0;

                // Clamp 到可滚动范围
                targetOffset = Math.Max(0, Math.Min(targetOffset, MangaScrollViewer.ScrollableHeight));
                MangaScrollViewer.ScrollToVerticalOffset(targetOffset);
            }), DispatcherPriority.Background);
        }

        #endregion

        #region 非客户区命中测试
        /// <summary>
        /// 自定义命中测试，保证上下边缘也能触发窗口缩放
        /// </summary>
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                var hitResult = HitTestResize(lParam);
                if (hitResult != IntPtr.Zero)
                {
                    handled = true;
                    return hitResult;
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr HitTestResize(IntPtr lParam)
        {
            // lParam 打包了屏幕坐标（低位 X，高位 Y）
            var mousePos = GetMousePosition(lParam);
            var pos = PointFromScreen(mousePos);
            double border = SystemParameters.WindowResizeBorderThickness.Left;
            double width = ActualWidth;
            double height = ActualHeight;

            bool onLeft = pos.X >= 0 && pos.X <= border;
            bool onRight = pos.X >= width - border && pos.X <= width;
            bool onTop = pos.Y >= 0 && pos.Y <= border;
            bool onBottom = pos.Y >= height - border && pos.Y <= height;

            if (onLeft && onTop) return new IntPtr(HTTOPLEFT);
            if (onRight && onTop) return new IntPtr(HTTOPRIGHT);
            if (onLeft && onBottom) return new IntPtr(HTBOTTOMLEFT);
            if (onRight && onBottom) return new IntPtr(HTBOTTOMRIGHT);
            if (onLeft) return new IntPtr(HTLEFT);
            if (onRight) return new IntPtr(HTRIGHT);
            if (onTop) return new IntPtr(HTTOP);
            if (onBottom) return new IntPtr(HTBOTTOM);

            return IntPtr.Zero;
        }

        private static Point GetMousePosition(IntPtr lParam)
        {
            int x = unchecked((short)((long)lParam & 0xFFFF));
            int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            return new Point(x, y);
        }
        #endregion
    }
}
