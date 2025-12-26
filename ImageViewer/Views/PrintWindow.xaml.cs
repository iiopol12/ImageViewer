using System;
using System.Drawing.Printing;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Views
{
    /// <summary>
    /// 打印窗口 - 提供图片打印预览和打印设置
    /// </summary>
    public partial class PrintWindow : Window
    {
        private BitmapSource _imageToPrint;
        private string _imagePath;
        private PrintQueue _selectedPrinter;

        // 纸张尺寸（毫米）
        private double _paperWidthMm = 210;
        private double _paperHeightMm = 297;

        // 边距（毫米）
        private double _marginTop = 10;
        private double _marginBottom = 10;
        private double _marginLeft = 10;
        private double _marginRight = 10;

        // 打印份数
        private int _copies = 1;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="image">要打印的图片</param>
        /// <param name="imagePath">图片路径（用于显示信息）</param>
        public PrintWindow(BitmapSource image, string imagePath = null)
        {
            InitializeComponent();

            _imageToPrint = image;
            _imagePath = imagePath;

            // 初始化打印机列表
            InitializePrinters();

            // 设置预览图片
            PreviewImage.Source = image;

            // 显示图片信息
            UpdateImageInfo();

            // 更新预览
            UpdatePreview();
        }

        #region 初始化

        /// <summary>
        /// 初始化打印机列表
        /// </summary>
        private void InitializePrinters()
        {
            try
            {
                var printServer = new LocalPrintServer();
                var printQueues = printServer.GetPrintQueues();

                foreach (var printer in printQueues)
                {
                    PrinterComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = printer.Name,
                        Tag = printer
                    });
                }

                // 选择默认打印机
                var defaultPrinter = LocalPrintServer.GetDefaultPrintQueue();
                if (defaultPrinter != null)
                {
                    for (int i = 0; i < PrinterComboBox.Items.Count; i++)
                    {
                        var item = PrinterComboBox.Items[i] as ComboBoxItem;
                        if (item?.Tag is PrintQueue pq && pq.Name == defaultPrinter.Name)
                        {
                            PrinterComboBox.SelectedIndex = i;
                            _selectedPrinter = pq;
                            break;
                        }
                    }
                }

                if (PrinterComboBox.SelectedIndex < 0 && PrinterComboBox.Items.Count > 0)
                {
                    PrinterComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"获取打印机列表失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 更新图片信息显示
        /// </summary>
        private void UpdateImageInfo()
        {
            if (_imageToPrint != null)
            {
                var info = $"尺寸: {_imageToPrint.PixelWidth} × {_imageToPrint.PixelHeight} 像素\n";
                info += $"DPI: {_imageToPrint.DpiX:F0} × {_imageToPrint.DpiY:F0}";

                if (!string.IsNullOrEmpty(_imagePath))
                {
                    var fileName = System.IO.Path.GetFileName(_imagePath);
                    info = $"文件: {fileName}\n" + info;
                }

                ImageInfoText.Text = info;
            }
        }

        #endregion

        #region 预览更新

        /// <summary>
        /// 更新打印预览
        /// </summary>
        private void UpdatePreview()
        {
            if (_imageToPrint == null) return;

            // 根据方向设置纸张尺寸
            bool isLandscape = LandscapeRadio.IsChecked == true;
            double paperWidth = isLandscape ? _paperHeightMm : _paperWidthMm;
            double paperHeight = isLandscape ? _paperWidthMm : _paperHeightMm;

            // 更新纸张预览大小（保持比例）
            PaperPreview.Width = paperWidth;
            PaperPreview.Height = paperHeight;

            // 计算可打印区域
            double printableWidth = paperWidth - _marginLeft - _marginRight;
            double printableHeight = paperHeight - _marginTop - _marginBottom;

            // 获取缩放模式
            var scaleMode = GetSelectedScaleMode();

            // 设置图片边距和对齐
            bool centerImage = CenterImageCheckBox.IsChecked == true;

            // 计算图片显示位置和大小
            var imageMargin = new Thickness(_marginLeft, _marginTop, _marginRight, _marginBottom);

            switch (scaleMode)
            {
                case "Fit":
                    PreviewImage.Stretch = Stretch.Uniform;
                    PreviewImage.Margin = imageMargin;
                    PreviewImage.HorizontalAlignment = centerImage ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    PreviewImage.VerticalAlignment = centerImage ? VerticalAlignment.Center : VerticalAlignment.Top;
                    break;

                case "Fill":
                    PreviewImage.Stretch = Stretch.UniformToFill;
                    PreviewImage.Margin = imageMargin;
                    PreviewImage.HorizontalAlignment = HorizontalAlignment.Center;
                    PreviewImage.VerticalAlignment = VerticalAlignment.Center;
                    PrintPreviewContainer.ClipToBounds = true;
                    break;

                case "Original":
                    PreviewImage.Stretch = Stretch.None;
                    PreviewImage.Margin = imageMargin;
                    PreviewImage.HorizontalAlignment = centerImage ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    PreviewImage.VerticalAlignment = centerImage ? VerticalAlignment.Center : VerticalAlignment.Top;
                    break;

                case "Stretch":
                    PreviewImage.Stretch = Stretch.Fill;
                    PreviewImage.Margin = imageMargin;
                    PreviewImage.HorizontalAlignment = HorizontalAlignment.Stretch;
                    PreviewImage.VerticalAlignment = VerticalAlignment.Stretch;
                    break;
            }
        }

        /// <summary>
        /// 获取选中的缩放模式
        /// </summary>
        private string GetSelectedScaleMode()
        {
            var selectedItem = ScaleModeComboBox.SelectedItem as ComboBoxItem;
            return selectedItem?.Tag?.ToString() ?? "Fit";
        }

        /// <summary>
        /// 获取选中的纸张大小
        /// </summary>
        private void UpdatePaperSize()
        {
            var selectedItem = PaperSizeComboBox.SelectedItem as ComboBoxItem;
            var paperTag = selectedItem?.Tag?.ToString() ?? "A4";

            switch (paperTag)
            {
                case "A4":
                    _paperWidthMm = 210;
                    _paperHeightMm = 297;
                    break;
                case "A3":
                    _paperWidthMm = 297;
                    _paperHeightMm = 420;
                    break;
                case "A5":
                    _paperWidthMm = 148;
                    _paperHeightMm = 210;
                    break;
                case "Letter":
                    _paperWidthMm = 216;
                    _paperHeightMm = 279;
                    break;
                case "4x6":
                    _paperWidthMm = 102;
                    _paperHeightMm = 152;
                    break;
                case "5x7":
                    _paperWidthMm = 127;
                    _paperHeightMm = 178;
                    break;
            }
        }

        /// <summary>
        /// 解析边距文本框的值
        /// </summary>
        private void ParseMargins()
        {
            if (double.TryParse(MarginTopTextBox.Text, out double top))
                _marginTop = Math.Max(0, top);
            if (double.TryParse(MarginBottomTextBox.Text, out double bottom))
                _marginBottom = Math.Max(0, bottom);
            if (double.TryParse(MarginLeftTextBox.Text, out double left))
                _marginLeft = Math.Max(0, left);
            if (double.TryParse(MarginRightTextBox.Text, out double right))
                _marginRight = Math.Max(0, right);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击最大化/还原
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        /// <summary>
        /// 关闭按钮点击
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 打印机选择改变
        /// </summary>
        private void PrinterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = PrinterComboBox.SelectedItem as ComboBoxItem;
            _selectedPrinter = selectedItem?.Tag as PrintQueue;

            if (_selectedPrinter != null)
            {
                StatusText.Text = $"已选择: {_selectedPrinter.Name}";
            }
        }

        /// <summary>
        /// 纸张大小改变
        /// </summary>
        private void PaperSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePaperSize();
            UpdatePreview();
        }

        /// <summary>
        /// 方向改变
        /// </summary>
        private void Orientation_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>
        /// 缩放模式改变
        /// </summary>
        private void ScaleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>
        /// 边距文本改变
        /// </summary>
        private void Margin_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 窗口未完全加载时跳过
            if (!IsLoaded) return; 
            ParseMargins();
            UpdatePreview();
        }

        /// <summary>
        /// 居中选项改变
        /// </summary>
        private void CenterImage_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>
        /// 减少份数
        /// </summary>
        private void DecreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            if (_copies > 1)
            {
                _copies--;
                CopiesTextBox.Text = _copies.ToString();
            }
        }

        /// <summary>
        /// 增加份数
        /// </summary>
        private void IncreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            if (_copies < 99)
            {
                _copies++;
                CopiesTextBox.Text = _copies.ToString();
            }
        }

        /// <summary>
        /// 取消按钮点击
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 打印按钮点击
        /// </summary>
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedPrinter == null)
                {
                    MessageBox.Show("请选择打印机", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 解析份数
                if (int.TryParse(CopiesTextBox.Text, out int copies))
                {
                    _copies = Math.Max(1, Math.Min(99, copies));
                }

                // 执行打印
                PrintImage();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打印失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 打印逻辑

        /// <summary>
        /// 执行打印
        /// </summary>
        private void PrintImage()
        {
            var printDialog = new PrintDialog();

            // 设置选中的打印机
            if (_selectedPrinter != null)
            {
                printDialog.PrintQueue = _selectedPrinter;
            }

            // 设置打印份数
            printDialog.PrintTicket.CopyCount = _copies;

            // 设置纸张方向
            bool isLandscape = LandscapeRadio.IsChecked == true;
            printDialog.PrintTicket.PageOrientation = isLandscape
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;

            // 创建可视化元素用于打印
            var visual = CreatePrintVisual(printDialog);

            // 执行打印
            string documentName = !string.IsNullOrEmpty(_imagePath)
                ? System.IO.Path.GetFileName(_imagePath)
                : "图片打印";

            printDialog.PrintVisual(visual, documentName);

            StatusText.Text = "打印任务已发送";
        }

        /// <summary>
        /// 创建打印用的可视化元素
        /// </summary>
        private DrawingVisual CreatePrintVisual(PrintDialog printDialog)
        {
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                // 获取可打印区域
                var printableArea = printDialog.PrintableAreaWidth;
                var printableHeight = printDialog.PrintableAreaHeight;

                // 转换边距从毫米到设备单位（96 DPI）
                double mmToPixel = 96.0 / 25.4;
                double marginTop = _marginTop * mmToPixel;
                double marginBottom = _marginBottom * mmToPixel;
                double marginLeft = _marginLeft * mmToPixel;
                double marginRight = _marginRight * mmToPixel;

                // 计算实际可用区域
                double availableWidth = printableArea - marginLeft - marginRight;
                double availableHeight = printableHeight - marginTop - marginBottom;

                // 获取图片尺寸
                double imageWidth = _imageToPrint.PixelWidth;
                double imageHeight = _imageToPrint.PixelHeight;

                // 根据缩放模式计算最终尺寸和位置
                var scaleMode = GetSelectedScaleMode();
                double finalWidth, finalHeight, finalX, finalY;

                switch (scaleMode)
                {
                    case "Fit":
                        // 适应纸张（保持比例）
                        var fitScale = Math.Min(availableWidth / imageWidth, availableHeight / imageHeight);
                        finalWidth = imageWidth * fitScale;
                        finalHeight = imageHeight * fitScale;
                        break;

                    case "Fill":
                        // 填充纸张（可能裁剪）
                        var fillScale = Math.Max(availableWidth / imageWidth, availableHeight / imageHeight);
                        finalWidth = imageWidth * fillScale;
                        finalHeight = imageHeight * fillScale;
                        break;

                    case "Original":
                        // 原始大小
                        finalWidth = imageWidth;
                        finalHeight = imageHeight;
                        break;

                    case "Stretch":
                        // 拉伸填满
                        finalWidth = availableWidth;
                        finalHeight = availableHeight;
                        break;

                    default:
                        finalWidth = availableWidth;
                        finalHeight = availableHeight;
                        break;
                }

                // 计算位置（居中或左上）
                bool centerImage = CenterImageCheckBox.IsChecked == true;
                if (centerImage)
                {
                    finalX = marginLeft + (availableWidth - finalWidth) / 2;
                    finalY = marginTop + (availableHeight - finalHeight) / 2;
                }
                else
                {
                    finalX = marginLeft;
                    finalY = marginTop;
                }

                // 绘制图片
                var rect = new Rect(finalX, finalY, finalWidth, finalHeight);
                dc.DrawImage(_imageToPrint, rect);
            }

            return visual;
        }

        #endregion
    }
}