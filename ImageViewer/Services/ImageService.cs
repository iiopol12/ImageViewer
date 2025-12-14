using ImageViewer.Models;
using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    public class ImageService : IDisposable
    {
        private static readonly string[] BitmapImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
        };

        private static readonly string[] MagickNetExtensions =
        {
            ".avif", ".heic", ".heif", ".jxl"
        };

        private static readonly HashSet<string> BitmapImageExtensionSet = new(BitmapImageExtensions, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportedExtensionSet = new(BitmapImageExtensions.Concat(MagickNetExtensions), StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> SupportedExtensions { get; } = SupportedExtensionSet.OrderBy(e => e).ToArray();

        public static string OpenFileDialogFilter =>
            $"图片文件|{string.Join(';', SupportedExtensions.Select(ext => $"*{ext}"))}|所有文件|*.*";
        
        private readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new();
        private readonly ConcurrentDictionary<string, BitmapSource> _imageCache = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _loadingTasks = new();
        private readonly SemaphoreSlim _loadSemaphore = new(4);
        private readonly int _maxCacheSize = 50;
        private readonly Queue<string> _cacheOrder = new();
        private readonly object _cacheLock = new();
        
        public static bool IsSupportedImage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && SupportedExtensionSet.Contains(ext);
        }

        private static bool PreferBitmapImage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(ext) && BitmapImageExtensionSet.Contains(ext);
        }

        private static BitmapSource? LoadWithBitmapImage(string filePath, int? decodePixelWidth = null)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
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

        private static BitmapSource? LoadWithMagickNet(string filePath, int? decodePixelWidth = null)
        {
            using var image = new MagickImage(filePath);
            image.AutoOrient();

            if (decodePixelWidth.HasValue && decodePixelWidth.Value > 0)
            {
                image.Resize((uint)decodePixelWidth.Value, 0);
            }

            var bitmapSource = image.ToBitmapSource();
            bitmapSource.Freeze();
            return bitmapSource;
        }
        
        public IEnumerable<ImageInfo> ScanFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                yield break;
            
            var files = Directory.GetFiles(folderPath)
                .Where(f => IsSupportedImage(f))
                .OrderBy(f => f, new NaturalStringComparer());
            
            foreach (var file in files)
            {
                yield return ImageInfo.FromFile(file);
            }
        }
        
        public async Task<BitmapSource?> LoadThumbnailAsync(ImageInfo imageInfo, int size = 120, CancellationToken cancellationToken = default)
        {
            if (_thumbnailCache.TryGetValue(imageInfo.FilePath, out var cached))
            {
                return cached;
            }
            
            await _loadSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_thumbnailCache.TryGetValue(imageInfo.FilePath, out cached))
                {
                    return cached;
                }
                
                var thumbnail = await Task.Run(() =>
                {
                    try
                    {
                        BitmapSource? bitmap;
                        if (PreferBitmapImage(imageInfo.FilePath))
                        {
                            try
                            {
                                bitmap = LoadWithBitmapImage(imageInfo.FilePath, size);
                            }
                            catch (Exception bitmapEx)
                            {
                                try
                                {
                                    bitmap = LoadWithMagickNet(imageInfo.FilePath, size);
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
                            bitmap = LoadWithMagickNet(imageInfo.FilePath, size);
                        }

                        if (bitmap == null)
                        {
                            return null;
                        }

                        imageInfo.Width = bitmap.PixelWidth;
                        imageInfo.Height = bitmap.PixelHeight;
                        return bitmap;
                    }
                    catch (Exception ex)
                    {
                        imageInfo.HasError = true;
                        imageInfo.ErrorMessage = ex.Message;
                        return null;
                    }
                }, cancellationToken);
                
                if (thumbnail != null)
                {
                    _thumbnailCache.TryAdd(imageInfo.FilePath, thumbnail);
                }
                
                return thumbnail;
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }
        
        public async Task<BitmapSource?> LoadImageAsync(ImageInfo imageInfo, int? maxSize = null, CancellationToken cancellationToken = default)
        {
            var cacheKey = maxSize.HasValue ? $"{imageInfo.FilePath}_{maxSize}" : imageInfo.FilePath;
            
            if (_imageCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
            
            var cts = new CancellationTokenSource();
            _loadingTasks.TryAdd(imageInfo.FilePath, cts);
            
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            
            await _loadSemaphore.WaitAsync(linkedCts.Token);
            try
            {
                if (_imageCache.TryGetValue(cacheKey, out cached))
                {
                    return cached;
                }
                
                imageInfo.IsLoading = true;
                
                var image = await Task.Run(() =>
                {
                    try
                    {
                        BitmapSource? bitmap;
                        if (PreferBitmapImage(imageInfo.FilePath))
                        {
                            try
                            {
                                bitmap = LoadWithBitmapImage(imageInfo.FilePath, maxSize);
                            }
                            catch (Exception bitmapEx)
                            {
                                try
                                {
                                    bitmap = LoadWithMagickNet(imageInfo.FilePath, maxSize);
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
                            bitmap = LoadWithMagickNet(imageInfo.FilePath, maxSize);
                        }

                        if (bitmap == null)
                        {
                            return null;
                        }

                        imageInfo.Width = bitmap.PixelWidth;
                        imageInfo.Height = bitmap.PixelHeight;
                        return bitmap;
                    }
                    catch (Exception ex)
                    {
                        imageInfo.HasError = true;
                        imageInfo.ErrorMessage = ex.Message;
                        return null;
                    }
                }, linkedCts.Token);
                
                if (image != null)
                {
                    AddToCache(cacheKey, image);
                }
                
                return image;
            }
            finally
            {
                imageInfo.IsLoading = false;
                if (_loadingTasks.TryRemove(imageInfo.FilePath, out var removedCts))
                {
                    removedCts.Dispose();
                }
                _loadSemaphore.Release();
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

                _thumbnailCache.TryAdd(filePath, bitmap);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载缩略图失败: {ex.Message}");
                return null;
            }
        }

       

        private void AddToCache(string key, BitmapSource image)
        {
            lock (_cacheLock)
            {
                if (_cacheOrder.Count >= _maxCacheSize)
                {
                    var oldest = _cacheOrder.Dequeue();
                    _imageCache.TryRemove(oldest, out _);
                }
                
                _imageCache.TryAdd(key, image);
                _cacheOrder.Enqueue(key);
            }
        }

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
                _imageCache.TryRemove(filePath, out _);
                _thumbnailCache.TryRemove(filePath, out _);
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
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _imageCache.Clear();
                _thumbnailCache.Clear();
                _cacheOrder.Clear();
            }
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
            ClearCache();
        }
    }
    
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
        
        private static long ExtractNumber(string s, ref int index)
        {
            var start = index;
            while (index < s.Length && char.IsDigit(s[index]))
                index++;
            
            if (long.TryParse(s.Substring(start, index - start), out var result))
                return result;
            
            return 0;
        }
    }
}
