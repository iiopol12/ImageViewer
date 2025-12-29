using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Helpers
{
    /// <summary>
    /// Shell 图标加载帮助类 - 增强版
    /// 支持检测文件夹是否包含图片并显示相应图标
    /// </summary>
    public static class ShellIconHelper
    {
        #region Win32 API

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbSizeFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        #endregion

        #region 支持的图片格式

        private static readonly string[] ImageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
            ".tiff", ".tif", ".ico", ".svg", ".heic", ".heif"
        };

        #endregion

        // 图标缓存 - 按路径缓存
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

        // 通用文件夹图标（预先加载）
        private static ImageSource? _genericFolderIcon;
        private static ImageSource? _imageFolderIcon; // 包含图片的文件夹图标
        private static readonly object _genericIconLock = new();

        /// <summary>
        /// 获取通用文件夹图标（同步，用于默认显示）
        /// </summary>
        public static ImageSource GetGenericFolderIcon()
        {
            if (_genericFolderIcon != null)
                return _genericFolderIcon;

            lock (_genericIconLock)
            {
                if (_genericFolderIcon != null)
                    return _genericFolderIcon;

                try
                {
                    // 使用一个通用路径获取文件夹图标
                    var shfi = new SHFILEINFO();
                    var result = SHGetFileInfo(
                        "folder",
                        FILE_ATTRIBUTE_DIRECTORY,
                        ref shfi,
                        (uint)Marshal.SizeOf(shfi),
                        SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

                    if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        var icon = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        icon.Freeze();
                        _genericFolderIcon = icon;
                        DestroyIcon(shfi.hIcon);
                    }
                }
                catch
                {
                    // 如果获取失败，创建一个简单的占位图标
                    _genericFolderIcon = CreatePlaceholderIcon(false);
                }

                return _genericFolderIcon ?? CreatePlaceholderIcon(false);
            }
        }

        /// <summary>
        /// 获取图片文件夹图标（包含图片的文件夹）
        /// </summary>
        public static ImageSource GetImageFolderIcon()
        {
            if (_imageFolderIcon != null)
                return _imageFolderIcon;

            lock (_genericIconLock)
            {
                if (_imageFolderIcon != null)
                    return _imageFolderIcon;

                // 创建一个带图片标识的文件夹图标
                _imageFolderIcon = CreatePlaceholderIcon(true);
                return _imageFolderIcon;
            }
        }

        /// <summary>
        /// 异步获取文件夹图标（带缓存和图片检测）
        /// </summary>
        public static async Task<ImageSource> GetFolderIconAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return GetGenericFolderIcon();

            // 先检查缓存
            if (_iconCache.TryGetValue(folderPath, out var cachedIcon))
                return cachedIcon;

            // 后台线程加载
            return await Task.Run(() =>
            {
                try
                {
                    // 检查文件夹是否包含图片
                    bool hasImages = FolderContainsImages(folderPath);

                    // 获取系统图标
                    var shfi = new SHFILEINFO();
                    var result = SHGetFileInfo(
                        folderPath,
                        FILE_ATTRIBUTE_DIRECTORY,
                        ref shfi,
                        (uint)Marshal.SizeOf(shfi),
                        SHGFI_ICON | SHGFI_LARGEICON);

                    ImageSource icon;
                    if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        // 必须在创建 BitmapSource 后立即 Freeze 以便跨线程使用
                        icon = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        icon.Freeze();
                        DestroyIcon(shfi.hIcon);
                    }
                    else
                    {
                        // 使用占位图标
                        icon = hasImages ? GetImageFolderIcon() : GetGenericFolderIcon();
                    }

                    // 如果包含图片，添加一个视觉标识（可选）
                    if (hasImages)
                    {
                        icon = AddImageIndicator(icon);
                    }

                    // 缓存
                    _iconCache.TryAdd(folderPath, icon);
                    return icon;
                }
                catch
                {
                    // 忽略错误，返回通用图标
                }

                return GetGenericFolderIcon();
            });
        }

        /// <summary>
        /// 检查文件夹是否包含图片文件
        /// </summary>
        private static bool FolderContainsImages(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return false;

                // 快速检查前几个文件
                var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Take(20); // 只检查前 20 个文件以提高性能

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ImageExtensions.Contains(ext))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 访问被拒绝或其他错误
            }

            return false;
        }

        /// <summary>
        /// 为图标添加图片指示器（右下角小图标）
        /// </summary>
        private static ImageSource AddImageIndicator(ImageSource baseIcon)
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // 绘制原始图标
                    dc.DrawImage(baseIcon, new Rect(0, 0, 32, 32));

                    // 在右下角绘制一个小的图片标识
                    var indicatorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // 绿色
                    var indicatorGeometry = new EllipseGeometry(new System.Windows.Point(26, 26), 6, 6);
                    dc.DrawGeometry(indicatorBrush, null, indicatorGeometry);

                    // 绘制一个简单的图片图标
                    var imageBrush = new SolidColorBrush(Colors.White);
                    var imageGeometry = new RectangleGeometry(new Rect(23, 23, 6, 6), 1, 1);
                    dc.DrawGeometry(imageBrush, null, imageGeometry);
                }

                var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                return rtb;
            }
            catch
            {
                // 如果添加指示器失败，返回原始图标
                return baseIcon;
            }
        }

        /// <summary>
        /// 获取"返回上级"图标
        /// </summary>
        public static ImageSource GetParentFolderIcon()
        {
            // 返回一个带向上箭头的文件夹图标
            return CreateParentFolderIcon();
        }

        /// <summary>
        /// 创建返回上级文件夹图标
        /// </summary>
        private static ImageSource CreateParentFolderIcon()
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                    null,
                    new Rect(0, 0, 32, 32));

                // 文件夹底色
                var folderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 200, 100));
                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = new System.Windows.Point(4, 10) };
                figure.Segments.Add(new LineSegment(new System.Windows.Point(4, 26), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(28, 26), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(28, 10), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(16, 10), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(14, 6), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(4, 6), true));
                figure.IsClosed = true;
                geometry.Figures.Add(figure);
                dc.DrawGeometry(folderBrush, null, geometry);

                // 向上箭头
                var arrowPen = new System.Windows.Media.Pen(
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                    2);
                arrowPen.StartLineCap = PenLineCap.Round;
                arrowPen.EndLineCap = PenLineCap.Round;

                // 箭头主干
                dc.DrawLine(arrowPen, new System.Windows.Point(16, 20), new System.Windows.Point(16, 12));
                // 箭头左侧
                dc.DrawLine(arrowPen, new System.Windows.Point(16, 12), new System.Windows.Point(13, 15));
                // 箭头右侧
                dc.DrawLine(arrowPen, new System.Windows.Point(16, 12), new System.Windows.Point(19, 15));
            }

            var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// 创建占位图标
        /// </summary>
        private static ImageSource CreatePlaceholderIcon(bool hasImages)
        {
            // 创建一个简单的 32x32 淡灰色方块作为占位
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // 画一个简单的文件夹形状
                var folderBrush = new SolidColorBrush(
                    hasImages
                    ? System.Windows.Media.Color.FromRgb(100, 200, 255) // 蓝色表示包含图片
                    : System.Windows.Media.Color.FromRgb(255, 200, 100) // 黄色表示普通文件夹
                );

                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = new System.Windows.Point(4, 10) };
                figure.Segments.Add(new LineSegment(new System.Windows.Point(4, 26), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(28, 26), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(28, 10), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(16, 10), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(14, 6), true));
                figure.Segments.Add(new LineSegment(new System.Windows.Point(4, 6), true));
                figure.IsClosed = true;
                geometry.Figures.Add(figure);
                dc.DrawGeometry(folderBrush, null, geometry);

                // 如果包含图片，添加一个小图标标识
                if (hasImages)
                {
                    var imageBrush = new SolidColorBrush(Colors.White);
                    var imageRect = new Rect(14, 14, 8, 6);
                    dc.DrawRectangle(imageBrush, null, imageRect);

                    // 画一个简单的山和太阳图案
                    var detailPen = new System.Windows.Media.Pen(
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 150, 200)),
                        1);
                    dc.DrawLine(detailPen, new System.Windows.Point(14, 20), new System.Windows.Point(18, 16));
                    dc.DrawLine(detailPen, new System.Windows.Point(18, 16), new System.Windows.Point(22, 20));

                    // 太阳
                    dc.DrawEllipse(
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 200, 0)),
                        null,
                        new System.Windows.Point(19, 16),
                        1.5, 1.5);
                }
            }

            var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// 清除图标缓存
        /// </summary>
        public static void ClearCache()
        {
            _iconCache.Clear();
        }

        /// <summary>
        /// 预热缓存 - 批量加载图标
        /// </summary>
        public static async Task PreloadIconsAsync(string[] folderPaths)
        {
            var tasks = new Task[folderPaths.Length];
            for (int i = 0; i < folderPaths.Length; i++)
            {
                tasks[i] = GetFolderIconAsync(folderPaths[i]);
            }
            await Task.WhenAll(tasks);
        }
    }
}