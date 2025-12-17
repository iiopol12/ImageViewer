using System;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Models
{
    public enum ImageSourceKind
    {
        File = 0,
        ZipEntry = 1
    }

    public partial class ImageInfo : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;

        public ImageSourceKind SourceKind { get; set; } = ImageSourceKind.File;
        public string? ArchivePath { get; set; }
        public string? ArchiveEntryPath { get; set; }

        public string FileName =>
            SourceKind == ImageSourceKind.ZipEntry
                ? Path.GetFileName(ArchiveEntryPath ?? string.Empty)
                : Path.GetFileName(FilePath);

        public string FileExtension =>
            SourceKind == ImageSourceKind.ZipEntry
                ? Path.GetExtension(ArchiveEntryPath ?? string.Empty).ToLowerInvariant()
                : Path.GetExtension(FilePath).ToLowerInvariant();

        public string CacheKey =>
            SourceKind == ImageSourceKind.ZipEntry
                ? $"zip:{ArchivePath}|{ArchiveEntryPath}"
                : FilePath;

        public string DisplayPath =>
            SourceKind == ImageSourceKind.ZipEntry
                ? $"{ArchivePath}::{ArchiveEntryPath}"
                : FilePath;

        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int BitDepth { get; private set; }
        public DateTime DateModified { get; set; }
        
        [ObservableProperty]
        private BitmapSource? _thumbnail;
        
        [ObservableProperty]
        private BitmapSource? _fullImage;
        
        [ObservableProperty]
        private bool _isLoading;
        
        [ObservableProperty]
        private bool _isBookmarked;
        
        [ObservableProperty]
        private bool _isCurrent;
        
        [ObservableProperty]
        private bool _hasError;
        
        [ObservableProperty]
        private string _errorMessage = string.Empty;


        [ObservableProperty]

        private bool _isSelected;


        [ObservableProperty]
        private double _rotationAngle = 0;

       
        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }
        
        public string DimensionsFormatted => $"{Width} × {Height}";
        
        public bool IsWide => Width > Height * 1.5;
        
        public static ImageInfo FromFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            return new ImageInfo
            {
                FilePath = filePath,
                SourceKind = ImageSourceKind.File,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue
            };
        }

        public static ImageInfo FromZipEntry(string archivePath, string entryPath, long uncompressedSize, DateTime dateModified)
        {
            return new ImageInfo
            {
                FilePath = $"{archivePath}::{entryPath}",
                SourceKind = ImageSourceKind.ZipEntry,
                ArchivePath = archivePath,
                ArchiveEntryPath = entryPath,
                FileSize = uncompressedSize,
                DateModified = dateModified
            };
        }

        /// <summary>
        /// 更新图片元数据供信息面板展示（在 UI 线程调用）
        /// </summary>
        public void UpdateMetadata(BitmapSource source)
        {
            Width = source.PixelWidth;
            Height = source.PixelHeight;
            BitDepth = source.Format.BitsPerPixel;

            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(DimensionsFormatted));
            OnPropertyChanged(nameof(BitDepth));
        }
    }
}
