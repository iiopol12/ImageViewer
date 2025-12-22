using System;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Models
{
    public enum ImageSourceKind
    {
        File = 0,
        ZipEntry = 1, // 压缩包内图片
        PdfPage = 2
    }

    public partial class ImageInfo : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;

        public ImageSourceKind SourceKind { get; set; } = ImageSourceKind.File;
        public string? ArchivePath { get; set; }
        public string? ArchiveEntryPath { get; set; }

        /// <summary>
        /// </summary>
        public int PdfPageIndex { get; set; }



        public string FileName =>
       SourceKind switch
       {
           ImageSourceKind.ZipEntry => Path.GetFileName(ArchiveEntryPath ?? string.Empty),
           ImageSourceKind.PdfPage => $"第 {PdfPageIndex + 1} 页",
           _ => Path.GetFileName(FilePath)
       };

        public string FileExtension =>
         SourceKind switch
         {
             ImageSourceKind.ZipEntry => Path.GetExtension(ArchiveEntryPath ?? string.Empty).ToLowerInvariant(),
             ImageSourceKind.PdfPage => ".pdf",
             _ => Path.GetExtension(FilePath).ToLowerInvariant()
         };

        public string CacheKey =>
         SourceKind switch
         {
             ImageSourceKind.ZipEntry => $"zip:{ArchivePath}|{ArchiveEntryPath}",
             ImageSourceKind.PdfPage => $"pdf:{FilePath}|page={PdfPageIndex}",
             _ => FilePath
         };

        public string DisplayPath =>
       SourceKind switch
       {
           ImageSourceKind.ZipEntry => $"{ArchivePath}::{ArchiveEntryPath}",
           ImageSourceKind.PdfPage => $"{FilePath} - 第 {PdfPageIndex + 1} 页",
           _ => FilePath
       };



        public string RelativePath { get; set; } = string.Empty;

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

        /// <summary>
        /// </summary>
        [ObservableProperty]
        private int _gifFrameCount;

        /// <summary>
        /// </summary>
        [ObservableProperty]
        private byte[]? _gifData;

        /// <summary>
        /// </summary>
        public bool IsAnimatedGif => FileExtension == ".gif" && GifFrameCount > 1;

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
                RelativePath = entryPath,
                FileSize = uncompressedSize,
                DateModified = dateModified
            };
        }

        /// <summary>
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


        /// <summary>
        /// </summary>
        public void UpdateGifMetadata(int frameCount, byte[]? gifData = null)
        {
            GifFrameCount = frameCount;
            if (gifData != null)
            {
                GifData = gifData;
            }
            OnPropertyChanged(nameof(IsAnimatedGif));
        }

        public static ImageInfo FromPdfPage(string pdfPath, int pageIndex, int width, int height, long estimatedSize, DateTime dateModified)
        {
            return new ImageInfo
            {
                FilePath = pdfPath,
                SourceKind = ImageSourceKind.PdfPage,
                PdfPageIndex = pageIndex,
                Width = width,
                Height = height,
                FileSize = estimatedSize,
                DateModified = dateModified,
                RelativePath = $"Page {pageIndex + 1}"
            };
        }




    }
}

