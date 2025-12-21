using ImageViewer.Models;
using ImageViewer.Views;
using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    public class ImageService : IDisposable
    {

        // 压缩包缓存限制
        private const int DefaultMaxExtractCacheFiles = 200;
        private const long DefaultMaxExtractCacheBytes = 512L * 1024 * 1024;

        private static readonly string[] BitmapImageExtensions =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".bmp",
            ".webp",
            ".tiff",
            ".tif",
            ".ico",
            ".dng",
            ".cr2",
            ".cr3",
            ".nef",
            ".arw",
            ".raf",
            ".rw2",
            ".orf",
            ".pef",
            ".srw",
        };

        private static readonly string[] MagickNetExtensions =
        {
            ".avif", ".heic", ".heif", ".jxl", ".psd", ".tga", ".exr", ".dds", ".wp2"
        };

        private static readonly string[] ArchiveExtensions =
        {
            ".zip",
            ".cbz",
            ".rar",
            ".cbr",
        };


        private static readonly string[] PdfExtensions = { ".pdf" };
        // HashSet用于O(1)查找效率

        private static readonly HashSet<string> PdfExtensionSet = new(PdfExtensions, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BitmapImageExtensionSet = new(BitmapImageExtensions, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportedExtensionSet = new(BitmapImageExtensions.Concat(MagickNetExtensions), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ArchiveExtensionSet = new(ArchiveExtensions, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> SupportedExtensions { get; } = SupportedExtensionSet.OrderBy(e => e).ToArray();
        public static IReadOnlyList<string> SupportedArchiveExtensions { get; } = ArchiveExtensionSet.OrderBy(e => e).ToArray();


        private readonly PdfService _pdfService = new();

        public static string OpenFileDialogFilter
        {
            get
            {
                var imagePatterns = string.Join(';', SupportedExtensions.Select(ext => $"*{ext}"));
                var archivePatterns = string.Join(';', SupportedArchiveExtensions.Select(ext => $"*{ext}"));
                var pdfPatterns = "*.pdf";
                var combinedPatterns = string.Join(';', new[] { imagePatterns, archivePatterns, pdfPatterns }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));

                return $"所有支持的文件|{combinedPatterns}|图片文件|{imagePatterns}|压缩包|{archivePatterns}|PDF 文档|{pdfPatterns}|所有文件|*.*";
            }
        }


        public static bool IsSupportedPdf(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && PdfExtensionSet.Contains(ext);
        }
        public IEnumerable<ImageInfo> ScanPdf(string pdfPath)
        {
            return _pdfService.ScanPdf(pdfPath);
        }


        public ArchiveLoadStrategy ArchiveLoadStrategy { get; set; } = ArchiveLoadStrategy.Stream;

        private enum ArchiveBackend
        {
            ZipArchive = 0,
            SharpCompress = 1
        }

        private readonly ConcurrentDictionary<string, ArchiveBackend> _archiveBackendCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _archivePasswordCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> _archivePasswordLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _archivePasswordCancelled = new(StringComparer.OrdinalIgnoreCase);

        public event Action<string>? ArchivePasswordCanceled;


        // 临时解压目录根路径（每个会话独立GUID）
        private readonly string _archiveExtractSessionRoot =
            Path.Combine(Path.GetTempPath(), "ImageViewer", "ArchiveExtract", Guid.NewGuid().ToString("N"));

        // LRU数据结构：线程安全锁
        private readonly object _archiveExtractCacheLock = new();
        // LRU链表：头部=最近使用，尾部=最久未使用
        private readonly LinkedList<ExtractCacheItem> _archiveExtractLru = new();
        // 快速索引：O(1)查找节点位置
        private readonly Dictionary<string, LinkedListNode<ExtractCacheItem>> _archiveExtractIndex = new(StringComparer.Ordinal);
        // 当前缓存总大小（字节）
        private long _archiveExtractTotalBytes;

        // 每个压缩包的提取锁（避免重复解压）
        private readonly ConcurrentDictionary<string, object> _archiveExtractLocks = new(StringComparer.Ordinal);

        // 实际数据存储（线程安全字典）
        private readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new();
        private readonly ConcurrentDictionary<string, BitmapSource> _imageCache = new();

        // 加载任务管理（用于取消进行中的加载）
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _loadingTasks = new();

        // 并发加载限制
        private readonly SemaphoreSlim _loadSemaphore = new(4);

        // 缓存容量上限
        private readonly int _maxCacheSize = 20;      // 大图缓存
        private readonly int _maxThumbnailCacheSize = 100; // 缩略图缓存

        // LRU #1: 图片缓存
        private readonly LinkedList<string> _imageLru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _imageLruIndex = new(StringComparer.Ordinal);

 
        // LRU #2: 缩略图缓存
        private readonly LinkedList<string> _thumbLru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _thumbLruIndex =new(StringComparer.Ordinal);

        // 统一锁
        private readonly object _cacheLock = new();
        private readonly object _imageCacheLock = new();
        private readonly object _thumbCacheLock = new();

     

        /// <summary>
        /// 判断文件是否为支持的图片格式
        /// </summary>
        public static bool IsSupportedImage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && SupportedExtensionSet.Contains(ext);
        }


        /// <summary>
        /// 判断文件是否为支持的压缩包格式
        /// </summary>
        public static bool IsSupportedArchive(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && ArchiveExtensionSet.Contains(ext);
        }

        private static string GetArchiveExtension(string archivePath)
        {
            return Path.GetExtension(archivePath).ToLowerInvariant();
        }

        private static bool IsZipLikeArchive(string extension)
        {
            return extension is ".zip" or ".cbz";
        }

        private static bool IsRarLikeArchive(string extension)
        {
            return extension is ".rar" or ".cbr";
        }

        private ArchiveBackend GetArchiveBackend(string archivePath)
        {
            var ext = GetArchiveExtension(archivePath);
            if (IsRarLikeArchive(ext))
            {
                return ArchiveBackend.SharpCompress;
            }

            if (_archiveBackendCache.TryGetValue(archivePath, out var backend))
            {
                return backend;
            }

            return ArchiveBackend.ZipArchive;
        }

        private void ForceSharpCompressBackend(string archivePath)
        {
            _archiveBackendCache[archivePath] = ArchiveBackend.SharpCompress;
        }

        public void ResetArchivePasswordCancellation(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return;
            }

            _archivePasswordCancelled.TryRemove(archivePath, out _);
        }

        public bool IsArchivePasswordCancelled(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return false;
            }

            return _archivePasswordCancelled.ContainsKey(archivePath);
        }

 

        private static bool IsSupportedImageExtension(string extension)
        {
            return !string.IsNullOrWhiteSpace(extension) && SupportedExtensionSet.Contains(extension);
        }

        /// <summary>
        /// 判断是否优先使用BitmapImage加载（速度更快）
        /// </summary>
        private static bool PreferBitmapImage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && BitmapImageExtensionSet.Contains(ext);
        }

        private static bool PreferBitmapImageExtension(string extension)
        {
            return !string.IsNullOrWhiteSpace(extension) && BitmapImageExtensionSet.Contains(extension);
        }



        /// <summary>
        /// 使用BitmapImage加载图片（适用于常见格式，性能更好）
        /// </summary>
        private static BitmapSource? LoadWithBitmapImage(string filePath, int? decodePixelWidth = null)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载到内存
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;// 忽略颜色配置文件

            if (decodePixelWidth.HasValue)
            {
                bitmap.DecodePixelWidth = decodePixelWidth.Value;// 缩放到指定宽度
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }


        /// <summary>
        /// 使用MagickNet加载图片
        /// </summary>
        private static BitmapSource? LoadWithMagickNet(string filePath, int? decodePixelWidth = null)
        {
            using var image = new MagickImage(filePath);
            image.AutoOrient();// 自动旋转EXIF方向

            if (decodePixelWidth.HasValue && decodePixelWidth.Value > 0)
            {
                image.Resize((uint)decodePixelWidth.Value, 0);// 等比缩放
            }

            var bitmapSource = image.ToBitmapSource();
            bitmapSource.Freeze();
            return bitmapSource;
        }


        /// <summary>
        /// 从字节数组加载（用于压缩包内的图片）
        /// </summary>
        private static BitmapSource? LoadWithBitmapImage(byte[] data, int? decodePixelWidth = null)
        {
            using var stream = new MemoryStream(data, writable: false);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            if (decodePixelWidth.HasValue)
            {
                bitmap.DecodePixelWidth = decodePixelWidth.Value;
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource? LoadWithMagickNet(byte[] data, int? decodePixelWidth = null)
        {
            using var image = new MagickImage(data);
            image.AutoOrient();

            if (decodePixelWidth.HasValue && decodePixelWidth.Value > 0)
            {
                image.Resize((uint)decodePixelWidth.Value, 0);
            }

            var bitmapSource = image.ToBitmapSource();
            bitmapSource.Freeze();
            return bitmapSource;
        }

        private static bool PassesFileSizeFilter(long sizeBytes, ImageFilterOptions options)
        {
            if (!options.ShouldCheckFileSize)
            {
                return true;
            }

            if (options.MinFileSizeBytes > 0 && sizeBytes < options.MinFileSizeBytes)
            {
                return false;
            }

            if (options.MaxFileSizeBytes < long.MaxValue && sizeBytes > options.MaxFileSizeBytes)
            {
                return false;
            }

            return true;
        }

        private static bool PassesDimensionFilter(int width, int height, ImageFilterOptions options)
        {
            if (!options.ShouldCheckDimensions)
            {
                return true;
            }

            if (options.MinWidth > 0 && width < options.MinWidth)
            {
                return false;
            }

            if (options.MinHeight > 0 && height < options.MinHeight)
            {
                return false;
            }

            if (options.MaxWidth > 0 && width > options.MaxWidth)
            {
                return false;
            }

            if (options.MaxHeight > 0 && height > options.MaxHeight)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetImageDimensions(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (PreferBitmapImage(filePath))
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        width = frame.PixelWidth;
                        height = frame.PixelHeight;
                        if (width > 0 && height > 0)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // fall back to Magick.NET
                }
            }

            try
            {
                var info = new MagickImageInfo(filePath);
                width = (int)info.Width;
                height = (int)info.Height;
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }

        private static ImageInfo CreateImageInfoFromFileInfo(string filePath, FileInfo fileInfo)
        {
            return new ImageInfo
            {
                FilePath = filePath,
                SourceKind = ImageSourceKind.File,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue
            };
        }

        public bool PassesFolderFilters(string filePath, ImageFilterOptions options, out FileInfo? fileInfo)
        {
            fileInfo = null;

            if (!IsSupportedImage(filePath))
            {
                return false;
            }

            if (!options.ShouldCheckFileSize && !options.ShouldCheckDimensions)
            {
                return true;
            }

            if (options.ShouldCheckFileSize)
            {
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch
                {
                    return false;
                }

                if (!fileInfo.Exists || !PassesFileSizeFilter(fileInfo.Length, options))
                {
                    return false;
                }
            }

            if (options.ShouldCheckDimensions)
            {
                if (!TryGetImageDimensions(filePath, out var width, out var height))
                {
                    return false;
                }

                if (!PassesDimensionFilter(width, height, options))
                {
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// 从流读取所有字节
        /// </summary>
        private static byte[] ReadAllBytes(Stream stream, long? expectedSize = null)
        {
            if (expectedSize is > 0 and <= int.MaxValue)
            {
                // 预分配大小以提高性能
                using var ms = new MemoryStream((int)expectedSize.Value);
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            using var result = new MemoryStream();
            stream.CopyTo(result);
            return result.ToArray();
        }



        /// <summary>
        /// 从ZIP压缩包读取指定条目的字节数据
        /// </summary>
        private static byte[] ReadZipEntryBytes(string archivePath, string entryPath, long? expectedSize = null)
        {
            using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new FileNotFoundException($"压缩包中找不到条目: {entryPath}", archivePath);
            }

            using var entryStream = entry.Open();
            return ReadAllBytes(entryStream, expectedSize ?? entry.Length);
        }


        /// <summary>
        /// 从ZIP压缩包解压指定条目到文件
        /// </summary>
        private static void ExtractZipEntryToFile(string archivePath, string entryPath, string destinationPath)
        {
            using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new FileNotFoundException($"压缩包中找不到条目: {entryPath}", archivePath);
            }

            using var entryStream = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            entryStream.CopyTo(output);
        }

        private static bool ShouldFallbackToSharpCompress(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is InvalidDataException or NotSupportedException)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool LooksLikePasswordRelatedFailure(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                var message = current.Message;
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var lower = message.ToLowerInvariant();
                if (lower.Contains("password") ||
                    lower.Contains("encrypted") ||
                    lower.Contains("wrong password") ||
                    lower.Contains("crypt") ||
                    lower.Contains("aes"))
                {
                    return true;
                }
            }
            return false;
        }

        private string? PromptForArchivePassword(string archivePath, string? errorMessage)
        {
            var app = Application.Current;
            if (app == null)
            {
                return null;
            }

            string? ShowDialog()
            {
                var dialog = new ArchivePasswordDialog(archivePath, errorMessage)
                {
                    Owner = app.MainWindow,
                    WindowStartupLocation = app.MainWindow != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
                };

                return dialog.ShowDialog() == true ? dialog.Password : null;
            }

            if (app.Dispatcher.CheckAccess())
            {
                return ShowDialog();
            }

            return app.Dispatcher.Invoke(ShowDialog);
        }

        private string? GetOrPromptArchivePassword(string archivePath, string? errorMessage, string? currentPassword, bool forceNewPassword)
        {
            if (_archivePasswordCancelled.ContainsKey(archivePath))
            {
                return null;
            }

            bool raiseCanceledEvent = false;
            var lockObj = _archivePasswordLocks.GetOrAdd(archivePath, _ => new object());
            string? resultPassword = null;
            lock (lockObj)
            {
                if (_archivePasswordCancelled.ContainsKey(archivePath))
                {
                    return null;
                }

                if (_archivePasswordCache.TryGetValue(archivePath, out var cached))
                {
                    if (!forceNewPassword)
                    {
                        return cached;
                    }

                    if (!string.Equals(cached, currentPassword, StringComparison.Ordinal))
                    {
                        return cached;
                    }
                }

                var password = PromptForArchivePassword(archivePath, errorMessage);
                if (password == null)
                {
                    if (_archivePasswordCancelled.TryAdd(archivePath, true))
                    {
                        raiseCanceledEvent = true;
                    }

                    _archivePasswordCache.TryRemove(archivePath, out _);
                    resultPassword = null;
                }
                else
                {
                    _archivePasswordCancelled.TryRemove(archivePath, out _);
                    _archivePasswordCache[archivePath] = password;
                    resultPassword = password;
                }

                // resultPassword set above
            }

            if (raiseCanceledEvent)
            {
                try
                {
                    ArchivePasswordCanceled?.Invoke(archivePath);
                }
                catch
                {
                    // ignore handler failures
                }
            }

            return resultPassword;
        }

        private static string NormalizeArchiveEntryPath(string entryPath)
        {
            return (entryPath ?? string.Empty).Replace('\\', '/');
        }

        private byte[] ReadArchiveEntryBytes(string archivePath, string entryPath, long? expectedSize = null)
        {
            var ext = GetArchiveExtension(archivePath);
            var backend = GetArchiveBackend(archivePath);

            if (IsZipLikeArchive(ext) && backend == ArchiveBackend.ZipArchive)
            {
                try
                {
                    return ReadZipEntryBytes(archivePath, entryPath, expectedSize);
                }
                catch (Exception ex) when (ShouldFallbackToSharpCompress(ex))
                {
                    ForceSharpCompressBackend(archivePath);
                }
            }

            ForceSharpCompressBackend(archivePath);
            return ReadArchiveEntryBytesWithSharpCompress(archivePath, entryPath, expectedSize);
        }

        private void ExtractArchiveEntryToFile(string archivePath, string entryPath, string destinationPath)
        {
            var ext = GetArchiveExtension(archivePath);
            var backend = GetArchiveBackend(archivePath);

            if (IsZipLikeArchive(ext) && backend == ArchiveBackend.ZipArchive)
            {
                try
                {
                    ExtractZipEntryToFile(archivePath, entryPath, destinationPath);
                    return;
                }
                catch (Exception ex) when (ShouldFallbackToSharpCompress(ex))
                {
                    ForceSharpCompressBackend(archivePath);
                }
            }

            ForceSharpCompressBackend(archivePath);
            ExtractArchiveEntryToFileWithSharpCompress(archivePath, entryPath, destinationPath);
        }

        private static IArchive OpenSharpCompressArchive(string archivePath, string? password)
        {
            var options = new ReaderOptions
            {
                Password = password
            };

            return ArchiveFactory.Open(archivePath, options);
        }

        private static IArchiveEntry FindSharpCompressEntry(IArchive archive, string entryPath)
        {
            var normalizedTarget = NormalizeArchiveEntryPath(entryPath);

            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory &&
                !string.IsNullOrWhiteSpace(e.Key) &&
                NormalizeArchiveEntryPath(e.Key) == normalizedTarget);

            entry ??= archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory &&
                !string.IsNullOrWhiteSpace(e.Key) &&
                string.Equals(NormalizeArchiveEntryPath(e.Key), normalizedTarget, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                throw new FileNotFoundException($"压缩包中找不到条目: {entryPath}");
            }

            return entry;
        }

        private byte[] ReadArchiveEntryBytesWithSharpCompress(string archivePath, string entryPath, long? expectedSize)
        {
            if (_archivePasswordCancelled.ContainsKey(archivePath))
            {
                throw new OperationCanceledException("已取消输入密码。");
            }

            var ext = GetArchiveExtension(archivePath);
            var isRar = IsRarLikeArchive(ext);

            _archivePasswordCache.TryGetValue(archivePath, out var password);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var archive = OpenSharpCompressArchive(archivePath, password);
                    var entry = FindSharpCompressEntry(archive, entryPath);
                    using var entryStream = entry.OpenEntryStream();
                    return ReadAllBytes(entryStream, expectedSize);
                }
                catch (Exception ex) when ((password == null && isRar) || LooksLikePasswordRelatedFailure(ex))
                {
                    var message = password == null ? "请输入压缩包密码：" : "密码错误或无法解密，请重试：";
                    var newPassword = GetOrPromptArchivePassword(archivePath, message, password, forceNewPassword: password != null);
                    if (newPassword == null)
                    {
                        throw new OperationCanceledException("已取消输入密码。", ex);
                    }

                    password = newPassword;
                }
            }

            throw new InvalidOperationException("无法解密压缩包内容：已多次尝试密码仍失败。");
        }

        private void ExtractArchiveEntryToFileWithSharpCompress(string archivePath, string entryPath, string destinationPath)
        {
            if (_archivePasswordCancelled.ContainsKey(archivePath))
            {
                throw new OperationCanceledException("已取消输入密码。");
            }

            var ext = GetArchiveExtension(archivePath);
            var isRar = IsRarLikeArchive(ext);

            _archivePasswordCache.TryGetValue(archivePath, out var password);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var archive = OpenSharpCompressArchive(archivePath, password);
                    var entry = FindSharpCompressEntry(archive, entryPath);
                    using var entryStream = entry.OpenEntryStream();
                    using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    entryStream.CopyTo(output);
                    return;
                }
                catch (Exception ex) when ((password == null && isRar) || LooksLikePasswordRelatedFailure(ex))
                {
                    var message = password == null ? "请输入压缩包密码：" : "密码错误或无法解密，请重试：";
                    var newPassword = GetOrPromptArchivePassword(archivePath, message, password, forceNewPassword: password != null);
                    if (newPassword == null)
                    {
                        throw new OperationCanceledException("已取消输入密码。", ex);
                    }

                    password = newPassword;
                }
            }

            throw new InvalidOperationException("无法解密压缩包内容：已多次尝试密码仍失败。");
        }

        private static string StableHash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes.AsSpan(0, 16));
        }

        private static string GetArchiveId(string archivePath)
        {
            var info = new FileInfo(archivePath);
            var len = info.Exists ? info.Length : 0;
            var ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            return StableHash($"{archivePath}|{len}|{ticks}");
        }

        private string GetExtractedEntryPath(string archivePath, string entryPath, string extension)
        {
            var archiveId = GetArchiveId(archivePath);
            var entryId = StableHash(entryPath);
            var ext = extension ?? string.Empty;
            return Path.Combine(_archiveExtractSessionRoot, archiveId, $"{entryId}{ext}");
        }

        private string EnsureZipEntryExtractedToTemp(ImageInfo imageInfo)
        {
            var archivePath = imageInfo.ArchivePath;
            var entryPath = imageInfo.ArchiveEntryPath;
            if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(entryPath))
            {
                throw new InvalidOperationException("压缩包图片缺少 ArchivePath/ArchiveEntryPath 信息。");
            }

            var cacheKey = imageInfo.CacheKey;
            var expectedSize = imageInfo.FileSize;
            var lockObj = _archiveExtractLocks.GetOrAdd(cacheKey, _ => new object());

            lock (lockObj)
            {
                var extractedPath = GetExtractedEntryPath(archivePath, entryPath, imageInfo.FileExtension);

                if (File.Exists(extractedPath))
                {
                    try
                    {
                        if (expectedSize <= 0 || new FileInfo(extractedPath).Length == expectedSize)
                        {
                            TouchExtractCache(cacheKey, extractedPath, expectedSize);
                            return extractedPath;
                        }
                    }
                    catch
                    {
                        // fall through and re-extract
                    }
                }

                var parent = Path.GetDirectoryName(extractedPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                var tempPath = $"{extractedPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    ExtractArchiveEntryToFile(archivePath, entryPath, tempPath);
                    File.Move(tempPath, extractedPath, overwrite: true);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                var actualSize = expectedSize;
                try
                {
                    if (actualSize <= 0 && File.Exists(extractedPath))
                    {
                        actualSize = new FileInfo(extractedPath).Length;
                    }
                }
                catch
                {
                    // ignore
                }

               
                TouchExtractCache(cacheKey, extractedPath, actualSize);
            
                EvictExtractCacheIfNeeded();
                return extractedPath;
            }
        }

        private void TouchExtractCache(string cacheKey, string filePath, long sizeBytes)
        {
            lock (_archiveExtractCacheLock)
            {
                if (_archiveExtractIndex.TryGetValue(cacheKey, out var existing))
                {
                    _archiveExtractTotalBytes -= existing.Value.SizeBytes;
                    existing.Value = new ExtractCacheItem(cacheKey, filePath, sizeBytes);
                    _archiveExtractTotalBytes += sizeBytes;

                    _archiveExtractLru.Remove(existing);
                    _archiveExtractLru.AddFirst(existing);

           
                    return;
                }

                var node = new LinkedListNode<ExtractCacheItem>(new ExtractCacheItem(cacheKey, filePath, sizeBytes));
                _archiveExtractLru.AddFirst(node);
                _archiveExtractIndex.Add(cacheKey, node);
                _archiveExtractTotalBytes += sizeBytes;
            }
        }

        private void EvictExtractCacheIfNeeded()
        {
            lock (_archiveExtractCacheLock)
            {
                while (_archiveExtractIndex.Count > DefaultMaxExtractCacheFiles || _archiveExtractTotalBytes > DefaultMaxExtractCacheBytes)
                {
                    var node = _archiveExtractLru.Last;
                    if (node == null)
                    {
                        break;
                    }

                    var item = node.Value;

                    try
                    {
                        File.Delete(item.FilePath);
                    }
                    catch (IOException)
                    {
                        break;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        break;
                    }

                    _archiveExtractLru.RemoveLast();
                    _archiveExtractIndex.Remove(item.CacheKey);
                    _archiveExtractTotalBytes -= item.SizeBytes;
                }
            }
        }
        /// <summary>
        /// 清除压缩包解压缓存
        /// </summary>
        private void ClearExtractCache()
        {
            lock (_archiveExtractCacheLock)
            {
                foreach (var item in _archiveExtractLru)
                {
                    try
                    {
                        File.Delete(item.FilePath);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                _archiveExtractLru.Clear();
                _archiveExtractIndex.Clear();
                _archiveExtractTotalBytes = 0;
            }

            _archiveExtractLocks.Clear();

            try
            {
                if (Directory.Exists(_archiveExtractSessionRoot))
                {
                    Directory.Delete(_archiveExtractSessionRoot, recursive: true);
                }
            }
            catch
            {
                // ignore
            }
        }


        // 核心加载逻辑
        private BitmapSource? LoadBitmap(ImageInfo imageInfo, int? decodePixelWidth)
        {

            // PDF 页面渲染
            if (imageInfo.SourceKind == ImageSourceKind.PdfPage)
            {
                return _pdfService.RenderPage(imageInfo, decodePixelWidth);
            }


            if (imageInfo.SourceKind == ImageSourceKind.ZipEntry)
            {
                return ArchiveLoadStrategy switch
                {
                    ArchiveLoadStrategy.Stream => LoadBitmapFromZipEntryStream(imageInfo, decodePixelWidth),
                    ArchiveLoadStrategy.TempExtractLru => LoadBitmapFromZipEntryTempExtractLru(imageInfo, decodePixelWidth),
                    _ => LoadBitmapFromZipEntryStream(imageInfo, decodePixelWidth)
                };
            }

            var filePath = imageInfo.FilePath;

            if (PreferBitmapImage(filePath))
            {
                try
                {
                    return LoadWithBitmapImage(filePath, decodePixelWidth);
                }
                catch (Exception bitmapEx)
                {
                    try
                    {
                        return LoadWithMagickNet(filePath, decodePixelWidth);
                    }
                    catch (Exception magickEx)
                    {
                        throw new InvalidOperationException(
                            $"BitmapImage 解码失败: {bitmapEx.Message}; Magick.NET 解码失败: {magickEx.Message}",
                            magickEx);
                    }
                }
            }

            return LoadWithMagickNet(filePath, decodePixelWidth);
        }


        // 从压缩包流加载图片
        private BitmapSource? LoadBitmapFromZipEntryStream(ImageInfo imageInfo, int? decodePixelWidth)
        {
            var archivePath = imageInfo.ArchivePath;
            var entryPath = imageInfo.ArchiveEntryPath;
            if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(entryPath))
            {
                throw new InvalidOperationException("压缩包图片缺少 ArchivePath/ArchiveEntryPath 信息。");
            }

            var bytes = ReadArchiveEntryBytes(archivePath, entryPath, imageInfo.FileSize);

            BitmapSource? bitmap;
            if (PreferBitmapImageExtension(imageInfo.FileExtension))
            {
                try
                {
                    bitmap = LoadWithBitmapImage(bytes, decodePixelWidth);
                }
                catch (Exception bitmapEx)
                {
                    try
                    {
                        bitmap = LoadWithMagickNet(bytes, decodePixelWidth);
                    }
                    catch (Exception magickEx)
                    {
                        throw new InvalidOperationException(
                            $"BitmapImage 解码失败: {bitmapEx.Message}; Magick.NET 解码失败: {magickEx.Message}",
                            magickEx);
                    }
                }
            }
            else
            {
                bitmap = LoadWithMagickNet(bytes, decodePixelWidth);
            }

            return bitmap;
        }


        // 从压缩包临时解压加载图片（带LRU缓存）
        private BitmapSource? LoadBitmapFromZipEntryTempExtractLru(ImageInfo imageInfo, int? decodePixelWidth)
        {
            var extractedPath = EnsureZipEntryExtractedToTemp(imageInfo);

            if (PreferBitmapImage(extractedPath))
            {
                try
                {
                    return LoadWithBitmapImage(extractedPath, decodePixelWidth);
                }
                catch (Exception bitmapEx)
                {
                    try
                    {
                        return LoadWithMagickNet(extractedPath, decodePixelWidth);
                    }
                    catch (Exception magickEx)
                    {
                        throw new InvalidOperationException(
                            $"BitmapImage 解码失败: {bitmapEx.Message}; Magick.NET 解码失败: {magickEx.Message}",
                            magickEx);
                    }
                }
            }

            return LoadWithMagickNet(extractedPath, decodePixelWidth);
        }
        

    
        public IEnumerable<ImageInfo> ScanFolder(string folderPath)
        {
            return ScanFolder(folderPath, includeSubfolders: false, maxSubfolderDepth: 0, filterOptions: null);
        }

        public IEnumerable<ImageInfo> ScanFolder(string folderPath, bool includeSubfolders, int maxSubfolderDepth, ImageFilterOptions? filterOptions = null)
        {
            if (!Directory.Exists(folderPath))
                yield break;

            IEnumerable<string> files;
            try
            {
                files = EnumerateFilesForScan(folderPath, includeSubfolders, maxSubfolderDepth);
            }
            catch
            {
                yield break;
            }

            var comparer = new NaturalStringComparer();
            var shouldApplyFilters = filterOptions != null &&
                                     (filterOptions.ShouldCheckFileSize || filterOptions.ShouldCheckDimensions);
            foreach (var item in files
                         .Where(IsSupportedImage)
                         .Select(file => new { File = file, SortKey = GetRelativeSortKey(folderPath, file) })
                         .OrderBy(x => x.SortKey, comparer)
                         .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase))
            {
                FileInfo? fileInfo = null;
                if (shouldApplyFilters && filterOptions != null && !PassesFolderFilters(item.File, filterOptions, out fileInfo))
                {
                    continue;
                }

                var info = fileInfo != null
                    ? CreateImageInfoFromFileInfo(item.File, fileInfo)
                    : ImageInfo.FromFile(item.File);
                info.RelativePath = item.SortKey;
                yield return info;
            }
        }

        private static IEnumerable<string> EnumerateFilesForScan(string folderPath, bool includeSubfolders, int maxSubfolderDepth)
        {
            var baseOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            if (!includeSubfolders || maxSubfolderDepth == 0)
            {
                return Directory.EnumerateFiles(folderPath, "*", baseOptions);
            }

            if (maxSubfolderDepth < 0)
            {
                var recursiveOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                return Directory.EnumerateFiles(folderPath, "*", recursiveOptions);
            }

            return EnumerateFilesWithDepthLimit(folderPath, maxSubfolderDepth, baseOptions);
        }

        private static IEnumerable<string> EnumerateFilesWithDepthLimit(string rootFolder, int maxSubfolderDepth, EnumerationOptions options)
        {
            var pending = new Stack<(string Folder, int Depth)>();
            pending.Push((rootFolder, 0));

            while (pending.Count > 0)
            {
                var (folder, depth) = pending.Pop();

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*", options);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                if (depth >= maxSubfolderDepth)
                {
                    continue;
                }

                IEnumerable<string> subFolders;
                try
                {
                    subFolders = Directory.EnumerateDirectories(folder, "*", options);
                }
                catch
                {
                    continue;
                }

                foreach (var subFolder in subFolders)
                {
                    pending.Push((subFolder, depth + 1));
                }
            }
        }

        private static string GetRelativeSortKey(string rootFolder, string filePath)
        {
            try
            {
                var relative = Path.GetRelativePath(rootFolder, filePath);
                if (relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return filePath;
                }

                return relative;
            }
            catch
            {
                return filePath;
            }
        }



        // 扫描压缩包内的图片
        public IEnumerable<ImageInfo> ScanArchive(string archivePath)
        {
            if (!File.Exists(archivePath) || !IsSupportedArchive(archivePath))
            {
                return Array.Empty<ImageInfo>();
            }

            var ext = GetArchiveExtension(archivePath);
            var backend = GetArchiveBackend(archivePath);

            if (IsRarLikeArchive(ext) || backend == ArchiveBackend.SharpCompress)
            {
                return ScanArchiveWithSharpCompress(archivePath);
            }

            try
            {
                return ScanArchiveWithZipArchive(archivePath);
            }
            catch (Exception ex) when (ShouldFallbackToSharpCompress(ex))
            {
                ForceSharpCompressBackend(archivePath);
                return ScanArchiveWithSharpCompress(archivePath);
            }
        }

        private static IReadOnlyList<ImageInfo> ScanArchiveWithZipArchive(string archivePath)
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var entries = archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Where(e => IsSupportedImageExtension(Path.GetExtension(e.FullName)))
                .OrderBy(e => e.FullName, new NaturalStringComparer())
                .ToList();

            var result = new List<ImageInfo>(entries.Count);
            foreach (var entry in entries)
            {
                var modified = entry.LastWriteTime.LocalDateTime;
                result.Add(ImageInfo.FromZipEntry(archivePath, entry.FullName, entry.Length, modified));
            }

            return result;
        }

        private IReadOnlyList<ImageInfo> ScanArchiveWithSharpCompress(string archivePath)
        {
            ForceSharpCompressBackend(archivePath);

            if (_archivePasswordCancelled.ContainsKey(archivePath))
            {
                throw new OperationCanceledException("已取消输入密码。");
            }

            var ext = GetArchiveExtension(archivePath);
            var isRar = IsRarLikeArchive(ext);

            _archivePasswordCache.TryGetValue(archivePath, out var password);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var archive = OpenSharpCompressArchive(archivePath, password);

                    var entries = archive.Entries
                        .Where(e => !e.IsDirectory)
                        .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                        .Where(e => IsSupportedImageExtension(Path.GetExtension(e.Key)))
                        .OrderBy(e => e.Key, new NaturalStringComparer())
                        .ToList();

                    var result = new List<ImageInfo>(entries.Count);
                    foreach (var entry in entries)
                    {
                        var modified = entry.LastModifiedTime?.ToLocalTime() ?? DateTime.MinValue;
                        result.Add(ImageInfo.FromZipEntry(archivePath, entry.Key, entry.Size, modified));
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    var hasPassword = !string.IsNullOrEmpty(password);
                    var shouldPrompt = !hasPassword ? (isRar || LooksLikePasswordRelatedFailure(ex)) : LooksLikePasswordRelatedFailure(ex);
                    if (!shouldPrompt || attempt >= 2)
                    {
                        throw;
                    }

                    var message = hasPassword ? "密码错误或无法解密，请重试：" : "请输入压缩包密码：";
                    var newPassword = GetOrPromptArchivePassword(archivePath, message, password, forceNewPassword: hasPassword);
                    if (newPassword == null)
                    {
                        throw new OperationCanceledException("已取消输入密码。", ex);
                    }

                    password = newPassword;
                }
            }

            return Array.Empty<ImageInfo>();
        }


        /// <summary>
        /// 原子化的获取并Touch缩略图
        /// </summary>
        private bool TryGetAndTouchThumbnail(string cacheKey, out BitmapSource? value)
        {
            lock (_thumbCacheLock)
            {
                if (_thumbnailCache.TryGetValue(cacheKey, out value))
                {
                    TouchThumbnailCache_NoLock(cacheKey);
                    return true;
                }

                value = null;
                return false;
            }
        }

        // 异步加载缩略图
        public async Task<BitmapSource?> LoadThumbnailAsync(ImageInfo imageInfo, int size = 120, CancellationToken cancellationToken = default)
        {
            var cacheKey = imageInfo.CacheKey;

            // 原子化检查缩略图缓存
            if (TryGetAndTouchThumbnail(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
                await _loadSemaphore.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            try
            {
                // 第二次检查
                if (TryGetAndTouchThumbnail(cacheKey, out cached))
                {
                    return cached;
                }

                BitmapSource? thumbnail;
                try
                {
                    thumbnail = await Task.Run(() =>
                    {
                        try
                        {
                            BitmapSource? bitmap;

                            // PDF 页面使用专门的缩略图渲染
                            if (imageInfo.SourceKind == ImageSourceKind.PdfPage)
                            {
                                bitmap = _pdfService.RenderThumbnail(imageInfo, size);
                            }
                            else
                            {
                                bitmap = LoadBitmap(imageInfo, size);
                            }

                            if (bitmap == null)
                            {
                                return null;
                            }

                            imageInfo.Width = bitmap.PixelWidth;
                            imageInfo.Height = bitmap.PixelHeight;
                            return bitmap;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            imageInfo.HasError = true;
                            imageInfo.ErrorMessage = ex.Message;
                            return null;
                        }
                    }, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                
                if (thumbnail != null)
                {
                    AddThumbnailToCache(cacheKey, thumbnail);
                }
                
                return thumbnail;
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }



        /// <summary>
        /// 原子化的获取+Touch操作
        /// </summary>
        private bool TryGetAndTouch(string cacheKey, out BitmapSource? value)
        {
            lock (_imageCacheLock)
            {
                if (_imageCache.TryGetValue(cacheKey, out value))
                {
                    TouchImageCache_NoLock(cacheKey);
                    return true;
                }
                return false;
            }
        }



        /// <summary>
        ///异步加载图片
        ///场景：两个线程同时请求同一个图片 需要 Double-Check Locking模式 用于防止多线程重复加载
        /// T1: 第一次检查 → 未命中 → 等待信号量
        // T2: 第一次检查 → 未命中 → 等待信号量
        // T1: 获得信号量 → 加载图片 → 添加缓存 → 释放信号量
        // T2: 获得信号量 → 第二次检查 → 命中！ → 返回（避免重复加载）
        /// 
        /// </summary>
        public async Task<BitmapSource?> LoadImageAsync(ImageInfo imageInfo, int? maxSize = null, CancellationToken cancellationToken = default)
        {
            var baseKey = imageInfo.CacheKey;
            var cacheKey = maxSize.HasValue ? $"{baseKey}_{maxSize}" : baseKey;


            // 第一次检查：原子化操作
            if (TryGetAndTouch(cacheKey, out var cached))
            {
                return cached;
            }

            var cts = new CancellationTokenSource();
            _loadingTasks.TryAdd(baseKey, cts);
            
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            
            bool semaphoreAcquired = false;
            try
            {
                await _loadSemaphore.WaitAsync(linkedCts.Token);
                semaphoreAcquired = true;

                // 第二次检查：原子化操作
                if (TryGetAndTouch(cacheKey, out cached))
                {
                    return cached;
                }


                imageInfo.IsLoading = true;
                
                BitmapSource? image;
                try
                {
                    image = await Task.Run(() =>
                    {
                        try
                        {
                            var bitmap = LoadBitmap(imageInfo, maxSize);

                            if (bitmap == null)
                            {
                                return null;
                            }

                            imageInfo.Width = bitmap.PixelWidth;
                            imageInfo.Height = bitmap.PixelHeight;
                            return bitmap;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            imageInfo.HasError = true;
                            imageInfo.ErrorMessage = ex.Message;
                            return null;
                        }
                    }, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                
                if (image != null)
                {
                    AddToCache(cacheKey, image);
                }
                
                return image;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                imageInfo.IsLoading = false;
                if (_loadingTasks.TryRemove(baseKey, out var removedCts))
                {
                    removedCts.Dispose();
                }

                if (semaphoreAcquired)
                {
                    _loadSemaphore.Release();
                }
            }
        }
        
        public void CancelLoading(string filePath)
        {
            if (_loadingTasks.TryRemove(filePath, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
        
        public void PreloadImages(IEnumerable<ImageInfo> images, CancellationToken cancellationToken = default)
        {
            foreach (var image in images)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _ = LoadImageAsync(image, null, cancellationToken);
            }
        }
        /// <summary>旋转90度命令</summary>
        public BitmapSource RotateImage(BitmapSource source, double angle)
        {
            var transform = new System.Windows.Media.RotateTransform(angle);
            var rotated = new TransformedBitmap(source, transform);
            rotated.Freeze();
            return rotated;
        }


        // 添加同步加载图片方法
        public BitmapSource? LoadImage(string filePath)
        {
            try
            {
                if (PreferBitmapImage(filePath))
                {
                    try
                    {
                        return LoadWithBitmapImage(filePath);
                    }
                    catch
                    {
                        return LoadWithMagickNet(filePath);
                    }
                }

                return LoadWithMagickNet(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图片失败: {ex.Message}");
                return null;
            }
        }

        // 添加同步加载缩略图方法
        public BitmapSource? LoadThumbnail(string filePath, int size = 120)
        {
            if (_thumbnailCache.TryGetValue(filePath, out var cached))
            {
                lock (_cacheLock) 
                {
                    TouchThumbnailCache_NoLock(filePath);
                }
                return cached;
            }

            try
            {
                BitmapSource? bitmap;
                if (PreferBitmapImage(filePath))
                {
                    try
                    {
                        bitmap = LoadWithBitmapImage(filePath, size);
                    }
                    catch
                    {
                        bitmap = LoadWithMagickNet(filePath, size);
                    }
                }
                else
                {
                    bitmap = LoadWithMagickNet(filePath, size);
                }

                if (bitmap == null)
                {
                    return null;
                }

                AddThumbnailToCache(filePath, bitmap);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载缩略图失败: {ex.Message}");
                return null;
            }
        }


        #region LRU缓存管理 - 图片缓存

        /// <summary>
        /// LRU Touch操作：将key移动到链表头部（标记为最近使用）
        /// 原理：LRU链表头部=最近访问，尾部=最久未访问
        /// </summary>
        private void TouchImageCache_NoLock(string key)
        {
            if (_imageLruIndex.TryGetValue(key, out var node))
            {
                _imageLru.Remove(node);// 从当前位置移除
                _imageLru.AddFirst(node); // 重新插入到头部
            }
        }



        /// <summary>
        /// LRU驱逐策略：当缓存超过容量时，删除最久未使用的项（链表尾部）
        /// 时间复杂度：O(1) - 直接访问尾节点
        /// </summary>
        private void EvictImageCacheIfNeeded_NoLock()
        {
            while (_imageLruIndex.Count > _maxCacheSize)
            {
                var node = _imageLru.Last;  // 获取链表尾部（
                if (node == null) break;

                var oldestKey = node.Value;


                // 三个结构同步删除
                _imageLru.RemoveLast();            // 1. 从链表移除
                _imageLruIndex.Remove(oldestKey);  // 2. 从索引移除
                _imageCache.TryRemove(oldestKey, out _); // 3. 从数据缓存移除
            } 
        }

      



        /// <summary>
        /// 添加图片到缓存
        /// 如果key已存在：更新值并Touch（避免重复节点破坏LRU结构）
        /// 如果key不存在：插入头部并触发驱逐检查
        /// </summary>
        private void AddToCache(string key, BitmapSource image)
        {
            lock (_cacheLock)
            {
                // 已存在则更新并Touch
                if (_imageCache.ContainsKey(key))
                {
                    _imageCache[key] = image;
                    TouchImageCache_NoLock(key);
                    return;
                }

                // 加入字典 + 加到 LRU 头
                _imageCache[key] = image;
                var node = new LinkedListNode<string>(key);
                _imageLru.AddFirst(node);    // 插入链表头部
                _imageLruIndex[key] = node;  // 建立索引映射

                EvictImageCacheIfNeeded_NoLock(); // 检查是否需要驱逐
            }
        }
        #endregion

        #region LRU缓存管理 - 缩略图缓存
        /// <summary>
        /// 缩略图LRU Touch操作（同图片缓存逻辑）
        /// </summary>
        private void TouchThumbnailCache_NoLock(string key)
        {
            if (_thumbLruIndex.TryGetValue(key, out var node))
            {
                _thumbLru.Remove(node);
                _thumbLru.AddFirst(node);
            }
        }


        /// <summary>
        /// LRU驱逐策略：当缓存超过容量时，删除最久未使用的项（链表尾部）
        /// 时间复杂度：O(1) - 直接访问尾节点
        /// </summary>
        private void EvictThumbnailCacheIfNeeded_NoLock()
        {
            while (_thumbLruIndex.Count > _maxThumbnailCacheSize)
            {
                var node = _thumbLru.Last;
                if (node == null) break;

                var oldestKey = node.Value;

                _thumbLru.RemoveLast();
                _thumbLruIndex.Remove(oldestKey);
                _thumbnailCache.TryRemove(oldestKey, out _);
            }
        }


        /// <summary>
        /// 添加图片到缓存
        /// 如果key已存在：更新值并Touch（避免重复节点破坏LRU结构）
        /// 如果key不存在：插入头部并触发驱逐检查
        /// </summary>
        private void AddThumbnailToCache(string key, BitmapSource thumbnail)
        {
            lock (_cacheLock)
            {
                // 更新并 touch（避免重复 key 让 LRU 乱）
                if (_thumbnailCache.TryGetValue(key, out _))  // 一次查询即可
                {
                    _thumbnailCache[key] = thumbnail;
                    TouchThumbnailCache_NoLock(key);
                    return;
                }

                _thumbnailCache[key] = thumbnail;

                var node = new LinkedListNode<string>(key);
                _thumbLru.AddFirst(node);
                _thumbLruIndex[key] = node;

                EvictThumbnailCacheIfNeeded_NoLock();
            }
        }
        #endregion


        /// <summary>
        /// 从缓存键中提取基础键（移除尺寸标记）
        /// 例如: "zip|entry|max=4096" → "zip|entry"
        /// </summary>
        private static string GetBaseCacheKey(string cacheKey)
        {
            //  zip|entry|max=4096  →  zip|entry
            var idx = cacheKey.IndexOf("|max=", StringComparison.Ordinal);
            return idx >= 0 ? cacheKey[..idx] : cacheKey;

        }


        /// <summary>
        /// 保存旋转后的图片
        /// 注意：需要清除所有相关缓存（数据+LRU结构）
        /// </summary>
        public void SaveRotatedImage(string filePath, BitmapSource rotatedImage)
        {
            try
            {
                BitmapEncoder encoder = Path.GetExtension(filePath).ToLower() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                    ".png" => new PngBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    ".gif" => new GifBitmapEncoder(),
                    ".tiff" or ".tif" => new TiffBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                encoder.Frames.Add(BitmapFrame.Create(rotatedImage));

                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                encoder.Save(stream);

                // 清除该图片的缓存
                lock (_cacheLock)
                {
                    _imageCache.TryRemove(filePath, out _);
                    _thumbnailCache.TryRemove(filePath, out _);

                    // 清理LRU结构
                    if (_imageLruIndex.TryGetValue(filePath, out var imgNode))
                    {
                        _imageLru.Remove(imgNode);
                        _imageLruIndex.Remove(filePath);
                    }
                    if (_thumbLruIndex.TryGetValue(filePath, out var thumbNode))
                    {
                        _thumbLru.Remove(thumbNode);
                        _thumbLruIndex.Remove(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存图片失败: {ex.Message}");
                throw;
            }
        }


        //public void SaveRotatedImage(string filePath, BitmapSource rotatedImage)
        //{
        //    try
        //    {
        //        var encoder = GetEncoderForExtension(Path.GetExtension(filePath));
        //        encoder.Frames.Add(BitmapFrame.Create(rotatedImage));

        //        using var stream = new FileStream(filePath, FileMode.Create);
        //        encoder.Save(stream);
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"保存旋转图片失败: {ex.Message}");
        //        throw;
        //    }
        //}

        private BitmapEncoder GetEncoderForExtension(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".png" => new PngBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                ".tiff" or ".tif" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
        }


        /// <summary>
        /// 压缩包解压缓存项记录
        /// </summary>
        private sealed record ExtractCacheItem(string CacheKey, string FilePath, long SizeBytes);

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _imageCache.Clear();
                _thumbnailCache.Clear();
    
                _thumbLru.Clear();
                _thumbLruIndex.Clear();
                _imageLru.Clear();
                _imageLruIndex.Clear();
            }

            _archiveBackendCache.Clear();
            _archivePasswordCache.Clear();
            _archivePasswordLocks.Clear();
            _archivePasswordCancelled.Clear();

            ClearExtractCache();
        }
        
        public void Dispose()
        {
            foreach (var cts in _loadingTasks.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _loadingTasks.Clear();
            _loadSemaphore.Dispose();

            _pdfService.Dispose();
            ClearCache();
        }


        /// <summary>
        /// 检测 GIF 文件的帧数
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>帧数，非 GIF 或错误返回 1</returns>
        public static int GetGifFrameCount(string filePath)
        {
            try
            {
                var ext = Path.GetExtension(filePath);
                if (!string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
                    return 1;

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return GetGifFrameCountFromStream(stream);
            }
            catch
            {
                return 1;
            }
        }


        // 关闭 PDF 文档的方法
        public void ClosePdf(string pdfPath)
        {
            _pdfService.CloseDocument(pdfPath);
        }
        /// <summary>
        /// 从字节数组检测 GIF 帧数
        /// </summary>
        public static int GetGifFrameCount(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                return GetGifFrameCountFromStream(stream);
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// 从流检测 GIF 帧数
        /// </summary>
        public static int GetGifFrameCountFromStream(Stream stream)
        {
            try
            {
                var decoder = new GifBitmapDecoder(
                    stream,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                return decoder.Frames.Count;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// 加载 GIF 原始数据（用于 GifViewerControl）
        /// </summary>
        public static byte[]? LoadGifData(string filePath)
        {
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从压缩包加载 GIF 原始数据
        /// </summary>
        public byte[]? LoadGifDataFromArchive(ImageInfo imageInfo)
        {
            if (imageInfo.SourceKind != ImageSourceKind.ZipEntry)
                return null;

            try
            {
                // 复用现有的压缩包读取逻辑
                return LoadArchiveEntryBytes(imageInfo.ArchivePath!, imageInfo.ArchiveEntryPath!);
            }
            catch
            {
                return null;
            }
        }


        /// <summary>
        /// 从压缩包读取条目的原始字节
        /// </summary>
        private byte[]? LoadArchiveEntryBytes(string archivePath, string entryPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entry = archive.GetEntry(entryPath);
                if (entry == null) return null;

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }


    #region 自然排序比较器

    /// <summary>
    /// 自然排序比较器（处理文件名中的数字）
    /// 例如: file1.jpg, file2.jpg, file10.jpg 按数字顺序排列
    /// 而不是字典序: file1.jpg, file10.jpg, file2.jpg
    /// </summary>

    public class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            
            int ix = 0, iy = 0;
            
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    var numX = ExtractNumber(x, ref ix);
                    var numY = ExtractNumber(y, ref iy);
                    
                    if (numX != numY)
                        return numX.CompareTo(numY);
                }
                else
                {
                    var charCompare = char.ToLowerInvariant(x[ix]).CompareTo(char.ToLowerInvariant(y[iy]));
                    if (charCompare != 0)
                        return charCompare;
                    
                    ix++;
                    iy++;
                }
            }
            
            return x.Length.CompareTo(y.Length);
        }
        /// <summary>
        /// 从字符串中提取连续的数字
        /// </summary>
        private static long ExtractNumber(string s, ref int index)
        {
            var start = index;
            while (index < s.Length && char.IsDigit(s[index]))
                index++;
            
            if (long.TryParse(s.Substring(start, index - start), out var result))
                return result;
            
            return 0;
        }

        #endregion
    }
}
