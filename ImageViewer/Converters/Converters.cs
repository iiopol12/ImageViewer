using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ImageViewer.Models;

namespace ImageViewer.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;
            bool invert = parameter?.ToString() == "Invert";

            if (invert) boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visibility = value is Visibility v && v == Visibility.Visible;
            bool invert = parameter?.ToString() == "Invert";

            return invert ? !visibility : visibility;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            bool invert = parameter?.ToString() == "Invert";

            if (invert) isNull = !isNull;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BackgroundColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BackgroundColor color)
            {
                return color switch
                {
                    BackgroundColor.Black => new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                    BackgroundColor.White => new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    BackgroundColor.Gray => new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    BackgroundColor.DarkGray => new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    BackgroundColor.Transparent => new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    BackgroundColor.TransparentBlur => new SolidColorBrush(Color.FromArgb(140, 20, 20, 20)),
                    _ => new SolidColorBrush(Color.FromRgb(45, 45, 48))
                };
            }
            return new SolidColorBrush(Color.FromRgb(45, 45, 48));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ZoomToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double zoom)
            {
                return $"{zoom * 100:F0}%";
            }
            return "100%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ViewModeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ViewMode mode)
            {
                return mode switch
                {
                    ViewMode.Single => "单图模式",
                    ViewMode.Manga => "漫画模式",
                    ViewMode.DoublePage => "双页模式",

                    _ => "单图模式"
                };
            }
            return "单图模式";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToBookmarkIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isBookmarked = value is bool b && b;
            return isBookmarked ? "★" : "☆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MultiBoolToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var value in values)
            {
                if (value is bool b && !b)
                    return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ToolbarVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 默认显示工具栏，如果设置值缺失则视为 true，并在全屏时隐藏
            bool showToolbar = values.Length > 0 ? values[0] as bool? ?? true : true;
            bool isFullScreen = values.Length > 1 && values[1] is bool full && full;

            if (isFullScreen)
            {
                return Visibility.Collapsed;
            }

            return showToolbar ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return new GridLength(d);
            }
            return new GridLength(200);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gl)
            {
                return gl.Value;
            }
            return 200.0;
        }
    }

    /// <summary>
    /// 图片视图可见性转换器
    /// 用于在瀑布流显示时隐藏单图/双页/漫画视图
    /// 参数: "Single" - 单图/双页模式, "Manga" - 漫画模式
    /// </summary>
    public class ImageViewVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] = IsMangaMode
            // values[1] = ShowWaterfallView
            if (values.Length < 2)
                return Visibility.Collapsed;

            bool isMangaMode = values[0] is bool manga && manga;
            bool showWaterfall = values[1] is bool waterfall && waterfall;
            string mode = parameter?.ToString() ?? "Single";

            // 如果瀑布流显示，则隐藏所有其他视图
            if (showWaterfall)
                return Visibility.Collapsed;

            // 否则根据当前模式显示对应视图
            if (mode == "Manga")
            {
                return isMangaMode ? Visibility.Visible : Visibility.Collapsed;
            }
            else // Single/DoublePage
            {
                return isMangaMode ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}