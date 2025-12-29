using ImageViewer.Helpers;
using ImageViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ImageViewer.Controls
{

    /// <summary>
    /// 文件夹快速导航浮层控件
    /// 类似 Alt+Tab 的体验，按住 Ctrl 时方向键导航，松开 Ctrl 或按 Enter 确认
    /// </summary>
    public partial class FolderNavigationPopup : UserControl
    {
        #region 事件

        /// <summary>
        /// 选择了文件夹时触发
        /// </summary>
        public event EventHandler<string>? FolderSelected;

        /// <summary>
        /// 取消导航时触发
        /// </summary>
        public event EventHandler? Cancelled;

        #endregion

        #region 字段

        private readonly ObservableCollection<FolderItem> _folders = new();
        private string _currentPath = string.Empty;
        private int _selectedIndex = -1;
        private bool _isCtrlPressed;
        private CancellationTokenSource? _iconLoadCts;
        private string _filterText = string.Empty;
        private List<FolderItem> _allFolders = new(); // 用于过滤
        private int _columns = 4; // 每行显示的列数
                                  // 用于判断是使用哪种快捷键模式
        private bool _useCtrlReleaseMode = false; // 如果是 Ctrl+Tab 模式，设为 true

        private enum NavigationMode
        {
            CurrentLevel,  // 当前级别（默认）
            ParentLevel,   // 父级文件夹
            ChildLevel     // 子级文件夹
        }
        private NavigationMode _currentMode = NavigationMode.CurrentLevel;
        #endregion

        #region 构造函数

        public FolderNavigationPopup()
        {
            InitializeComponent();
            FolderItemsControl.ItemsSource = _folders;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 显示导航浮层
        /// </summary>
        /// <param name="currentFolderPath">当前文件夹路径</param>
        public  async Task ShowAsync(string currentFolderPath, bool useCtrlReleaseMode = false)
        {
            if (string.IsNullOrEmpty(currentFolderPath))
            {
                return;
            }

            _currentPath = currentFolderPath;
            _isCtrlPressed = true;
            _filterText = string.Empty;
            _useCtrlReleaseMode = useCtrlReleaseMode;
            _isCtrlPressed = useCtrlReleaseMode; // 如果是 Ctrl+Tab 模式，初始认为 Ctrl 被按下
            // 更新当前路径显示
            CurrentPathText.Text = currentFolderPath;

            // 加载文件夹列表
            await LoadFoldersAsync(currentFolderPath);

            // 显示控件并获取焦点
            Visibility = Visibility.Visible;
            Focusable = true;
            Focus();
            Keyboard.Focus(this);

            // 淡入动画
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fadeIn);

            // 默认选中第一项（返回上级）
            if (_folders.Count > 0)
            {
                SelectIndex(0);
            }
            // 更新操作提示
            UpdateHintText();
        }

        /// <summary>
        /// 隐藏导航浮层
        /// </summary>
        public void Hide()
        {
            _iconLoadCts?.Cancel();

            // 淡出动画
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
            fadeOut.Completed += (s, e) =>
            {
                Visibility = Visibility.Collapsed;
                _folders.Clear();
                _allFolders.Clear();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        #endregion

        #region 文件夹加载

        /// <summary>
        /// 加载指定路径的文件夹列表
        /// </summary>
        private async Task LoadFoldersAsync(string path,bool excludeParentItem = false)
        {
            _iconLoadCts?.Cancel();
            _iconLoadCts = new CancellationTokenSource();
            var ct = _iconLoadCts.Token;

            _folders.Clear();
            _allFolders.Clear();

            try
            {
                // 添加"返回上级"项
                var parentPath = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentPath) && Directory.Exists(parentPath))
                {
                    var parentItem = FolderItem.CreateParentFolder(parentPath);
                    parentItem.Icon = ShellIconHelper.GetParentFolderIcon();
                    _folders.Add(parentItem);
                    _allFolders.Add(parentItem);
                }
                if (!excludeParentItem && !string.IsNullOrEmpty(parentPath))
                {
                    _folders.Add(FolderItem.CreateParentFolder(parentPath));
                }
                // 获取子文件夹
                var subDirs = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.GetDirectories(path)
                            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    catch
                    {
                        return new List<string>();
                    }
                }, ct);

                if (ct.IsCancellationRequested) return;

                // 添加子文件夹项
                foreach (var dir in subDirs)
                {
                    if (ct.IsCancellationRequested) break;

                    var folderName = Path.GetFileName(dir);
                    // 跳过隐藏文件夹和系统文件夹
                    if (folderName.StartsWith(".") || folderName.StartsWith("$"))
                        continue;

                    var item = FolderItem.CreateFolder(dir, folderName);
                    item.Icon = ShellIconHelper.GetGenericFolderIcon(); // 先显示通用图标
                    _folders.Add(item);
                    _allFolders.Add(item);
                }

                // 更新空状态
                UpdateEmptyState();

                // 异步加载真实图标
                _ = LoadIconsAsync(ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步加载文件夹图标
        /// </summary>
        private async Task LoadIconsAsync(CancellationToken ct)
        {
            foreach (var item in _folders.Where(f => !f.IsParentFolder && f.IsLoadingIcon))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var icon = await ShellIconHelper.GetFolderIconAsync(item.Path);
                    if (!ct.IsCancellationRequested)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            item.Icon = icon;
                            item.IsLoadingIcon = false;
                        });
                    }
                }
                catch
                {
                    // 忽略单个图标加载失败
                }

                // 稍微延迟避免 UI 卡顿
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 更新空状态显示
        /// </summary>
        private void UpdateEmptyState()
        {
            var hasOnlyParent = _folders.Count <= 1 && _folders.All(f => f.IsParentFolder);
            EmptyState.Visibility = hasOnlyParent ? Visibility.Visible : Visibility.Collapsed;
        }
        /// <summary>
        /// 更新操作提示文本
        /// </summary>
        private void UpdateHintText()
        {
            // 根据模式显示不同的提示
            if (_useCtrlReleaseMode)
            {
                // Ctrl+Tab 模式
                HintText.Text = "方向键移动选择  •  Enter进入  •  Esc取消  •  松开Ctrl确认";
            }
            else
            {
                // Ctrl+G 等其他模式
                HintText.Text = "方向键移动选择  •  Enter进入  •  Esc取消";
            }
        }
        #endregion

        #region 键盘事件处理

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyDown(e);
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyDown(e);
        }

        private void HandleKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Tab:
                    // Ctrl+Tab 或 Tab 移动选择
                    if (_isCtrlPressed || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                            MovePrevious();
                        else
                            MoveNext();
                    }
                    e.Handled = true;
                    break;

                case Key.Left:
                    MovePrevious();
                    e.Handled = true;
                    break;

                case Key.Right:
                    MoveNext();
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // Ctrl+Tab+Up：切换到父级模式
                         SwitchToParentLevel();
                    }
                    else
                    {
                        MoveUp(); // 原有向上移动
                      
                    }
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // Ctrl+Tab+Down：切换到子级模式
                         SwitchToChildLevel();
                    }
                    else
                    {
                        MoveDown(); // 原有向下移动
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    ConfirmSelection();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    Cancel();
                    e.Handled = true;
                    break;

                case Key.LeftCtrl:
                case Key.RightCtrl:
                    _isCtrlPressed = true;
                    e.Handled = true;
                    break;

                default:
                    // 字母键用于快速过滤
                    if (e.Key >= Key.A && e.Key <= Key.Z)
                    {
                        var ch = (char)('a' + (e.Key - Key.A));
                        FilterByFirstLetter(ch);
                        e.Handled = true;
                    }
                    break;
            }
        }
        private async Task SwitchToParentLevel()
        {
            var parentPath = Path.GetDirectoryName(_currentPath);
            if (string.IsNullOrEmpty(parentPath)) return;

            _currentMode = NavigationMode.ParentLevel;
            UpdateHintText(); // 更新提示："正在查看上级文件夹"

            // 重新加载父级的所有文件夹（不含返回上级项）
            await LoadFoldersAsync(parentPath, excludeParentItem: true);
        }

        private async Task SwitchToChildLevel()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _folders.Count)
                return;

            var selectedFolder = _folders[_selectedIndex];
            if (selectedFolder.IsParentFolder) return;

            _currentMode = NavigationMode.ChildLevel;
            UpdateHintText(); // 更新提示："正在查看子文件夹"

            // 加载选中文件夹的子文件夹
            await LoadFoldersAsync(selectedFolder.Path, excludeParentItem: false);
        }
        private void UserControl_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            HandleKeyUp(e);
        }

        private void UserControl_KeyUp(object sender, KeyEventArgs e)
        {
            HandleKeyUp(e);
        }

        private void HandleKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                // 松开 Ctrl 确认选择
                _isCtrlPressed = false;
                // 只在 Ctrl+Tab 模式下，松开 Ctrl 才确认选择
                if (_useCtrlReleaseMode)
                {
                    ConfirmSelection();
                }
                ConfirmSelection();
                e.Handled = true;
            }
        }

        #endregion

        #region 导航逻辑

        /// <summary>
        /// 移动到下一项
        /// </summary>
        private void MoveNext()
        {
            if (_folders.Count == 0) return;
            var newIndex = (_selectedIndex + 1) % _folders.Count;
            SelectIndex(newIndex);
        }

        /// <summary>
        /// 移动到上一项
        /// </summary>
        private void MovePrevious()
        {
            if (_folders.Count == 0) return;
            var newIndex = _selectedIndex - 1;
            if (newIndex < 0) newIndex = _folders.Count - 1;
            SelectIndex(newIndex);
        }

        /// <summary>
        /// 移动到上一行
        /// </summary>
        private void MoveUp()
        {
            if (_folders.Count == 0) return;
            var newIndex = _selectedIndex - _columns;
            if (newIndex < 0)
            {
                // 跳到最后一行对应位置
                var lastRowStart = (_folders.Count - 1) / _columns * _columns;
                newIndex = Math.Min(lastRowStart + (_selectedIndex % _columns), _folders.Count - 1);
            }
            SelectIndex(newIndex);
        }

        /// <summary>
        /// 移动到下一行
        /// </summary>
        private void MoveDown()
        {
            if (_folders.Count == 0) return;
            var newIndex = _selectedIndex + _columns;
            if (newIndex >= _folders.Count)
            {
                // 跳到第一行对应位置
                newIndex = _selectedIndex % _columns;
                if (newIndex >= _folders.Count) newIndex = 0;
            }
            SelectIndex(newIndex);
        }

        /// <summary>
        /// 选中指定索引
        /// </summary>
        private void SelectIndex(int index)
        {
            if (index < 0 || index >= _folders.Count) return;

            // 取消之前的选中
            if (_selectedIndex >= 0 && _selectedIndex < _folders.Count)
            {
                _folders[_selectedIndex].IsSelected = false;
            }

            // 设置新选中
            _selectedIndex = index;
            _folders[_selectedIndex].IsSelected = true;

            // 确保选中项可见
            EnsureItemVisible(index);
        }

        /// <summary>
        /// 确保指定项在视口中可见
        /// </summary>
        private void EnsureItemVisible(int index)
        {
            if (FolderItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }

        /// <summary>
        /// 按首字母过滤
        /// </summary>
        private void FilterByFirstLetter(char letter)
        {
            // 查找第一个匹配的项
            var matchIndex = _folders
                .Select((item, idx) => new { item, idx })
                .FirstOrDefault(x => !x.item.IsParentFolder &&
                                     x.item.DisplayName.StartsWith(letter.ToString(), StringComparison.OrdinalIgnoreCase))
                ?.idx ?? -1;

            if (matchIndex >= 0)
            {
                SelectIndex(matchIndex);
            }
        }

        /// <summary>
        /// 确认选择
        /// </summary>
        private void ConfirmSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _folders.Count)
            {
                Cancel();
                return;
            }

            var selectedFolder = _folders[_selectedIndex];
            var path = selectedFolder.Path;

            Hide();
            FolderSelected?.Invoke(this, path);
        }

        /// <summary>
        /// 取消导航
        /// </summary>
        private void Cancel()
        {
            Hide();
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region UI 事件处理

        /// <summary>
        /// 鼠标点击文件夹项
        /// </summary>
        private void FolderItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FolderItem item)
            {
                var index = _folders.IndexOf(item);
                if (index >= 0)
                {
                    SelectIndex(index);

                    // 双击直接进入
                    if (e.ClickCount == 2)
                    {
                        ConfirmSelection();
                    }
                    // 单击选中后，如果不在 Ctrl+Tab 模式或 Ctrl 未按下，直接确认
                    else if (!_useCtrlReleaseMode || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        ConfirmSelection();
                    }
                }
            }
        }

        /// <summary>
        /// 过滤文本框内容改变
        /// </summary>
        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterText = FilterTextBox.Text.ToLowerInvariant();
            ApplyFilter();
        }

        /// <summary>
        /// 应用过滤
        /// </summary>
        private void ApplyFilter()
        {
            _folders.Clear();

            var filtered = string.IsNullOrEmpty(_filterText)
                ? _allFolders
                : _allFolders.Where(f => f.IsParentFolder ||
                                         f.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                             .ToList();

            foreach (var item in filtered)
            {
                _folders.Add(item);
            }

            UpdateEmptyState();

            // 重新选中第一项
            if (_folders.Count > 0)
            {
                SelectIndex(0);
            }
            else
            {
                _selectedIndex = -1;
            }
        }

        /// <summary>
        /// 滚轮滚动
        /// </summary>
        private void FolderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        #endregion
    }
}