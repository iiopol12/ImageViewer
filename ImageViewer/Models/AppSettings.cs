using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Models
{
    public enum ViewMode
    {
        Single,
        Manga,
        DoublePage
    }

    public enum ArchiveLoadStrategy
    {
        /// <summary>
        /// 直接从压缩包条目读取并解码。
        /// </summary>
        Stream = 0,

        /// <summary>
        /// 解压到临时目录并做 LRU 缓存。
        /// </summary>
        TempExtractLru = 1
    }

    public enum BackgroundColor
    {
        Black,
        White,
        Gray,
        DarkGray,
        Transparent,
        TransparentBlur
    }

    public enum ScrollWheelBehavior
    {
        Zoom,
        Navigate
    }

    public partial class AppSettings : ObservableObject
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImageViewer", "settings.json");

        [ObservableProperty]
        private ViewMode _defaultViewMode = ViewMode.Single;

        [ObservableProperty]
        private BackgroundColor _backgroundColor = BackgroundColor.DarkGray;

        [ObservableProperty]
        private ScrollWheelBehavior _scrollWheelBehavior = ScrollWheelBehavior.Zoom;

        [ObservableProperty]
        private int _slideshowInterval = 5;

        [ObservableProperty]
        private bool _slideshowShuffle;

        [ObservableProperty]
        private bool _scanSubfoldersEnabled;

        [ObservableProperty]
        private int _scanSubfoldersDepth = 1;

        [ObservableProperty]
        private bool _rememberWindowPosition = true;

        [ObservableProperty]
        private bool _rememberReadingPosition = true;

        [ObservableProperty]
        private bool _freezeDuringResize = true;

        [ObservableProperty]
        private double _windowLeft;

        [ObservableProperty]
        private double _windowTop;

        [ObservableProperty]
        private double _windowWidth = 1200;

        [ObservableProperty]
        private double _windowHeight = 800;

        [ObservableProperty]
        private bool _isMaximized;

        [ObservableProperty]
        private bool _showToolbar = true;

        [ObservableProperty]
        private bool _showStatusBar = true;

        [ObservableProperty]
        private bool _showSidebar = true;

        [ObservableProperty]
        private double _sidebarWidth = 200;

        [ObservableProperty]
        private int _preloadCount = 3;

        [ObservableProperty]
        private int _thumbnailSize = 120;

        [ObservableProperty]
        private int _mangaDecodeWidth = 1600;

        [ObservableProperty]
        private ArchiveLoadStrategy _archiveLoadStrategy = ArchiveLoadStrategy.Stream;

        [ObservableProperty]
        private bool _filtersEnabled;

        [ObservableProperty]
        private bool _sizeFilterEnabled;

        [ObservableProperty]
        private int _minWidth;

        [ObservableProperty]
        private int _minHeight;

        [ObservableProperty]
        private int _maxWidth;

        [ObservableProperty]
        private int _maxHeight;

        [ObservableProperty]
        private bool _fileSizeFilterEnabled;

        [ObservableProperty]
        private int _minFileSizeKB;

        [ObservableProperty]
        private int _maxFileSizeMB;

        // === 瀑布流/漫画总览布局参数 ===
        [ObservableProperty]
        private double _MangaGap = 4;

  

        [ObservableProperty]
        private string _localSendPath = string.Empty;

        public List<Bookmark> Bookmarks { get; set; } = new();
        public Dictionary<string, int> ReadingPositions { get; set; } = new();
        public List<string> RecentFiles { get; set; } = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }

    public class Bookmark
    {
        /// <summary>
        /// 文件路径或 CacheKey（PDF 页面使用 CacheKey）
        /// </summary>

        public string FilePath { get; set; } = string.Empty;


        /// <summary>
        /// 显示名称
        /// </summary>
        public string Name { get; set; } = string.Empty;


        /// <summary>
        /// 添加时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;


        /// <summary>
        /// PDF 页码（仅 PDF 有效，-1 表示非 PDF）
        /// </summary>
        public int PageIndex { get; set; } = -1;


    }
}
