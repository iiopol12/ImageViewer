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
        private bool _rememberWindowPosition = true;

        [ObservableProperty]
        private bool _rememberReadingPosition = true;

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
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
