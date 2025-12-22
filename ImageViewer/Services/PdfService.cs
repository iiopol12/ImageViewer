using ImageViewer.Models;
using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    /// <summary>
    /// </summary>
    public class PdfService : IDisposable
    {
        private readonly Dictionary<string, PdfDocument> _documentCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheLock = new();

        private const float DefaultDpi = 150f;
        private const float ThumbnailDpi = 72f;

        /// <summary>
        /// </summary>
        public static bool IsPdf(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// </summary>
        public IReadOnlyList<ImageInfo> ScanPdf(string pdfPath)
        {
            if (!File.Exists(pdfPath))
                return Array.Empty<ImageInfo>();

            var doc = GetOrOpenDocument(pdfPath);
            var pageCount = doc.PageCount;
            if (pageCount <= 0)
                return Array.Empty<ImageInfo>();

            var fileInfo = new FileInfo(pdfPath);
            var estimatedPageSize = fileInfo.Length / pageCount; // 估算每页大小

            var result = new List<ImageInfo>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                var pageSize = doc.PageSizes[i];

                result.Add(new ImageInfo
                {
                    FilePath = pdfPath,
                    SourceKind = ImageSourceKind.PdfPage,
                    PdfPageIndex = i,
                    FileSize = estimatedPageSize,
                    DateModified = fileInfo.LastWriteTime,
                    Width = (int)pageSize.Width,
                    Height = (int)pageSize.Height,
                    RelativePath = $"Page {i + 1}"
                });
            }

            return result;
        }

        /// <summary>
        /// </summary>
        public int GetPageCount(string pdfPath)
        {
            try
            {
                var doc = GetOrOpenDocument(pdfPath);
                return doc.PageCount;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="pageIndex">页码（从 0 开始）</param>
        public BitmapSource? RenderPage(string pdfPath, int pageIndex, int? targetWidth = null)
        {
            try
            {
                var doc = GetOrOpenDocument(pdfPath);

                if (pageIndex < 0 || pageIndex >= doc.PageCount)
                    return null;

                var pageSize = doc.PageSizes[pageIndex];

                // 计算渲染尺寸
                float dpi;
                if (targetWidth.HasValue && targetWidth.Value > 0)
                {
                    dpi = targetWidth.Value / (float)pageSize.Width * 72f;
                    dpi = Math.Clamp(dpi, 36f, 300f);
                }
                else
                {
                    dpi = DefaultDpi;
                }

                int renderWidth = (int)(pageSize.Width * dpi / 72f);
                int renderHeight = (int)(pageSize.Height * dpi / 72f);

                using var image = doc.Render(pageIndex, renderWidth, renderHeight, dpi, dpi, false);

                return ConvertToBitmapSource(image);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"渲染 PDF 页面失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public BitmapSource? RenderThumbnail(string pdfPath, int pageIndex, int thumbnailSize = 120)
        {
            try
            {
                var doc = GetOrOpenDocument(pdfPath);

                if (pageIndex < 0 || pageIndex >= doc.PageCount)
                    return null;

                var pageSize = doc.PageSizes[pageIndex];

                // 计算缩略图尺寸（保持宽高比）
                float scale = Math.Min(
                    thumbnailSize / (float)pageSize.Width,
                    thumbnailSize / (float)pageSize.Height);

                int renderWidth = Math.Max(1, (int)(pageSize.Width * scale));
                int renderHeight = Math.Max(1, (int)(pageSize.Height * scale));

                using var image = doc.Render(pageIndex, renderWidth, renderHeight, ThumbnailDpi, ThumbnailDpi, false);
                return ConvertToBitmapSource(image);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"渲染 PDF 缩略图失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// </summary>
        public BitmapSource? RenderPage(ImageInfo imageInfo, int? targetWidth = null)
        {
            if (imageInfo.SourceKind != ImageSourceKind.PdfPage)
                return null;

            return RenderPage(imageInfo.FilePath, imageInfo.PdfPageIndex, targetWidth);
        }

        /// <summary>
        /// </summary>
        public BitmapSource? RenderThumbnail(ImageInfo imageInfo, int thumbnailSize = 120)
        {
            if (imageInfo.SourceKind != ImageSourceKind.PdfPage)
                return null;

            return RenderThumbnail(imageInfo.FilePath, imageInfo.PdfPageIndex, thumbnailSize);
        }

        /// <summary>
        /// </summary>
        private PdfDocument GetOrOpenDocument(string pdfPath)
        {
            lock (_cacheLock)
            {
                if (_documentCache.TryGetValue(pdfPath, out var cached))
                {
                    return cached;
                }

                try
                {
                    var doc = PdfDocument.Load(pdfPath);
                    _documentCache[pdfPath] = doc;
                    return doc;
                }
                catch (DllNotFoundException ex)
                {
                    throw new InvalidOperationException(
                        "无法加载 PDF 渲染引擎（pdfium.dll）。请确认已将 pdfium.dll 放到 exe 同目录。",
                        ex);
                }
                catch (BadImageFormatException ex)
                {
                    throw new InvalidOperationException(
                        "PDF 渲染引擎架构不匹配（可能混用了 x86/x64 的 pdfium.dll）。",
                        ex);
                }
                catch (PdfException ex)
                {
                    var hint = ex.Error switch
                    {
                        PdfError.PasswordProtected => "PDF 已加密，需要密码。",
                        PdfError.UnsupportedSecurityScheme => "PDF 使用了不支持的加密方案。",
                        PdfError.InvalidFormat => "PDF 格式无效或已损坏。",
                        PdfError.CannotOpenFile => "无法打开 PDF 文件（可能被占用或权限不足）。",
                        _ => $"PDFium 错误: {ex.Error}"
                    };

                    throw new InvalidOperationException($"打开 PDF 失败：{hint}", ex);
                }
            }
        }

        /// <summary>
        /// </summary>
        public void CloseDocument(string pdfPath)
        {
            lock (_cacheLock)
            {
                if (_documentCache.TryGetValue(pdfPath, out var doc))
                {
                    doc.Dispose();
                    _documentCache.Remove(pdfPath);
                }
            }
        }

        /// <summary>
        /// </summary>
        private static BitmapSource ConvertToBitmapSource(System.Drawing.Image image)
        {
            using var bitmap = new Bitmap(image);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                var bitmapSource = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96, 96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    bitmapData.Scan0,
                    bitmapData.Stride * bitmap.Height,
                    bitmapData.Stride);

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                foreach (var doc in _documentCache.Values)
                {
                    try { doc.Dispose(); } catch { }
                }
                _documentCache.Clear();
            }
        }
    }
}

