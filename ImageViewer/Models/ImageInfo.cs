using System;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Models
{
    public partial class ImageInfo : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string FileExtension => Path.GetExtension(FilePath).ToLowerInvariant();
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
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
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                DateModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue
            };
        }
    }
}
