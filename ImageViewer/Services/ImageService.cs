using ImageViewer.Models;
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
        private static readonly string[] SupportedExtensions = 
        { 
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
        };
        
        private readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new();
        private readonly ConcurrentDictionary<string, BitmapSource> _imageCache = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _loadingTasks = new();
        private readonly SemaphoreSlim _loadSemaphore = new(4);
        private readonly int _maxCacheSize = 50;
        private readonly Queue<string> _cacheOrder = new();
        private readonly object _cacheLock = new();
        
        public static bool IsSupportedImage(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
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
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(imageInfo.FilePath);
                        bitmap.DecodePixelWidth = size;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        imageInfo.Width = bitmap.PixelWidth * size / (bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 1);
                        imageInfo.Height = bitmap.PixelHeight * size / (bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 1);
                        
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
            
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            
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
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(imageInfo.FilePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        
                        if (maxSize.HasValue)
                        {
                            bitmap.DecodePixelWidth = maxSize.Value;
                        }
                        
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
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
                _loadingTasks.TryRemove(imageInfo.FilePath, out _);
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
        /// <summary>Ðý×ª90¶ÈÃüÁî</summary>
        public BitmapSource RotateImage(BitmapSource source, double angle)
        {
            var transform = new System.Windows.Media.RotateTransform(angle);
            var rotated = new TransformedBitmap(source, transform);
            rotated.Freeze();
            return rotated;
        }


        // Ìí¼ÓÍ¬²½¼ÓÔØÍ¼Æ¬·½·¨
        public BitmapSource? LoadImage(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"¼ÓÔØÍ¼Æ¬Ê§°Ü: {ex.Message}");
                return null;
            }
        }

        // Ìí¼ÓÍ¬²½¼ÓÔØËõÂÔÍ¼·½·¨
        public BitmapSource? LoadThumbnail(string filePath, int size = 120)
        {
            if (_thumbnailCache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.DecodePixelWidth = size;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();

                _thumbnailCache.TryAdd(filePath, bitmap);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"¼ÓÔØËõÂÔÍ¼Ê§°Ü: {ex.Message}");
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

                // Çå³ý¸ÃÍ¼Æ¬µÄ»º´æ
                _imageCache.TryRemove(filePath, out _);
                _thumbnailCache.TryRemove(filePath, out _);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"±£´æÍ¼Æ¬Ê§°Ü: {ex.Message}");
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
        //        Debug.WriteLine($"±£´æÐý×ªÍ¼Æ¬Ê§°Ü: {ex.Message}");
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
