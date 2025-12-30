using ImageViewer.Helpers;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;


namespace ImageViewer.Views
{
    public partial class MainWindow : Window
    {

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private const int FolderSwitchMaxVisibleItems = 7;
        private readonly ObservableCollection<FolderSwitchEntry> _folderSwitchVisibleEntries = new();
        private List<FolderSwitchEntry> _folderSwitchEntries = new();
        private string _folderSwitchBaseDirectory = string.Empty;
        private int _folderSwitchSelectedIndex;
        private int _folderSwitchWindowStart;
        private bool _isFolderSwitchOpen;
        private bool _isFolderSwitchCommitInProgress;
        private readonly ImageService _folderSwitchImageService = new();
        private CancellationTokenSource? _folderSwitchThumbnailCts;

        // 鼠标拖拽相关字段
        private Point _lastMousePosition;   // 上次鼠标位置
        private bool _isDragging;           // 是否正在拖拽图片

        // 最大化窗口拖动相关字段
        private Point _dragStartMousePosition;      // 开始拖动时的鼠标位置
        private bool _isDraggingMaximizedWindow;    // 是否正在拖动最大化窗口
        private bool _isNormalWindowDragging;       // 是否正在拖动普通窗口

        // 全屏模式前的窗口状态保存
        private WindowState _previousWindowState; // 之前的窗口状态（最大化/正常）
        private WindowStyle _previousWindowStyle; // 之前的窗口样式
        private ResizeMode _previousResizeMode;   // 之前的调整大小模式
        private bool _previousShowStatusBar;      // 之前是否显示状态栏
        private bool _previousShowSidebar;        // 之前是否显示侧边栏

        private double _previousLeft;
        private double _previousTop;
        private double _previousWidth;
        private double _previousHeight;
        private bool _previousTopmost;
        // 窗口位置恢复标志
        private bool _hasRestoredWindowPlacement;

        // 全局快捷键管理器
        private HotKeyManager _hotKeyManager;

        // 鼠标光标自动隐藏相关
        private readonly DispatcherTimer _cursorHideTimer;// 光标隐藏计时器
        private bool _isCursorHidden;                     // 光标是否已隐藏
        private readonly TimeSpan _cursorHideDelay = TimeSpan.FromSeconds(3);

        private bool _mangaCenterPending;

        // 非客户区命中测试常量
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_SIZING = 0x0214;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        private const int WMSZ_LEFT = 1;
        private const int WMSZ_RIGHT = 2;
        private const int WMSZ_TOP = 3;
        private const int WMSZ_TOPLEFT = 4;
        private const int WMSZ_TOPRIGHT = 5;
        private const int WMSZ_BOTTOM = 6;
        private const int WMSZ_BOTTOMLEFT = 7;
        private const int WMSZ_BOTTOMRIGHT = 8;

        private bool _isInSizeMove;
        private bool _isResizeFreezeActive;
        private bool _isResizeFreezePending;
        private int _resizeSizingEdge;
        private double _resizeFreezeDipWidth;
        private double _resizeFreezeDipHeight;
        private RenderTargetBitmap? _resizePreparedSnapshot;
        private Storyboard? _resizeFreezeStoryboard;
        private Visibility _mainLayoutVisibilityBeforeResize = Visibility.Visible;

        // 全屏模式下缩略图栏自动显示相关
        private const double FullScreenBarTriggerZone = 60; // 触发区域像素
        private bool _isFullScreenBarVisible;
        private readonly DispatcherTimer _fullScreenBarHideTimer;

        public MainWindow()
        {
            InitializeComponent();

            FolderSwitchListBox.ItemsSource = _folderSwitchVisibleEntries;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            // 初始化全局快捷键管理器
            _hotKeyManager = new HotKeyManager(this);
            // 初始化光标自动隐藏计时器
            _cursorHideTimer = new DispatcherTimer
            {
                Interval = _cursorHideDelay
            };
            _cursorHideTimer.Tick += CursorHideTimer_Tick;
            _fullScreenBarHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _fullScreenBarHideTimer.Tick += FullScreenBarHideTimer_Tick;
            ContentRendered += (s, e) =>
            {
                // 注册全局快捷键
                RegisterGlobalHotKeys();

                if (Opacity < 1)
                {
                    FadeInWindow();
                }

            };
        }

        internal Task LoadStartupFileAsync(string filePath)
        {
            return ViewModel.LoadImageFromPath(filePath);
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

            // 处理非客户区命中，确保上下边框可缩放
            // 注册窗口消息钩子，处理自定义命中测试
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WindowProc);
            }

        }
        #region 非客户区命中测试
        /// <summary>
        /// 自定义命中测试，保证上下边缘也能触发窗口缩放
        /// </summary>
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCHITTEST:
                    {
                        var hitResult = HitTestResize(lParam);
                        if (hitResult != IntPtr.Zero)
                        {
                            handled = true;
                            return hitResult;
                        }
                        break;
                    }
                case WM_ENTERSIZEMOVE:
                    _isInSizeMove = true;
                    _resizeSizingEdge = 0;
                    _resizePreparedSnapshot = null;
                    _isResizeFreezeActive = false;
                    _isResizeFreezePending = false;
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, PrepareResizeFreezeSnapshot);
                    break;
                case WM_SIZING:
                    if (_isInSizeMove)
                    {
                        _resizeSizingEdge = wParam.ToInt32();
                        if (!_isResizeFreezeActive && !_isResizeFreezePending)
                        {
                            _isResizeFreezePending = true;
                            Dispatcher.BeginInvoke(DispatcherPriority.Render, BeginResizeFreezeIfEnabled);
                        }
                    }
                    break;
                case WM_EXITSIZEMOVE:
                    _isInSizeMove = false;
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, EndResizeFreezeIfNeeded);
                    break;
            }
            return IntPtr.Zero;
        }

        private IntPtr HitTestResize(IntPtr lParam)
        {
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

        #region Resize Freeze (reduce jitter during window sizing)
        private bool IsResizeFreezeEnabled()
        {
            try
            {
                return ViewModel?.Settings?.FreezeDuringResize ?? true;
            }
            catch
            {
                return true;
            }
        }

        private void PrepareResizeFreezeSnapshot()
        {
            if (!_isInSizeMove || _isResizeFreezeActive || !IsResizeFreezeEnabled())
            {
                return;
            }

            if (_resizePreparedSnapshot != null)
            {
                return;
            }

            CaptureResizeFreezeSnapshot();
        }

        private void BeginResizeFreezeIfEnabled()
        {
            _isResizeFreezePending = false;

            if (!_isInSizeMove || _isResizeFreezeActive || !IsResizeFreezeEnabled())
            {
                return;
            }

            if (_resizePreparedSnapshot == null)
            {
                CaptureResizeFreezeSnapshot();
                if (_resizePreparedSnapshot == null)
                {
                    return;
                }
            }

            StopResizeFreezeStoryboard();

            _isResizeFreezeActive = true;

            _mainLayoutVisibilityBeforeResize = MainLayout.Visibility;
            MainLayout.Visibility = Visibility.Collapsed;

            ResizeFreezeImage.BeginAnimation(OpacityProperty, null);
            ResizeFreezeImage.RenderTransform = null;
            ResizeFreezeImage.RenderTransformOrigin = new Point(0.5, 0.5);
            ResizeFreezeImage.Opacity = 1;
            ResizeFreezeImage.Width = _resizeFreezeDipWidth;
            ResizeFreezeImage.Height = _resizeFreezeDipHeight;
            ResizeFreezeImage.Source = _resizePreparedSnapshot;
            ResizeFreezeImage.Visibility = Visibility.Visible;

            ResizeFreezeBorder.BeginAnimation(OpacityProperty, null);
            ResizeFreezeBorder.Opacity = 1;
            ResizeFreezeBorder.Visibility = Visibility.Visible;
        }

        private void EndResizeFreezeIfNeeded()
        {
            if (!_isResizeFreezeActive)
            {
                _resizePreparedSnapshot = null;
                _isResizeFreezePending = false;
                return;
            }

            MainLayout.Visibility = _mainLayoutVisibilityBeforeResize;
            AnimateAndCleanupResizeFreezeOverlay();
        }

        private void CaptureResizeFreezeSnapshot()
        {
            if (RootGrid == null)
            {
                return;
            }

            _resizeFreezeDipWidth = RootGrid.ActualWidth;
            _resizeFreezeDipHeight = RootGrid.ActualHeight;
            if (_resizeFreezeDipWidth <= 0 || _resizeFreezeDipHeight <= 0)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(RootGrid);
            int pixelWidth = Math.Max(1, (int)Math.Round(_resizeFreezeDipWidth * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Round(_resizeFreezeDipHeight * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);

            rtb.Render(RootGrid);
            rtb.Freeze();
            _resizePreparedSnapshot = rtb;
        }

        private void AnimateAndCleanupResizeFreezeOverlay()
        {
            StopResizeFreezeStoryboard();

            double newWidth = RootGrid.ActualWidth;
            double newHeight = RootGrid.ActualHeight;

            double scaleX = (_resizeFreezeDipWidth > 0) ? (newWidth / _resizeFreezeDipWidth) : 1;
            double scaleY = (_resizeFreezeDipHeight > 0) ? (newHeight / _resizeFreezeDipHeight) : 1;

            if (!double.IsFinite(scaleX) || scaleX <= 0) scaleX = 1;
            if (!double.IsFinite(scaleY) || scaleY <= 0) scaleY = 1;

            var scaleTransform = new ScaleTransform(1, 1);
            ResizeFreezeImage.RenderTransformOrigin = GetResizeTransformOrigin(_resizeSizingEdge);
            ResizeFreezeImage.RenderTransform = scaleTransform;

            var duration = TimeSpan.FromMilliseconds(160);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };

            storyboard.Children.Add(CreateDoubleAnimation(ResizeFreezeImage, UIElement.OpacityProperty, 1, 0, duration, ease));
            storyboard.Children.Add(CreateDoubleAnimation(ResizeFreezeBorder, UIElement.OpacityProperty, 1, 0, duration, ease));
            storyboard.Children.Add(CreateDoubleAnimation(scaleTransform, ScaleTransform.ScaleXProperty, 1, scaleX, duration, ease));
            storyboard.Children.Add(CreateDoubleAnimation(scaleTransform, ScaleTransform.ScaleYProperty, 1, scaleY, duration, ease));

            storyboard.Completed += (_, _) => CleanupResizeFreezeOverlay();

            _resizeFreezeStoryboard = storyboard;
            storyboard.Begin();
        }

        private static Timeline CreateDoubleAnimation(
            DependencyObject target,
            DependencyProperty property,
            double from,
            double to,
            TimeSpan duration,
            IEasingFunction? easing)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(duration),
                EasingFunction = easing
            };

            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(property));
            return anim;
        }

        private static Point GetResizeTransformOrigin(int sizingEdge)
        {
            return sizingEdge switch
            {
                WMSZ_LEFT => new Point(1, 0.5),
                WMSZ_RIGHT => new Point(0, 0.5),
                WMSZ_TOP => new Point(0.5, 1),
                WMSZ_BOTTOM => new Point(0.5, 0),
                WMSZ_TOPLEFT => new Point(1, 1),
                WMSZ_TOPRIGHT => new Point(0, 1),
                WMSZ_BOTTOMLEFT => new Point(1, 0),
                WMSZ_BOTTOMRIGHT => new Point(0, 0),
                _ => new Point(0.5, 0.5)
            };
        }

        private void CleanupResizeFreezeOverlay()
        {
            StopResizeFreezeStoryboard();

            ResizeFreezeImage.BeginAnimation(OpacityProperty, null);
            ResizeFreezeImage.RenderTransform = null;
            ResizeFreezeImage.Source = null;
            ResizeFreezeImage.Visibility = Visibility.Collapsed;
            ResizeFreezeImage.Opacity = 1;
            ResizeFreezeImage.Width = double.NaN;
            ResizeFreezeImage.Height = double.NaN;

            ResizeFreezeBorder.BeginAnimation(OpacityProperty, null);
            ResizeFreezeBorder.Visibility = Visibility.Collapsed;
            ResizeFreezeBorder.Opacity = 1;

            _resizePreparedSnapshot = null;
            _resizeFreezeDipWidth = 0;
            _resizeFreezeDipHeight = 0;
            _resizeSizingEdge = 0;
            _isResizeFreezeActive = false;
        }

        private void StopResizeFreezeStoryboard()
        {
            if (_resizeFreezeStoryboard == null)
            {
                return;
            }

            try
            {
                _resizeFreezeStoryboard.Stop();
            }
            catch
            {
            }
            finally
            {
                _resizeFreezeStoryboard = null;
            }
        }
        #endregion

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


        /// <summary>
        /// 窗口淡入动画 - 使窗口平滑显示，避免突兀的白屏
        /// </summary>
        private void FadeInWindow()
        {
            const double durationMs = 120;
            // 清除之前的动画
            BeginAnimation(OpacityProperty, null);
            var animation = new DoubleAnimation
            {
                From = 0, // 从完全透明开始，避免显示初始化白屏
                To = 1,  // 到完全不透明结束
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase() // 使用二次缓动函数，使动画更平滑
            };
            BeginAnimation(OpacityProperty, animation);
        }

        #region 全局快捷键注册

        /// <summary>
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

                _hotKeyManager.RegisterHotKey(ModifierKeys.Control | ModifierKeys.Shift, Key.F,
                    () => ViewModel.ToggleFilterCommand?.Execute(null));

                // === 收藏文件夹切换 ===
                _hotKeyManager.RegisterHotKey(ModifierKeys.Control, Key.Tab,
                    ShowFolderSwitchOverlay);
                _hotKeyManager.RegisterHotKey(ModifierKeys.Control | ModifierKeys.Shift, Key.B,
                    OpenFavoritesPage);

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
                    () => ViewModel.ToggleFitToActualCommand?.Execute(null));

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
                if (WindowState == WindowState.Maximized)
                {
                    // 保存当前鼠标位置（相对于窗口）
                    _dragStartMousePosition = e.GetPosition(this);
                    _isDraggingMaximizedWindow = true;

                    // 捕获鼠标以便跟踪移动
                    TitleBar.CaptureMouse();
                }
                else
                {
                    _isNormalWindowDragging = true;
                    DragMove();
                }
            }
        }

        /// <summary>
        /// 标题栏鼠标释放事件
        /// </summary>
        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingMaximizedWindow)
            {
                _isDraggingMaximizedWindow = false;
                TitleBar.ReleaseMouseCapture();
            }
            _isNormalWindowDragging = false;
        }

        /// <summary>
        /// 标题栏鼠标移动事件
        /// </summary>
        private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingMaximizedWindow && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentMousePosition = e.GetPosition(this);

                // 计算鼠标移动距离
                double deltaX = currentMousePosition.X - _dragStartMousePosition.X;
                double deltaY = currentMousePosition.Y - _dragStartMousePosition.Y;

                // 当鼠标移动超过一定阈值时，恢复窗口大小并开始拖动
                if (Math.Abs(deltaX) > 5 || Math.Abs(deltaY) > 5)
                {
                    RestoreAndDragMaximizedWindow(currentMousePosition);
                }
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
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;

                MaximizeIcon_Normal.Visibility = Visibility.Visible;
                MaximizeIcon_Restore.Visibility = Visibility.Collapsed;
            }
            else
            {
                WindowState = WindowState.Maximized;

                MaximizeIcon_Normal.Visibility = Visibility.Collapsed;
                MaximizeIcon_Restore.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 恢复最大化窗口并开始拖动
        /// </summary>
        private void RestoreAndDragMaximizedWindow(Point currentMousePos)
        {
            _isDraggingMaximizedWindow = false;
            TitleBar.ReleaseMouseCapture();

            // 获取屏幕上的鼠标位置
            var screenPoint = PointToScreen(currentMousePos);

            // 计算恢复后窗口的合适大小（约为屏幕的75%）
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));
            double targetWidth = screen.WorkingArea.Width * 0.5;
            double targetHeight = screen.WorkingArea.Height * 0.75;

            // 计算鼠标在窗口宽度中的相对位置比例
            double relativeX = currentMousePos.X / ActualWidth;

            // 恢复到正常状态
            WindowState = WindowState.Normal;

            // 更新最大化按钮图标
            MaximizeIcon_Normal.Visibility = Visibility.Visible;
            MaximizeIcon_Restore.Visibility = Visibility.Collapsed;

            // 强制更新布局以获取新的窗口大小
            UpdateLayout();

            // 设置恢复后的窗口大小
            Width = targetWidth;
            Height = targetHeight;

            // 计算新的窗口位置，使鼠标保持在标题栏的相对位置
            double newLeft = screenPoint.X - (targetWidth * relativeX);
            double newTop = screenPoint.Y - currentMousePos.Y;

            // 确保窗口不会超出屏幕边界
            newLeft = Math.Max(screen.WorkingArea.Left, Math.Min(newLeft, screen.WorkingArea.Right - targetWidth));
            newTop = Math.Max(screen.WorkingArea.Top, Math.Min(newTop, screen.WorkingArea.Bottom - targetHeight));

            // 设置窗口位置
            Left = newLeft;
            Top = newTop;

            // 开始拖动窗口
            try
            {
                DragMove();
            }
            catch
            {
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
            ScheduleFitToWindowUpdate();
        }


        private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow(openFavorites: false);
        }

        private void OpenFavoritesPage()
        {
            OpenSettingsWindow(openFavorites: true);
        }

        private void OpenSettingsWindow(bool openFavorites)
        {
            var settingsWindow = new MenuInterface(ViewModel.Settings)
            {
                Owner = this
            };
            if (openFavorites)
            {
                settingsWindow.OpenFavoritesPage();
            }
            settingsWindow.ShowDialog();
            ThemeManager.Apply(ViewModel.Settings.Theme);
        }

        #region 打印功能

        /// <summary>
        /// 打印按钮点击事件
        /// </summary>
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            OpenPrintWindow();
        }

        /// <summary>
        /// 打印菜单项点击事件
        /// </summary>
        private void PrintMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenPrintWindow();
        }

        /// <summary>
        /// 打开打印窗口
        /// </summary>
        private void OpenPrintWindow()
        {
            if (ViewModel.DisplayImage == null)
            {
                System.Windows.MessageBox.Show("没有可打印的图片", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var printWindow = new PrintWindow(ViewModel.DisplayImage, ViewModel.CurrentImage?.FilePath)
                {
                    Owner = this
                };
                printWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开打印窗口失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        /// <summary>
        /// 窗口关闭事件 - 保存设置和清理资源
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {


            if (_isResizeFreezeActive)
            {
                MainLayout.Visibility = _mainLayoutVisibilityBeforeResize;
                CleanupResizeFreezeOverlay();
            }

            if (ViewModel.IsFullScreen)
            {
                ExitFullScreen();
            }

            // 停止全屏栏隐藏计时器
            _fullScreenBarHideTimer?.Stop();

            // 注销所有快捷键
            _hotKeyManager?.Dispose();
            _folderSwitchThumbnailCts?.Cancel();
            _folderSwitchThumbnailCts?.Dispose();
            _folderSwitchImageService.Dispose();

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


                MaximizeIcon_Normal.Visibility = Visibility.Visible;
                MaximizeIcon_Restore.Visibility = Visibility.Collapsed;
            }
            else
            {


                MaximizeIcon_Normal.Visibility = Visibility.Collapsed;
                MaximizeIcon_Restore.Visibility = Visibility.Visible;
            }

        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ctrl+P 打印快捷键
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.P)
            {
                OpenPrintWindow();
                e.Handled = true;
                return;
            }

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

        private sealed partial class FolderSwitchEntry : ObservableObject
        {
            public FolderSwitchEntry(string fullPath, string displayName, bool exists, bool isParent, string? coverPath)
            {
                FullPath = fullPath;
                DisplayName = displayName;
                Exists = exists;
                IsParent = isParent;
                CoverPath = coverPath;
            }

            public string FullPath { get; }
            public string DisplayName { get; }
            public bool Exists { get; }
            public bool IsParent { get; }
            public string? CoverPath { get; }

            [ObservableProperty]
            private BitmapSource? _coverThumbnail;

            [ObservableProperty]
            private bool _isCoverLoading;
        }

        private int GetFolderSwitchCoverSize()
        {
            var size = ViewModel.Settings?.ThumbnailSize ?? 48;
            return Math.Clamp(size, 40, 64);
        }

        private void FolderSwitchOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isFolderSwitchOpen)
                return;

            CloseFolderSwitchOverlay(restoreHotKeys: true);
        }

        private void FolderSwitchOverlayContent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isFolderSwitchOpen)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Tab)
                {
                    ShowFolderSwitchOverlay();
                    e.Handled = true;
                }

                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    CloseFolderSwitchOverlay(restoreHotKeys: true);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    e.Handled = true;
                    await CommitFolderSwitchSelectionAsync();
                    break;
                case Key.Left:
                    MoveFolderSwitchSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    MoveFolderSwitchSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveFolderSwitchSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Q:
                    e.Handled = true;
                    CloseFolderSwitchOverlay(restoreHotKeys: true);
                    break;
                case Key.Down:
                    MoveFolderSwitchSelection(1);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    MoveFolderSwitchSelection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                    e.Handled = true;
                    break;
            }
        }

        private async void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isFolderSwitchOpen)
                return;

            if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl)
                return;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                return;

            e.Handled = true;
            await CommitFolderSwitchSelectionAsync();
        }

        private void ShowFolderSwitchOverlay()
        {
            if (_isFolderSwitchOpen)
                return;

            var entries = BuildFavoriteFolderSwitchEntries();
            if (entries.Count == 0)
            {
                ViewModel.StatusMessage = "暂无收藏文件夹";
                return;
            }

            _folderSwitchEntries = entries;
            _folderSwitchBaseDirectory = string.Empty;
            _folderSwitchSelectedIndex = GetFavoriteFolderSelectionIndex(entries);
            _folderSwitchWindowStart = 0;

            FolderSwitchBasePathText.Text = $"共 {entries.Count} 个收藏文件夹";

            _isFolderSwitchOpen = true;
            FolderSwitchOverlay.Visibility = Visibility.Visible;

            _folderSwitchThumbnailCts?.Cancel();
            _folderSwitchThumbnailCts?.Dispose();
            _folderSwitchThumbnailCts = new CancellationTokenSource();

            // 暂停全局热键，避免方向键等在浮层中仍触发图片导航
            _hotKeyManager?.UnregisterAll();

            RefreshFolderSwitchVisibleItems();
            FolderSwitchListBox.Focus();
        }

        private void CloseFolderSwitchOverlay(bool restoreHotKeys)
        {
            if (!_isFolderSwitchOpen)
                return;

            _isFolderSwitchOpen = false;
            _isFolderSwitchCommitInProgress = false;

            FolderSwitchOverlay.Visibility = Visibility.Collapsed;
            FolderSwitchBasePathText.Text = string.Empty;

            FolderSwitchMoreLeft.Visibility = Visibility.Collapsed;
            FolderSwitchMoreRight.Visibility = Visibility.Collapsed;

            _folderSwitchVisibleEntries.Clear();
            _folderSwitchEntries = new List<FolderSwitchEntry>();
            _folderSwitchBaseDirectory = string.Empty;
            _folderSwitchSelectedIndex = 0;
            _folderSwitchWindowStart = 0;

            _folderSwitchThumbnailCts?.Cancel();
            _folderSwitchThumbnailCts?.Dispose();
            _folderSwitchThumbnailCts = null;

            if (restoreHotKeys && IsActive)
            {
                RegisterGlobalHotKeys();
            }
        }

        private static int GetFolderSwitchDefaultSelectionIndex(List<FolderSwitchEntry> entries)
        {
            if (entries.Count <= 0)
                return 0;

            if (entries[0].IsParent && entries.Count > 1)
                return 1;

            return 0;
        }

        private async Task CommitFolderSwitchSelectionAsync()
        {
            if (!_isFolderSwitchOpen || _isFolderSwitchCommitInProgress)
                return;

            _isFolderSwitchCommitInProgress = true;

            try
            {
                var entry = GetFolderSwitchSelectedEntry();
                CloseFolderSwitchOverlay(restoreHotKeys: true);

                if (entry == null)
                    return;

                if (!entry.Exists || (!Directory.Exists(entry.FullPath) && !File.Exists(entry.FullPath)))
                {
                    ViewModel.StatusMessage = "收藏路径不存在";
                    return;
                }

                if (Directory.Exists(entry.FullPath))
                {
                    await ViewModel.LoadFolder(entry.FullPath);
                }
                else
                {
                    await ViewModel.LoadImageFromPath(entry.FullPath);
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"切换目录失败: {ex.Message}";
            }
            finally
            {
                _isFolderSwitchCommitInProgress = false;
            }
        }

        private FolderSwitchEntry? GetFolderSwitchSelectedEntry()
        {
            if (_folderSwitchEntries.Count == 0)
                return null;

            if (_folderSwitchSelectedIndex < 0 || _folderSwitchSelectedIndex >= _folderSwitchEntries.Count)
                return null;

            return _folderSwitchEntries[_folderSwitchSelectedIndex];
        }

        private void MoveFolderSwitchSelection(int delta)
        {
            if (!_isFolderSwitchOpen || _folderSwitchEntries.Count == 0)
                return;

            var total = _folderSwitchEntries.Count;
            var nextIndex = _folderSwitchSelectedIndex + delta;

            if (nextIndex < 0)
                nextIndex = total - 1;
            else if (nextIndex >= total)
                nextIndex = 0;

            _folderSwitchSelectedIndex = nextIndex;

            EnsureFolderSwitchSelectionVisible();
            RefreshFolderSwitchVisibleItems();
        }

        private void SelectFolderSwitchParent()
        {
            if (!_isFolderSwitchOpen || _folderSwitchEntries.Count == 0)
                return;

            if (!_folderSwitchEntries[0].IsParent)
                return;

            _folderSwitchSelectedIndex = 0;
            EnsureFolderSwitchSelectionVisible();
            RefreshFolderSwitchVisibleItems();
        }

        private void SelectFolderSwitchFirstChild()
        {
            if (!_isFolderSwitchOpen || _folderSwitchEntries.Count == 0)
                return;

            var childIndex = _folderSwitchEntries[0].IsParent ? 1 : 0;
            if (childIndex >= _folderSwitchEntries.Count)
                return;

            _folderSwitchSelectedIndex = childIndex;
            EnsureFolderSwitchSelectionVisible();
            RefreshFolderSwitchVisibleItems();
        }

        private void BrowseFolderSwitchToParent()
        {
            if (!_isFolderSwitchOpen || string.IsNullOrWhiteSpace(_folderSwitchBaseDirectory))
                return;

            string? parentDirectory;
            try
            {
                parentDirectory = Directory.GetParent(_folderSwitchBaseDirectory)?.FullName;
            }
            catch
            {
                parentDirectory = null;
            }

            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
                return;

            var previousBaseDirectory = _folderSwitchBaseDirectory;

            _folderSwitchBaseDirectory = parentDirectory;
            FolderSwitchBasePathText.Text = parentDirectory;

            _folderSwitchEntries = BuildFolderSwitchEntries(parentDirectory);

            var targetIndex = -1;
            for (var i = 0; i < _folderSwitchEntries.Count; i++)
            {
                if (string.Equals(_folderSwitchEntries[i].FullPath, previousBaseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
            }

            _folderSwitchSelectedIndex = targetIndex >= 0
                ? targetIndex
                : GetFolderSwitchDefaultSelectionIndex(_folderSwitchEntries);

            _folderSwitchWindowStart = 0;
            RefreshFolderSwitchVisibleItems();
        }

        private void BrowseFolderSwitchIntoSelectedFolder()
        {
            if (!_isFolderSwitchOpen || _folderSwitchEntries.Count == 0)
                return;

            var entry = GetFolderSwitchSelectedEntry();
            if (entry == null)
                return;

            if (!Directory.Exists(entry.FullPath))
                return;

            var previousBaseDirectory = _folderSwitchBaseDirectory;
            var nextBaseDirectory = entry.FullPath;

            if (string.Equals(previousBaseDirectory, nextBaseDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            _folderSwitchBaseDirectory = nextBaseDirectory;
            FolderSwitchBasePathText.Text = nextBaseDirectory;

            _folderSwitchEntries = BuildFolderSwitchEntries(nextBaseDirectory);

            if (entry.IsParent && !string.IsNullOrWhiteSpace(previousBaseDirectory))
            {
                var targetIndex = -1;
                for (var i = 0; i < _folderSwitchEntries.Count; i++)
                {
                    if (string.Equals(_folderSwitchEntries[i].FullPath, previousBaseDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i;
                        break;
                    }
                }

                _folderSwitchSelectedIndex = targetIndex >= 0
                    ? targetIndex
                    : GetFolderSwitchDefaultSelectionIndex(_folderSwitchEntries);
            }
            else
            {
                _folderSwitchSelectedIndex = GetFolderSwitchDefaultSelectionIndex(_folderSwitchEntries);
            }

            _folderSwitchWindowStart = 0;
            RefreshFolderSwitchVisibleItems();
        }

        private void EnsureFolderSwitchSelectionVisible()
        {
            if (_folderSwitchEntries.Count == 0)
            {
                _folderSwitchWindowStart = 0;
                return;
            }

            var total = _folderSwitchEntries.Count;
            var windowSize = Math.Min(FolderSwitchMaxVisibleItems, total);

            if (_folderSwitchSelectedIndex < _folderSwitchWindowStart)
            {
                _folderSwitchWindowStart = _folderSwitchSelectedIndex;
            }
            else if (_folderSwitchSelectedIndex >= _folderSwitchWindowStart + windowSize)
            {
                _folderSwitchWindowStart = _folderSwitchSelectedIndex - (windowSize - 1);
            }

            _folderSwitchWindowStart = Math.Clamp(_folderSwitchWindowStart, 0, Math.Max(0, total - windowSize));
        }

        private void RefreshFolderSwitchVisibleItems()
        {
            _folderSwitchVisibleEntries.Clear();

            if (_folderSwitchEntries.Count == 0)
            {
                FolderSwitchMoreLeft.Visibility = Visibility.Collapsed;
                FolderSwitchMoreRight.Visibility = Visibility.Collapsed;
                return;
            }

            EnsureFolderSwitchSelectionVisible();

            var total = _folderSwitchEntries.Count;
            var windowSize = Math.Min(FolderSwitchMaxVisibleItems, total);
            var windowEndExclusive = Math.Min(total, _folderSwitchWindowStart + windowSize);

            for (var i = _folderSwitchWindowStart; i < windowEndExclusive; i++)
            {
                var entry = _folderSwitchEntries[i];
                _folderSwitchVisibleEntries.Add(entry);
                EnsureFolderSwitchCoverThumbnail(entry);
            }

            FolderSwitchMoreLeft.Visibility = _folderSwitchWindowStart > 0 ? Visibility.Visible : Visibility.Collapsed;
            FolderSwitchMoreRight.Visibility = windowEndExclusive < total ? Visibility.Visible : Visibility.Collapsed;

            FolderSwitchListBox.SelectedIndex = Math.Clamp(_folderSwitchSelectedIndex - _folderSwitchWindowStart, 0, windowSize - 1);
        }

        private void EnsureFolderSwitchCoverThumbnail(FolderSwitchEntry entry)
        {
            if (_folderSwitchThumbnailCts == null || _folderSwitchThumbnailCts.IsCancellationRequested)
            {
                return;
            }

            if (entry.CoverThumbnail != null || entry.IsCoverLoading)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.CoverPath) || !File.Exists(entry.CoverPath))
            {
                return;
            }

            _ = LoadFolderSwitchCoverThumbnailAsync(entry, _folderSwitchThumbnailCts.Token);
        }

        private async Task LoadFolderSwitchCoverThumbnailAsync(FolderSwitchEntry entry, CancellationToken token)
        {
            entry.IsCoverLoading = true;
            try
            {
                var coverPath = entry.CoverPath;
                if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
                {
                    return;
                }

                var imageInfo = ImageInfo.FromFile(coverPath);
                var thumbnail = await _folderSwitchImageService.LoadThumbnailAsync(imageInfo, GetFolderSwitchCoverSize(), token);
                if (thumbnail != null && !token.IsCancellationRequested)
                {
                    entry.CoverThumbnail = thumbnail;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                entry.IsCoverLoading = false;
            }
        }

        private static List<FolderSwitchEntry> BuildFolderSwitchEntries(string baseDirectory)
        {
            var entries = new List<FolderSwitchEntry>();

            try
            {
                var parent = Directory.GetParent(baseDirectory)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                {
                    entries.Add(new FolderSwitchEntry(parent, GetFolderDisplayName(parent), exists: true, isParent: true, coverPath: null));
                }
            }
            catch
            {
            }

            IEnumerable<string> subFolders;
            try
            {
                subFolders = Directory.EnumerateDirectories(baseDirectory);
            }
            catch
            {
                subFolders = Enumerable.Empty<string>();
            }

            foreach (var folderPath in subFolders
                         .Select(p => new { Path = p, Name = GetFolderDisplayName(p) })
                         .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(x => x.Path, StringComparer.CurrentCultureIgnoreCase)
                         .Select(x => x.Path))
            {
                entries.Add(new FolderSwitchEntry(folderPath, GetFolderDisplayName(folderPath), exists: true, isParent: false, coverPath: null));
            }

            return entries;
        }

        private List<FolderSwitchEntry> BuildFavoriteFolderSwitchEntries()
        {
            var entries = new List<FolderSwitchEntry>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var comparer = new NaturalStringComparer();
            var favoriteImages = ViewModel.Settings.Bookmarks
                .Where(b => b.Type == BookmarkType.Image && !string.IsNullOrWhiteSpace(b.FilePath))
                .Select(b => TryResolveFavoriteImagePath(b.FilePath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .ToList();

            foreach (var bookmark in ViewModel.Settings.Bookmarks
                         .Where(b => b.Type == BookmarkType.Folder && !string.IsNullOrWhiteSpace(b.FilePath))
                         .OrderByDescending(b => b.CreatedAt))
            {
                var folderPath = bookmark.FilePath;
                if (!unique.Add(folderPath))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(bookmark.Name)
                    ? GetFolderDisplayName(folderPath)
                    : bookmark.Name;
                var exists = Directory.Exists(folderPath) || File.Exists(folderPath);
                var coverPath = favoriteImages
                    .Where(path => IsPathUnderFolder(path, folderPath))
                    .OrderBy(path => GetFolderRelativePath(folderPath, path), comparer)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                entries.Add(new FolderSwitchEntry(folderPath, displayName, exists, isParent: false, coverPath));
            }

            return entries;
        }

        private static string? TryResolveFavoriteImagePath(string bookmarkKey)
        {
            if (string.IsNullOrWhiteSpace(bookmarkKey))
            {
                return null;
            }

            if (bookmarkKey.StartsWith("pdf:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (bookmarkKey.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return bookmarkKey;
        }

        private static bool IsPathUnderFolder(string filePath, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            try
            {
                var itemPath = Path.GetFullPath(filePath);
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

        private static string GetFolderRelativePath(string folderPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return filePath;
            }

            try
            {
                return Path.GetRelativePath(folderPath, filePath);
            }
            catch
            {
                return filePath;
            }
        }

        private int GetFavoriteFolderSelectionIndex(List<FolderSwitchEntry> entries)
        {
            if (entries.Count == 0)
            {
                return 0;
            }

            if (TryGetFolderSwitchBaseDirectory(out var currentFolder))
            {
                var index = entries.FindIndex(entry =>
                    string.Equals(entry.FullPath, currentFolder, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    return index;
                }
            }

            var firstExisting = entries.FindIndex(entry => entry.Exists);
            return firstExisting >= 0 ? firstExisting : 0;
        }

        private bool TryGetFolderSwitchBaseDirectory(out string baseDirectory)
        {
            baseDirectory = string.Empty;

            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentFolderPath))
            {
                if (Directory.Exists(ViewModel.CurrentFolderPath))
                {
                    baseDirectory = ViewModel.CurrentFolderPath;
                    return true;
                }

                if (File.Exists(ViewModel.CurrentFolderPath))
                {
                    baseDirectory = ViewModel.CurrentFolderPath;
                    return true;
                }
            }

            var currentImagePath = ViewModel.CurrentImage?.FilePath;
            if (!string.IsNullOrWhiteSpace(currentImagePath))
            {
                var dir = Path.GetDirectoryName(currentImagePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    baseDirectory = dir;
                    return true;
                }
            }

            return false;
        }

        private static string GetFolderDisplayName(string fullPath)
        {
            var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(name) ? fullPath : name;
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
            CloseFolderSwitchOverlay(restoreHotKeys: false);
            _hotKeyManager?.UnregisterAll();
        }

        /// <summary>
        /// 鼠标移动事件 - 用于自动隐藏光标
        /// </summary>
        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 处理最大化窗口的拖动
            if (_isDraggingMaximizedWindow && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentMousePosition = e.GetPosition(this);

                // 计算鼠标移动距离
                double deltaX = currentMousePosition.X - _dragStartMousePosition.X;
                double deltaY = currentMousePosition.Y - _dragStartMousePosition.Y;

                // 当鼠标移动超过一定阈值时，恢复窗口大小并开始拖动
                if (Math.Abs(deltaX) > 5 || Math.Abs(deltaY) > 5)
                {
                    RestoreAndDragMaximizedWindow(currentMousePosition);
                }
                return;
            }

            // 处理普通窗口拖动到顶部最大化
            if (_isNormalWindowDragging && e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
            {
                // 获取屏幕坐标
                var screenPoint = PointToScreen(e.GetPosition(this));
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));

                // 检查是否拖动到屏幕顶部（工作区顶部）
                if (screenPoint.Y <= screen.WorkingArea.Top + 5)
                {
                    // 最大化窗口
                    WindowState = WindowState.Maximized;
                    MaximizeIcon_Normal.Visibility = Visibility.Collapsed;
                    MaximizeIcon_Restore.Visibility = Visibility.Visible;
                    _isNormalWindowDragging = false;
                }
            }

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

            // 全屏模式下检测鼠标位置，自动显示缩略图栏 ===
            if (ViewModel.IsFullScreen)
            {
                HandleFullScreenBarVisibility(e.GetPosition(this));
            }
        }

        /// <summary>
        /// 处理全屏模式下缩略图栏的自动显示/隐藏
        /// </summary>
        private void HandleFullScreenBarVisibility(Point mousePos)
        {
            bool shouldShow = false;

            if (ViewModel.IsMangaMode && ViewModel.Settings.ShowSidebarInMangaFullScreen)
            {
                // 漫画模式：检测左侧边缘
                shouldShow = mousePos.X <= FullScreenBarTriggerZone;

                // 如果鼠标在侧边栏上方，也保持显示
                if (_isFullScreenBarVisible && mousePos.X <= ViewModel.Settings.SidebarWidth)
                {
                    shouldShow = true;
                }

                if (shouldShow)
                {
                    ShowFullScreenSidebar();
                }
                else if (_isFullScreenBarVisible)
                {
                    StartFullScreenBarHideTimer();
                }
            }
            else if (!ViewModel.IsMangaMode && ViewModel.Settings.ShowBottomBarInFullScreen)
            {
                // 单图/双页模式：检测底部边缘
                shouldShow = mousePos.Y >= ActualHeight - FullScreenBarTriggerZone;

                // 如果鼠标在底边栏上方，也保持显示
                if (_isFullScreenBarVisible && mousePos.Y >= ActualHeight - ViewModel.Settings.BottomBarHeight)
                {
                    shouldShow = true;
                }

                if (shouldShow)
                {
                    ShowFullScreenBottomBar();
                }
                else if (_isFullScreenBarVisible)
                {
                    StartFullScreenBarHideTimer();
                }
            }
        }
        /// <summary>
        /// 显示全屏底边栏
        /// </summary>
        private void ShowFullScreenBottomBar()
        {
            _fullScreenBarHideTimer.Stop();
            _isFullScreenBarVisible = true;

            FullScreenBottomBar.IsHitTestVisible = true;

            // 淡入动画
            var animation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            FullScreenBottomBar.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>
        /// 显示全屏侧边栏
        /// </summary>
        private void ShowFullScreenSidebar()
        {
            _fullScreenBarHideTimer.Stop();
            _isFullScreenBarVisible = true;

            FullScreenSidebar.IsHitTestVisible = true;

            // 淡入动画
            var animation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            FullScreenSidebar.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>
        /// 启动隐藏计时器
        /// </summary>
        private void StartFullScreenBarHideTimer()
        {
            _fullScreenBarHideTimer.Stop();
            _fullScreenBarHideTimer.Start();
        }


        /// <summary>
        /// 隐藏计时器触发
        /// </summary>
        private void FullScreenBarHideTimer_Tick(object? sender, EventArgs e)
        {
            _fullScreenBarHideTimer.Stop();
            HideFullScreenBars();
        }

        /// <summary>
        /// 隐藏全屏缩略图栏
        /// </summary>
        private void HideFullScreenBars()
        {
            _isFullScreenBarVisible = false;

            // 淡出动画
            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            animation.Completed += (s, e) =>
            {
                FullScreenBottomBar.IsHitTestVisible = false;
                FullScreenSidebar.IsHitTestVisible = false;
            };

            FullScreenBottomBar.BeginAnimation(OpacityProperty, animation);
            FullScreenSidebar.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>
        /// 检查鼠标是否在缩略图栏上
        /// </summary>
        private bool IsMouseOverThumbnailBar(Point mousePos)
        {
            // 检查非全屏底边栏
            if (BottomThumbnailBar.Visibility == Visibility.Visible)
            {
                var barTop = ActualHeight - ViewModel.Settings.BottomBarHeight;
                if (mousePos.Y >= barTop)
                    return true;
            }

            // 检查全屏底边栏
            if (FullScreenBottomBar.Visibility == Visibility.Visible &&
                FullScreenBottomBar.Opacity > 0 &&
                FullScreenBottomBar.IsHitTestVisible)
            {
                var barTop = ActualHeight - ViewModel.Settings.BottomBarHeight;
                if (mousePos.Y >= barTop)
                    return true;
            }

            // 检查漫画模式侧边栏
            if (MangaSidebar.Visibility == Visibility.Visible)
            {
                if (mousePos.X <= ViewModel.Settings.SidebarWidth)
                    return true;
            }

            // 检查全屏侧边栏
            if (FullScreenSidebar.Visibility == Visibility.Visible &&
                FullScreenSidebar.Opacity > 0 &&
                FullScreenSidebar.IsHitTestVisible)
            {
                if (mousePos.X <= ViewModel.Settings.SidebarWidth)
                    return true;
            }

            return false;
        }
        /// <summary>
        /// 鼠标滚轮预览事件
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 如果鼠标在底边栏或侧边栏上，不处理（让底边栏自己处理滚动）
            var mousePos = e.GetPosition(this);
            if (IsMouseOverThumbnailBar(mousePos))
            {
                return; // 让底边栏/侧边栏自己处理
            }

            bool ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            // Ctrl+滚轮始终是缩放
            if (ctrlPressed)
            {
                if (ViewModel.ShowWaterfallView)
                {
                    HandleWaterfallZoomWithMouseWheel(e);
                }
                else if (ViewModel.IsMangaMode)
                {
                    HandleMangaZoomWithMouseWheel(e);
                }
                else
                {
                    HandleZoomWithMouseWheel(e);
                }
                e.Handled = true;
                return;
            }

            // 非Ctrl时，根据设置决定行为
            if (ViewModel.Settings.ScrollWheelBehavior == ScrollWheelBehavior.Navigate)
            {
                // 翻页模式：非漫画模式下全局翻页
                if (!ViewModel.IsMangaMode && !ViewModel.ShowWaterfallView)
                {
                    ViewModel.HandleMouseWheel(e.Delta, ctrlPressed);
                    e.Handled = true;
                }
            }
            else if (ViewModel.Settings.ScrollWheelBehavior == ScrollWheelBehavior.Zoom)
            {
                // 缩放模式：在PreviewMouseWheel中处理全局缩放，避免被子控件拦截
                if (ViewModel.ShowWaterfallView)
                {
                    HandleWaterfallZoomWithMouseWheel(e);
                }
                else if (ViewModel.IsMangaMode)
                {
                    HandleMangaZoomWithMouseWheel(e);
                }
                else
                {
                    HandleZoomWithMouseWheel(e);
                }
                e.Handled = true;
            }
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

            Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
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
            // 如果鼠标在底边栏或侧边栏上，不处理（让底边栏自己处理滚动）
            var mousePos = e.GetPosition(this);
            if (IsMouseOverThumbnailBar(mousePos))
            {
                return; // 底边栏/侧边栏已经通过PreviewMouseWheel处理了滚动
            }

            bool ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            if (ViewModel.ShowWaterfallView)
            {
                if (ctrlPressed)
                {
                    HandleWaterfallZoomWithMouseWheel(e);
                    e.Handled = true;
                }
                return;
            }

            bool forceZoom = ctrlPressed || ViewModel.Settings.ScrollWheelBehavior == ScrollWheelBehavior.Zoom;

            if (forceZoom)
            {
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
            else
            {
                // 翻页行为：全局生效
                ViewModel.HandleMouseWheel(e.Delta, ctrlPressed);
                e.Handled = true; // 标记事件已处理
            }
        }

        /// <summary>
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

        /// <summary>
        /// </summary>
        private void HandleWaterfallZoomWithMouseWheel(MouseWheelEventArgs e)
        {
            const double ZOOM_FACTOR = 0.1;
            const double MIN_ZOOM = 0.2;
            const double MAX_ZOOM = 3.0; // 限制最大图尺寸

            if (ViewModel.Images.Count == 0)
                return;

            Point mousePos = e.GetPosition(WaterfallScrollViewer);

            double horizontalRatio = 0;
            double verticalRatio = 0;

            if (WaterfallScrollViewer.ViewportWidth > 0 && WaterfallScrollViewer.ViewportHeight > 0)
            {
                horizontalRatio = (WaterfallScrollViewer.HorizontalOffset + mousePos.X) / WaterfallScrollViewer.ExtentWidth;
                verticalRatio = (WaterfallScrollViewer.VerticalOffset + mousePos.Y) / WaterfallScrollViewer.ExtentHeight;
            }

            double currentZoom = ViewModel.WaterfallZoomLevel;
            double delta = e.Delta > 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
            double newZoom = currentZoom * (1 + delta);

            newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, newZoom));

            if (Math.Abs(newZoom - currentZoom) < 0.001)
                return;

            ViewModel.WaterfallZoomLevel = newZoom;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WaterfallScrollViewer.ExtentWidth > 0 && WaterfallScrollViewer.ExtentHeight > 0)
                {
                    double newHorizontalOffset = horizontalRatio * WaterfallScrollViewer.ExtentWidth - mousePos.X;
                    double newVerticalOffset = verticalRatio * WaterfallScrollViewer.ExtentHeight - mousePos.Y;

                    newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, WaterfallScrollViewer.ScrollableWidth));
                    newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, WaterfallScrollViewer.ScrollableHeight));

                    WaterfallScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                    WaterfallScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 瀑布流视图空白处点击：取消所有选择
        /// </summary>
        private void WaterfallScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ViewModel.ShowWaterfallView)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (source == null)
                return;

            // 点击在滚动条/滑块上，不清除选择
            if (FindVisualAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null ||
                FindVisualAncestor<Thumb>(source) != null ||
                FindVisualAncestor<RepeatButton>(source) != null)
            {
                return;
            }

            // 点击在图片项上，不清除选择
            var itemContainer = FindVisualAncestor<FrameworkElement>(source, fe => fe.Tag is ImageInfo);
            if (itemContainer != null)
                return;

            foreach (var img in ViewModel.Images)
            {
                if (img.IsSelected)
                    img.IsSelected = false;
            }
        }

        private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static FrameworkElement? FindVisualAncestor<FrameworkElement>(DependencyObject source, Func<FrameworkElement, bool> predicate)
            where FrameworkElement : System.Windows.FrameworkElement
        {
            var current = source;
            while (current != null)
            {
                if (current is System.Windows.FrameworkElement fe && predicate((FrameworkElement)fe))
                    return (FrameworkElement)fe;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                await ViewModel.HandleFileDrop(files);
            }
        }

        private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        }

        #endregion

        #region  ViewModel 属性变化处理
        /// <summary>
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
                    // 图片加载完成后，触发淡入动画
                    StartImageFadeIn();
                    RequestCenterMangaCurrentImage();
                    goto case nameof(MainViewModel.FitToWindow);
                case nameof(MainViewModel.SecondDisplayImage):
                    // 图片加载完成后，触发淡入动画
                    StartImageFadeIn();
                    goto case nameof(MainViewModel.FitToWindow);
                case nameof(MainViewModel.CurrentViewMode):
                case nameof(MainViewModel.FitToWindow):
                case nameof(MainViewModel.ZoomLevel):
                    // 查看模式改变时，重新计算缩放
                    ScheduleFitToWindowUpdate();
                    RequestCenterMangaCurrentImage();
                    break;
                case nameof(MainViewModel.CurrentIndex):
                    RequestCenterMangaCurrentImage();
                    break;
                case nameof(MainViewModel.IsSlideShowActive):
                    // 适应窗口选项改变时，重新计算缩放
                    HandleSlideShowChange();
                    RequestCenterMangaCurrentImage();
                    break;
            }
        }

        private void RequestCenterMangaCurrentImage()
        {
            if (!ViewModel.IsMangaMode || ViewModel.ShowWaterfallView)
            {
                return;
            }

            if (_mangaCenterPending)
            {
                return;
            }

            _mangaCenterPending = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _mangaCenterPending = false;
                CenterMangaImageAtViewportCenter(ViewModel.CurrentIndex, attempt: 0);
            }), DispatcherPriority.Loaded);
        }

        private void CenterMangaImageAtViewportCenter(int index, int attempt)
        {
            if (!ViewModel.IsMangaMode || ViewModel.ShowWaterfallView)
            {
                return;
            }

            if (index != ViewModel.CurrentIndex)
            {
                return;
            }

            if (index < 0 || index >= ViewModel.Images.Count)
            {
                return;
            }

            if (!MangaScrollViewer.IsLoaded || !MangaItemsControl.IsLoaded || !MangaScrollViewer.IsVisible)
            {
                RetryCenterMangaImage(index, attempt);
                return;
            }

            if (MangaScrollViewer.ViewportHeight <= 0 || MangaScrollViewer.ExtentHeight <= 0)
            {
                RetryCenterMangaImage(index, attempt);
                return;
            }

            MangaItemsControl.UpdateLayout();
            var container = MangaItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container == null || container.ActualHeight <= 0 || container.ActualWidth <= 0)
            {
                RetryCenterMangaImage(index, attempt);
                return;
            }

            try
            {
                var centerInViewer = container.TransformToVisual(MangaScrollViewer)
                    .Transform(new Point(container.ActualWidth / 2, container.ActualHeight / 2));

                var targetOffset = MangaScrollViewer.VerticalOffset + (centerInViewer.Y - MangaScrollViewer.ViewportHeight / 2);
                targetOffset = Math.Max(0, Math.Min(targetOffset, MangaScrollViewer.ScrollableHeight));

                MangaScrollViewer.ScrollToVerticalOffset(targetOffset);
            }
            catch
            {
                RetryCenterMangaImage(index, attempt);
            }
        }

        private void RetryCenterMangaImage(int index, int attempt)
        {
            const int maxAttempts = 8;
            if (attempt >= maxAttempts)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterMangaImageAtViewportCenter(index, attempt + 1);
            }), DispatcherPriority.Loaded);
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
            // 1. 保存当前窗口的所有状态
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousTopmost = Topmost;

            // 2. 保存窗口位置和大小
            if (WindowState == WindowState.Normal)
            {
                _previousLeft = Left;
                _previousTop = Top;
                _previousWidth = Width;
                _previousHeight = Height;
            }
            else
            {
                _previousLeft = RestoreBounds.Left;
                _previousTop = RestoreBounds.Top;
                _previousWidth = RestoreBounds.Width;
                _previousHeight = RestoreBounds.Height;
            }

            _previousShowStatusBar = ViewModel.Settings.ShowStatusBar;
            _previousShowSidebar = ViewModel.Settings.ShowSidebar;
            ViewModel.Settings.ShowStatusBar = false;
            ViewModel.Settings.ShowSidebar = false;

            // 4. 获取当前窗口所在的屏幕
            var currentScreen = GetCurrentScreen();

            // 5. 设置真正的全屏（覆盖任务栏）
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true; // 置顶确保覆盖任务栏

            // 6. 使用屏幕的完整区域
            Left = currentScreen.Bounds.Left;
            Top = currentScreen.Bounds.Top;
            Width = currentScreen.Bounds.Width;
            Height = currentScreen.Bounds.Height;

            // 重置全屏缩略图栏状态
            _isFullScreenBarVisible = false;
            _fullScreenBarHideTimer.Stop();
            FullScreenBottomBar.Opacity = 0;
            FullScreenBottomBar.IsHitTestVisible = false;
            FullScreenSidebar.Opacity = 0;
            FullScreenSidebar.IsHitTestVisible = false;

        }


        /// <summary>
        /// 退出全屏模式
        /// </summary>
        private void ExitFullScreen()
        {
            // 1. 恢复窗口属性
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            Topmost = _previousTopmost;

            // 2. 恢复窗口位置和大小
            Left = _previousLeft;
            Top = _previousTop;
            Width = _previousWidth;
            Height = _previousHeight;

            // 3. 恢复窗口状态
            WindowState = _previousWindowState;

            ViewModel.Settings.ShowStatusBar = _previousShowStatusBar;
            ViewModel.Settings.ShowSidebar = _previousShowSidebar;

            // 隐藏全屏缩略图栏
            _fullScreenBarHideTimer.Stop();
            HideFullScreenBars();
        }
        /// <summary>
        /// 获取窗口当前所在的屏幕
        /// </summary>
        private Screen GetCurrentScreen()
        {
            // 获取窗口中心点的屏幕坐标
            var centerX = Left + Width / 2;
            var centerY = Top + Height / 2;
            var centerPoint = new System.Drawing.Point((int)centerX, (int)centerY);

            // 找到包含此点的屏幕
            return Screen.FromPoint(centerPoint);
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
        private void ImageContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 只有在拖拽状态下才处理移动
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ImageScrollViewer);
                var delta = currentPosition - _lastMousePosition;
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
        {
            Dispatcher.BeginInvoke(new Action(UpdateFitToWindowZoom), DispatcherPriority.Background);
        }
        static double GetImageDipWidth(BitmapSource source)
        {
            if (source == null)
                return 0;

            var dpiX = source.DpiX;
            if (!double.IsFinite(dpiX) || dpiX <= 0)
                dpiX = 96;

            return source.PixelWidth * 96.0 / dpiX;
        }

        static double GetImageDipHeight(BitmapSource source)
        {
            if (source == null)
                return 0;

            var dpiY = source.DpiY;
            if (!double.IsFinite(dpiY) || dpiY <= 0)
                dpiY = 96;

            return source.PixelHeight * 96.0 / dpiY;
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


            // 获取图片显示大小（DIP），避免 DPI 元数据导致的缩放偏差
            double imageWidth = GetImageDipWidth(ViewModel.DisplayImage);
            double imageHeight = GetImageDipHeight(ViewModel.DisplayImage);
            // 如果是双页模式，需要考虑第二张图片
            if (ViewModel.IsDoublePage && ViewModel.SecondDisplayImage != null)
            {
                imageWidth += GetImageDipWidth(ViewModel.SecondDisplayImage) + 8;
                imageHeight = Math.Max(imageHeight, GetImageDipHeight(ViewModel.SecondDisplayImage));
            }

            if (imageWidth <= 0 || imageHeight <= 0)
                return;
            // 计算适应窗口的缩放比例（取宽度和高度缩放比例的较小值）
            var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
            // 设置缩放级别
            if (double.IsFinite(scale) && scale > 0)
            {
                if (Math.Abs(scale - ViewModel.ZoomLevel) < 0.001)
                {
                    return;
                }

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

        #region 缩略图列表Thumbnail List
        /// <summary>
        /// 缩略图列表选择改变事件 - 切换到选中的图片
        /// </summary>
        private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as System.Windows.Controls.ListBox;
            if (listBox == null) return;

            // 确保选中项滚动到可见区域
            if (listBox.SelectedItem != null)
            {
                listBox.ScrollIntoView(listBox.SelectedItem);
            }

            // 这个事件主要用于确保滚动到选中项
            // 但我们添加手动更新以确保绑定正常工作
            if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < ViewModel.Images.Count)
            {
                if (ViewModel.CurrentIndex != listBox.SelectedIndex)
                {
                    ViewModel.CurrentIndex = listBox.SelectedIndex;
                }
            }
        }
        /// <summary>
        /// 缩略图列表滚轮事件 - 实现水平滚动（用于旧版兼容）
        /// </summary>
        private void ThumbnailList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox listBox)
                return;

            // 查找ListBox内的ScrollViewer
            var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer == null)
                return;

            // 水平滚动（滚轮向上滚动时向左，向下滚动时向右）
            double scrollAmount = e.Delta > 0 ? -60 : 60;
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);

            e.Handled = true; // 阻止事件继续冒泡
        }

        /// <summary>
        /// 底边栏横向缩略图列表滚轮事件 - 实现平滑水平滚动
        /// </summary>
        private void BottomThumbnailList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox listBox)
                return;

            // 查找ListBox内的ScrollViewer
            var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer == null)
                return;

            // 使用与漫画模式侧边栏类似的平滑滚动量
            // 每个缩略图约108像素宽（100宽度+8边距），滚动约2-3个缩略图的距离
            double scrollAmount = e.Delta > 0 ? -240 : 240;

            // 计算新的滚动位置
            double newOffset = scrollViewer.HorizontalOffset + scrollAmount;

            // 限制在有效范围内
            newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableWidth));

            scrollViewer.ScrollToHorizontalOffset(newOffset);

            e.Handled = true; // 阻止事件继续冒泡，防止触发翻页
        }

        /// <summary>
        /// 查找视觉树中的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found)
                    return found;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }


        #endregion

        #region 瀑布流视图 Waterfall View
        /// <summary>
        /// 瀑布流项点击事件 - 选择图片并切换到单图模式
        /// </summary>
        private void WaterfallItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not ImageInfo imageInfo)
                return;

            // 找到图片在集合中的索引
            var index = ViewModel.Images.IndexOf(imageInfo);
            if (index < 0)
                return;

            var modifiers = Keyboard.Modifiers;
            bool ctrlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (ctrlPressed)
            {
                imageInfo.IsSelected = !imageInfo.IsSelected;
                e.Handled = true;
                return;
            }

            if (shiftPressed && imageInfo.IsSelected)
            {
                imageInfo.IsSelected = false;
                e.Handled = true;
                return;
            }

            // 漫画模式普通点击：只切换当前图片，不离开漫画模式，也不产生选中描边
            if (ViewModel.IsMangaMode && !ViewModel.ShowWaterfallView)
            {
                if (ViewModel.CurrentIndex != index)
                {
                    ViewModel.CurrentIndex = index;
                }
                return;
            }

            // 瀑布流普通点击：清除所有复选并跳转到单图模式
            foreach (var img in ViewModel.Images)
            {
                if (img.IsSelected)
                {
                    img.IsSelected = false;
                }
            }

            ViewModel.SelectFromWaterfallCommand.Execute(index);
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

            ViewModel.ScrollOffset = viewportTop;
        }

        #endregion


    }
}