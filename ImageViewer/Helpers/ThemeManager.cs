using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;

namespace ImageViewer.Helpers
{
    public static class ThemeManager
    {
        private sealed class ThemePalette
        {
            public ThemePalette(
                Color primary,
                Color primaryDark,
                Color accent,
                Color backgroundDark,
                Color backgroundMedium,
                Color backgroundLight,
                Color foreground,
                Color foregroundDim,
                Color imageBackground,
                Color toolbarBackground,
                Color cardBackground,
                Color headerGradientStart,
                Color headerGradientMiddle,
                Color headerGradientEnd)
            {
                Primary = primary;
                PrimaryDark = primaryDark;
                Accent = accent;
                BackgroundDark = backgroundDark;
                BackgroundMedium = backgroundMedium;
                BackgroundLight = backgroundLight;
                Foreground = foreground;
                ForegroundDim = foregroundDim;
                ImageBackground = imageBackground;
                ToolbarBackground = toolbarBackground;
                CardBackground = cardBackground;
                HeaderGradientStart = headerGradientStart;
                HeaderGradientMiddle = headerGradientMiddle;
                HeaderGradientEnd = headerGradientEnd;
            }

            public Color Primary { get; }
            public Color PrimaryDark { get; }
            public Color Accent { get; }
            public Color BackgroundDark { get; }
            public Color BackgroundMedium { get; }
            public Color BackgroundLight { get; }
            public Color Foreground { get; }
            public Color ForegroundDim { get; }
            public Color ImageBackground { get; }
            public Color ToolbarBackground { get; }
            public Color CardBackground { get; }
            public Color HeaderGradientStart { get; }
            public Color HeaderGradientMiddle { get; }
            public Color HeaderGradientEnd { get; }
        }

        // 深色主题
        private static readonly ThemePalette DarkPalette = new(
            primary: Color.FromRgb(0x34, 0x98, 0xDB),
            primaryDark: Color.FromRgb(0x29, 0x80, 0xB9),
            accent: Color.FromRgb(0xE7, 0x4C, 0x3C),
            backgroundDark: Color.FromRgb(0x1A, 0x1A, 0x1D),
            backgroundMedium: Color.FromRgb(0x2D, 0x2D, 0x30),
            backgroundLight: Color.FromRgb(0x3E, 0x3E, 0x42),
            foreground: Color.FromRgb(0xF5, 0xF5, 0xF5),
            foregroundDim: Color.FromRgb(0x88, 0x88, 0x88),
            imageBackground: Color.FromRgb(0x2D, 0x2D, 0x30),
            toolbarBackground: Color.FromRgb(0x2D, 0x2D, 0x30),
            cardBackground: Color.FromRgb(0x25, 0x25, 0x28),
            headerGradientStart: Color.FromRgb(0x2A, 0x3D, 0x4F),
            headerGradientMiddle: Color.FromRgb(0x1F, 0x2C, 0x3B),
            headerGradientEnd: Color.FromRgb(0x20, 0x25, 0x2F));

        // 浅色主题
        private static readonly ThemePalette LightPalette = new(
            primary: Color.FromRgb(0x00, 0x78, 0xD4),
            primaryDark: Color.FromRgb(0x00, 0x5A, 0x9E),
            accent: Color.FromRgb(0xD1, 0x34, 0x38),
            backgroundDark: Color.FromRgb(0xE8, 0xE8, 0xE8),
            backgroundMedium: Color.FromRgb(0xF5, 0xF5, 0xF5),
            backgroundLight: Color.FromRgb(0xE0, 0xE0, 0xE0),
            foreground: Color.FromRgb(0x1A, 0x1A, 0x1A),
            foregroundDim: Color.FromRgb(0x60, 0x60, 0x60),
            imageBackground: Color.FromRgb(0xF0, 0xF0, 0xF0),
            toolbarBackground: Color.FromRgb(0xE8, 0xE8, 0xE8),
            cardBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            headerGradientStart: Color.FromRgb(0x64, 0x71, 0x76),
            headerGradientMiddle: Color.FromRgb(0x47, 0x52, 0x5e),
            headerGradientEnd: Color.FromRgb(0x5c, 0x60, 0x67));

        // 柔和粉红主题（浅色背景）
        private static readonly ThemePalette SoftPinkPalette = new(
            primary: Color.FromRgb(0xE9, 0x1E, 0x63),      // 粉红主色
            primaryDark: Color.FromRgb(0xC2, 0x18, 0x5B),  // 深粉红
            accent: Color.FromRgb(0xFF, 0x40, 0x81),       // 亮粉红强调色
            backgroundDark: Color.FromRgb(0xFC, 0xE4, 0xEC),   // 浅粉红背景
            backgroundMedium: Color.FromRgb(0xFF, 0xF0, 0xF5), // 更浅的粉白
            backgroundLight: Color.FromRgb(0xF8, 0xBB, 0xD0),  // 粉色高亮
            foreground: Color.FromRgb(0x31, 0x1B, 0x27),       // 深玫红文字
            foregroundDim: Color.FromRgb(0x88, 0x0E, 0x4F),    // 暗粉红次要文字
            imageBackground: Color.FromRgb(0xFF, 0xF5, 0xF8),  // 图片区背景
            toolbarBackground: Color.FromRgb(0xFC, 0xE4, 0xEC),
            cardBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            headerGradientStart: Color.FromRgb(0xEC, 0x40, 0x7A),
            headerGradientMiddle: Color.FromRgb(0xE9, 0x1E, 0x63),
            headerGradientEnd: Color.FromRgb(0xAD, 0x14, 0x57));

        // 辣红色主题（深色背景）
        private static readonly ThemePalette HotRedPalette = new(
            primary: Color.FromRgb(0xF4, 0x43, 0x36),      // 辣红主色
            primaryDark: Color.FromRgb(0xD3, 0x2F, 0x2F),  // 深红
            accent: Color.FromRgb(0xFF, 0x52, 0x52),       // 亮红强调色
            backgroundDark: Color.FromRgb(0x1A, 0x0A, 0x0A),   // 深红黑背景
            backgroundMedium: Color.FromRgb(0x2D, 0x15, 0x15), // 暗红背景
            backgroundLight: Color.FromRgb(0x4A, 0x20, 0x20),  // 红色高亮
            foreground: Color.FromRgb(0xFF, 0xEB, 0xEE),       // 浅粉白文字
            foregroundDim: Color.FromRgb(0xEF, 0x9A, 0x9A),    // 浅红次要文字
            imageBackground: Color.FromRgb(0x2D, 0x15, 0x15),
            toolbarBackground: Color.FromRgb(0x2D, 0x15, 0x15),
            cardBackground: Color.FromRgb(0x22, 0x10, 0x10),
            headerGradientStart: Color.FromRgb(0xB7, 0x1C, 0x1C),
            headerGradientMiddle: Color.FromRgb(0x8B, 0x0A, 0x0A),
            headerGradientEnd: Color.FromRgb(0x5D, 0x00, 0x00));

        // 海洋蓝主题（深色背景）
        private static readonly ThemePalette OceanPalette = new(
            primary: Color.FromRgb(0x00, 0xBC, 0xD4),      // 青色主色
            primaryDark: Color.FromRgb(0x00, 0x97, 0xA7),  // 深青色
            accent: Color.FromRgb(0x00, 0xE5, 0xFF),       // 亮青色强调
            backgroundDark: Color.FromRgb(0x0A, 0x16, 0x1A),   // 深蓝黑
            backgroundMedium: Color.FromRgb(0x12, 0x24, 0x2D), // 深海蓝
            backgroundLight: Color.FromRgb(0x1E, 0x3A, 0x46),  // 蓝色高亮
            foreground: Color.FromRgb(0xE0, 0xF7, 0xFA),       // 浅青白文字
            foregroundDim: Color.FromRgb(0x80, 0xDE, 0xEA),    // 青色次要文字
            imageBackground: Color.FromRgb(0x12, 0x24, 0x2D),
            toolbarBackground: Color.FromRgb(0x12, 0x24, 0x2D),
            cardBackground: Color.FromRgb(0x0D, 0x1B, 0x22),
            headerGradientStart: Color.FromRgb(0x00, 0x69, 0x6B),
            headerGradientMiddle: Color.FromRgb(0x00, 0x4D, 0x61),
            headerGradientEnd: Color.FromRgb(0x00, 0x2A, 0x3A));

        // 森林绿主题（深色背景）
        private static readonly ThemePalette ForestPalette = new(
            primary: Color.FromRgb(0x4C, 0xAF, 0x50),      // 绿色主色
            primaryDark: Color.FromRgb(0x38, 0x8E, 0x3C),  // 深绿色
            accent: Color.FromRgb(0x8B, 0xC3, 0x4A),       // 亮绿强调
            backgroundDark: Color.FromRgb(0x0A, 0x14, 0x0A),   // 深绿黑
            backgroundMedium: Color.FromRgb(0x15, 0x28, 0x15), // 暗绿背景
            backgroundLight: Color.FromRgb(0x25, 0x40, 0x25),  // 绿色高亮
            foreground: Color.FromRgb(0xE8, 0xF5, 0xE9),       // 浅绿白文字
            foregroundDim: Color.FromRgb(0xA5, 0xD6, 0xA7),    // 浅绿次要文字
            imageBackground: Color.FromRgb(0x15, 0x28, 0x15),
            toolbarBackground: Color.FromRgb(0x15, 0x28, 0x15),
            cardBackground: Color.FromRgb(0x10, 0x1E, 0x10),
            headerGradientStart: Color.FromRgb(0x2E, 0x7D, 0x32),
            headerGradientMiddle: Color.FromRgb(0x1B, 0x5E, 0x20),
            headerGradientEnd: Color.FromRgb(0x0D, 0x3D, 0x10));

        private static ThemePalette GetPalette(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Light => LightPalette,
                AppTheme.SoftPink => SoftPinkPalette,
                AppTheme.HotRed => HotRedPalette,
                AppTheme.Ocean => OceanPalette,
                AppTheme.Forest => ForestPalette,
                _ => DarkPalette
            };
        }

        public static void Apply(AppTheme theme)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return;
            }

            var palette = GetPalette(theme);

            // 更新颜色资源
            UpdateColor(resources, "PrimaryColor", palette.Primary);
            UpdateColor(resources, "PrimaryDarkColor", palette.PrimaryDark);
            UpdateColor(resources, "AccentColor", palette.Accent);
            UpdateColor(resources, "BackgroundDarkColor", palette.BackgroundDark);
            UpdateColor(resources, "BackgroundMediumColor", palette.BackgroundMedium);
            UpdateColor(resources, "BackgroundLightColor", palette.BackgroundLight);
            UpdateColor(resources, "ForegroundColor", palette.Foreground);
            UpdateColor(resources, "ForegroundDimColor", palette.ForegroundDim);
            UpdateColor(resources, "ImageBackgroundColor", palette.ImageBackground);
            UpdateColor(resources, "ToolbarBackgroundColor", palette.ToolbarBackground);
            UpdateColor(resources, "CardBackgroundColor", palette.CardBackground);
            UpdateColor(resources, "HeaderGradientStartColor", palette.HeaderGradientStart);
            UpdateColor(resources, "HeaderGradientMiddleColor", palette.HeaderGradientMiddle);
            UpdateColor(resources, "HeaderGradientEndColor", palette.HeaderGradientEnd);

            // 更新画笔资源
            UpdateBrush(resources, "PrimaryBrush", palette.Primary);
            UpdateBrush(resources, "PrimaryDarkBrush", palette.PrimaryDark);
            UpdateBrush(resources, "AccentBrush", palette.Accent);
            UpdateBrush(resources, "BackgroundDarkBrush", palette.BackgroundDark);
            UpdateBrush(resources, "BackgroundMediumBrush", palette.BackgroundMedium);
            UpdateBrush(resources, "BackgroundLightBrush", palette.BackgroundLight);
            UpdateBrush(resources, "ForegroundBrush", palette.Foreground);
            UpdateBrush(resources, "ForegroundDimBrush", palette.ForegroundDim);
            UpdateBrush(resources, "ImageBackgroundBrush", palette.ImageBackground);
            UpdateBrush(resources, "ToolbarBackgroundBrush", palette.ToolbarBackground);
            UpdateBrush(resources, "CardBackgroundBrush", palette.CardBackground);

            // 更新渐变画笔
            UpdateGradientBrush(resources, "HeaderGradientBrush",
                palette.HeaderGradientStart,
                palette.HeaderGradientMiddle,
                palette.HeaderGradientEnd);
        }

        private static void UpdateColor(ResourceDictionary resources, string key, Color color)
        {
            resources[key] = color;
        }

        private static void UpdateBrush(ResourceDictionary resources, string key, Color color)
        {
            if (resources[key] is SolidColorBrush existingBrush)
            {
                if (existingBrush.IsFrozen)
                {
                    resources[key] = new SolidColorBrush(color);
                }
                else
                {
                    existingBrush.Color = color;
                }
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }

        private static void UpdateGradientBrush(ResourceDictionary resources, string key,
            Color startColor, Color middleColor, Color endColor)
        {
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            gradientBrush.GradientStops.Add(new GradientStop(startColor, 0));
            gradientBrush.GradientStops.Add(new GradientStop(middleColor, 0.55));
            gradientBrush.GradientStops.Add(new GradientStop(endColor, 1));

            resources[key] = gradientBrush;
        }
    }
}