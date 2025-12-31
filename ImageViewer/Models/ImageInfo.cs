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

        private string? _exifDateTaken;
        public string? ExifDateTaken
        {
            get => _exifDateTaken;
            private set => SetProperty(ref _exifDateTaken, value);
        }

        private string? _exifCameraModel;
        public string? ExifCameraModel
        {
            get => _exifCameraModel;
            private set => SetProperty(ref _exifCameraModel, value);
        }

        private string? _exifAperture;
        public string? ExifAperture
        {
            get => _exifAperture;
            private set => SetProperty(ref _exifAperture, value);
        }

        private string? _exifShutterSpeed;
        public string? ExifShutterSpeed
        {
            get => _exifShutterSpeed;
            private set => SetProperty(ref _exifShutterSpeed, value);
        }

        private string? _exifIso;
        public string? ExifIso
        {
            get => _exifIso;
            private set => SetProperty(ref _exifIso, value);
        }

        private string? _exifFocalLength;
        public string? ExifFocalLength
        {
            get => _exifFocalLength;
            private set => SetProperty(ref _exifFocalLength, value);
        }

        private string? _exifGpsLocation;
        public string? ExifGpsLocation
        {
            get => _exifGpsLocation;
            private set => SetProperty(ref _exifGpsLocation, value);
        }

        private double? _exifGpsLatitude;
        public double? ExifGpsLatitude
        {
            get => _exifGpsLatitude;
            private set => SetProperty(ref _exifGpsLatitude, value);
        }

        private double? _exifGpsLongitude;
        public double? ExifGpsLongitude
        {
            get => _exifGpsLongitude;
            private set => SetProperty(ref _exifGpsLongitude, value);
        }

        public bool HasExifGps => ExifGpsLatitude.HasValue && ExifGpsLongitude.HasValue;
        
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


        [ObservableProperty]
        private int _gifFrameCount;


        [ObservableProperty]
        private byte[]? _gifData;


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

        public void UpdateExifMetadata(ExifMetadata? metadata)
        {
            ExifDateTaken = metadata?.DateTaken;
            ExifCameraModel = metadata?.CameraModel;
            ExifAperture = metadata?.Aperture;
            ExifShutterSpeed = metadata?.ShutterSpeed;
            ExifIso = metadata?.Iso;
            ExifFocalLength = metadata?.FocalLength;
            ExifGpsLocation = metadata?.GpsLocation;
            ExifGpsLatitude = metadata?.GpsLatitude;
            ExifGpsLongitude = metadata?.GpsLongitude;
            OnPropertyChanged(nameof(HasExifGps));
        }


  
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

        public void UpdateFilePath(string newFilePath, string? relativePath = null, FileInfo? fileInfo = null)
        {
            if (string.IsNullOrWhiteSpace(newFilePath))
            {
                return;
            }

            FilePath = newFilePath;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(FileExtension));
            OnPropertyChanged(nameof(CacheKey));
            OnPropertyChanged(nameof(DisplayPath));
            OnPropertyChanged(nameof(IsAnimatedGif));

            if (relativePath != null)
            {
                RelativePath = relativePath;
                OnPropertyChanged(nameof(RelativePath));
            }

            if (fileInfo != null)
            {
                FileSize = fileInfo.Exists ? fileInfo.Length : 0;
                DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue;
                OnPropertyChanged(nameof(FileSize));
                OnPropertyChanged(nameof(FileSizeFormatted));
                OnPropertyChanged(nameof(DateModified));
            }
        }




    }
}

