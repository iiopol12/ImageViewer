using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Models
{
    public enum ViewMode
    {
        Single,
        Manga,
        DoublePage
    }

    public enum SingleDoubleBarLayout
    {
        Bottom,
        Side
    }

    public enum ArchiveLoadStrategy
    {
        /// <summary>
        /// 直接从压缩包条目读取并解码。
        /// </summary>
        Stream = 0,

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

    public enum MapProvider
    {
        Amap,
        Google
    }

    public enum AppTheme
    {
        Dark,           // 深色主题
        Light,          // 浅色主题
        SoftPink,       // 柔和粉红（浅色背景）
        HotRed,         // 辣红色（深色背景）
        Ocean,          // 海洋蓝（深色背景）
        Forest          // 森林绿（深色背景）
    }


    public enum AppLanguage
    {
        ChineseSimplified,  // 简体中文
        English             // English
    }

    public enum BookmarkType
    {
        Image,
        Folder
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
        private MapProvider _mapProvider = MapProvider.Amap;

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
        private AppTheme _theme = AppTheme.Dark;

        [ObservableProperty]
        private AppLanguage _language = AppLanguage.ChineseSimplified;

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
        private SingleDoubleBarLayout _singleModeBarLayout = SingleDoubleBarLayout.Bottom;

        [ObservableProperty]
        private SingleDoubleBarLayout _doublePageBarLayout = SingleDoubleBarLayout.Bottom;

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

        // === 全屏模式下底边栏/侧边栏自动显示设置 ===
        /// <summary>
        /// 单图/双页模式全屏时，鼠标靠近底部是否自动显示底边缩略图栏
        /// </summary>
        [ObservableProperty]
        private bool _showBottomBarInFullScreen = true;

        /// <summary>
        /// 漫画模式全屏时，鼠标靠近侧边是否自动显示侧边栏
        /// </summary>
        [ObservableProperty]
        private bool _showSidebarInMangaFullScreen = true;

        /// <summary>
        /// 底边栏高度（单图/双页模式）
        /// </summary>
        [ObservableProperty]
        private double _bottomBarHeight = 120;

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
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    if (TryGetLegacySingleDoubleBarLayout(json, out var legacyLayout))
                    {
                        if (settings.SingleModeBarLayout == SingleDoubleBarLayout.Bottom &&
                            settings.DoublePageBarLayout == SingleDoubleBarLayout.Bottom)
                        {
                            settings.SingleModeBarLayout = legacyLayout;
                            settings.DoublePageBarLayout = legacyLayout;
                        }
                    }
                    return settings;
                }
            }
            catch { }
            return new AppSettings();
        }

        private static bool TryGetLegacySingleDoubleBarLayout(string json, out SingleDoubleBarLayout layout)
        {
            layout = SingleDoubleBarLayout.Bottom;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("SingleDoubleBarLayout", out var element))
                {
                    return false;
                }

                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
                {
                    if (Enum.IsDefined(typeof(SingleDoubleBarLayout), value))
                    {
                        layout = (SingleDoubleBarLayout)value;
                        return true;
                    }
                    return false;
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text) &&
                        Enum.TryParse(text, ignoreCase: true, out SingleDoubleBarLayout parsed))
                    {
                        layout = parsed;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
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
        /// 书签类型
        /// </summary>
        public BookmarkType Type { get; set; } = BookmarkType.Image;

        /// <summary>
        /// 收藏文件夹是否展开
        /// </summary>
        public bool IsExpanded { get; set; } = true;

        /// <summary>
        /// 自定义排序键（可用于名称/自定义排序）
        /// </summary>
        public string SortKey { get; set; } = string.Empty;

 
        [JsonIgnore]
        public string Path
        {
            get => FilePath;
            set => FilePath = value;
        }

    
        [JsonIgnore]
        public string DisplayName
        {
            get => Name;
            set => Name = value;
        }

 
        [JsonIgnore]
        public DateTime AddedAt
        {
            get => CreatedAt;
            set => CreatedAt = value;
        }

 
        public int PageIndex { get; set; } = -1;


    }
}
