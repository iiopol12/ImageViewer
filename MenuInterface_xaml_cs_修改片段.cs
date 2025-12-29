// =====================================================
// MenuInterface.xaml.cs 修改部分
// 
// 以下是需要添加到 MenuInterface.xaml.cs 的代码。
// =====================================================

// ========== 1. 在构造函数中添加事件处理（约第98行附近） ==========

// 在现有的 ValueChanged 事件处理之后添加：
BottomBarHeightSlider.ValueChanged += (s, e) => BottomBarHeightText.Text = ((int)e.NewValue).ToString();

// ========== 2. 在 LoadSettings() 方法中添加（约第210行附近） ==========

// 在现有设置加载代码末尾添加：

// 全屏模式缩略图栏设置
ShowBottomBarInFullScreenCheckBox.IsChecked = _settings.ShowBottomBarInFullScreen;
ShowSidebarInMangaFullScreenCheckBox.IsChecked = _settings.ShowSidebarInMangaFullScreen;
BottomBarHeightSlider.Value = _settings.BottomBarHeight;
BottomBarHeightText.Text = ((int)_settings.BottomBarHeight).ToString();

// ========== 3. 在 SaveButton_Click() 方法中添加（保存设置部分） ==========

// 在现有设置保存代码中添加：

// 全屏模式缩略图栏设置
_settings.ShowBottomBarInFullScreen = ShowBottomBarInFullScreenCheckBox.IsChecked ?? true;
_settings.ShowSidebarInMangaFullScreen = ShowSidebarInMangaFullScreenCheckBox.IsChecked ?? true;
_settings.BottomBarHeight = BottomBarHeightSlider.Value;


// =====================================================
// 完整的 LoadSettings() 方法示例（包含新增部分）
// =====================================================

private void LoadSettings()
{
    // 原有代码...
    foreach (ComboBoxItem item in ViewModeComboBox.Items)
    {
        if (item.Tag is ViewMode mode && mode == _settings.DefaultViewMode)
        {
            ViewModeComboBox.SelectedItem = item;
            break;
        }
    }

    foreach (ComboBoxItem item in ThemeComboBox.Items)
    {
        if (item.Tag is AppTheme theme && theme == _settings.Theme)
        {
            ThemeComboBox.SelectedItem = item;
            break;
        }
    }

    foreach (ComboBoxItem item in ScrollWheelComboBox.Items)
    {
        if (item.Tag is ScrollWheelBehavior behavior && behavior == _settings.ScrollWheelBehavior)
        {
            ScrollWheelComboBox.SelectedItem = item;
            break;
        }
    }

    // 幻灯片设置
    IntervalSlider.Value = _settings.SlideshowInterval;
    IntervalText.Text = _settings.SlideshowInterval.ToString();
    ShuffleSlideshowCheckBox.IsChecked = _settings.SlideshowShuffle;

    // 性能设置
    PreloadSlider.Value = _settings.PreloadCount;
    PreloadText.Text = _settings.PreloadCount.ToString();
    ThumbnailSlider.Value = _settings.ThumbnailSize;
    ThumbnailText.Text = _settings.ThumbnailSize.ToString();

    // 漫画模式设置
    MangaGapSlider.Value = _settings.MangaGap;
    MangaGapText.Text = ((int)_settings.MangaGap).ToString();
    MangaDecodeSlider.Value = _settings.MangaDecodeWidth;
    MangaDecodeText.Text = _settings.MangaDecodeWidth.ToString();

    // 行为设置
    RememberPositionCheckBox.IsChecked = _settings.RememberWindowPosition;
    RememberReadingCheckBox.IsChecked = _settings.RememberReadingPosition;
    FreezeDuringResizeCheckBox.IsChecked = _settings.FreezeDuringResize;
    ShowStatusBarCheckBox.IsChecked = _settings.ShowStatusBar;

    // 子文件夹扫描
    ScanSubfoldersCheckBox.IsChecked = _settings.ScanSubfoldersEnabled;
    foreach (ComboBoxItem item in ScanSubfoldersDepthComboBox.Items)
    {
        if (item.Tag is string depthStr && int.TryParse(depthStr, out int depth) && depth == _settings.ScanSubfoldersDepth)
        {
            ScanSubfoldersDepthComboBox.SelectedItem = item;
            break;
        }
    }

    // LocalSend 路径
    LocalSendPathTextBox.Text = _settings.LocalSendPath;

    // 过滤设置
    // ... 原有过滤设置代码 ...

    // ========== 新增：全屏模式缩略图栏设置 ==========
    ShowBottomBarInFullScreenCheckBox.IsChecked = _settings.ShowBottomBarInFullScreen;
    ShowSidebarInMangaFullScreenCheckBox.IsChecked = _settings.ShowSidebarInMangaFullScreen;
    BottomBarHeightSlider.Value = _settings.BottomBarHeight;
    BottomBarHeightText.Text = ((int)_settings.BottomBarHeight).ToString();
}


// =====================================================
// 完整的 SaveButton_Click() 方法示例（包含新增部分）
// =====================================================

private void SaveButton_Click(object sender, RoutedEventArgs e)
{
    // 保存视图模式
    if (ViewModeComboBox.SelectedItem is ComboBoxItem viewModeItem && viewModeItem.Tag is ViewMode viewMode)
    {
        _settings.DefaultViewMode = viewMode;
    }

    // 保存主题
    if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem && themeItem.Tag is AppTheme theme)
    {
        _settings.Theme = theme;
    }

    // 保存滚轮行为
    if (ScrollWheelComboBox.SelectedItem is ComboBoxItem scrollItem && scrollItem.Tag is ScrollWheelBehavior scrollBehavior)
    {
        _settings.ScrollWheelBehavior = scrollBehavior;
    }

    // 幻灯片设置
    _settings.SlideshowInterval = (int)IntervalSlider.Value;
    _settings.SlideshowShuffle = ShuffleSlideshowCheckBox.IsChecked ?? false;

    // 性能设置
    _settings.PreloadCount = (int)PreloadSlider.Value;
    _settings.ThumbnailSize = (int)ThumbnailSlider.Value;

    // 漫画模式设置
    _settings.MangaGap = MangaGapSlider.Value;
    _settings.MangaDecodeWidth = (int)MangaDecodeSlider.Value;

    // 行为设置
    _settings.RememberWindowPosition = RememberPositionCheckBox.IsChecked ?? true;
    _settings.RememberReadingPosition = RememberReadingCheckBox.IsChecked ?? true;
    _settings.FreezeDuringResize = FreezeDuringResizeCheckBox.IsChecked ?? true;
    _settings.ShowStatusBar = ShowStatusBarCheckBox.IsChecked ?? true;

    // 子文件夹扫描
    _settings.ScanSubfoldersEnabled = ScanSubfoldersCheckBox.IsChecked ?? false;
    if (ScanSubfoldersDepthComboBox.SelectedItem is ComboBoxItem depthItem && 
        depthItem.Tag is string depthStr && 
        int.TryParse(depthStr, out int depth))
    {
        _settings.ScanSubfoldersDepth = depth;
    }

    // LocalSend 路径
    _settings.LocalSendPath = LocalSendPathTextBox.Text;

    // 过滤设置
    // ... 原有过滤设置保存代码 ...

    // ========== 新增：全屏模式缩略图栏设置 ==========
    _settings.ShowBottomBarInFullScreen = ShowBottomBarInFullScreenCheckBox.IsChecked ?? true;
    _settings.ShowSidebarInMangaFullScreen = ShowSidebarInMangaFullScreenCheckBox.IsChecked ?? true;
    _settings.BottomBarHeight = BottomBarHeightSlider.Value;

    // 保存设置
    _settings.Save();
    
    // 关闭窗口
    Close();
}
