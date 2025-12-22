using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        // 填充星星路径
        private const string FilledStarData =
            "M8,0 L9.76,5.17 L15.3,5.64 L10.8,9.02 " +
            "L12.24,14.36 L8,11.4 L3.76,14.36 " +
            "L5.2,9.02 L0.7,5.64 L6.24,5.17 Z";

        // 空心星星路径（简单版本，可以跟填充一样，看你需求）
        private const string EmptyStarData =
            "M8,0 L9.76,5.17 L15.3,5.64 L10.8,9.02 " +
            "L12.24,14.36 L8,11.4 L3.76,14.36 " +
            "L5.2,9.02 L0.7,5.64 L6.24,5.17 Z";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isBookmarked = value is bool b && b;

            string data = isBookmarked ? FilledStarData : EmptyStarData;

            return Geometry.Parse(data);
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

    public class DoubleToThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return new Thickness(d);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Thickness th)
            {
                return th.Left;
            }
            return 0.0;
        }
    }

    public class DoubleToCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return new CornerRadius(d);
            }
            return new CornerRadius(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CornerRadius cr)
            {
                return cr.TopLeft;
            }
            return 0.0;
        }
    }

    public class MangaPlaceholderHeightConverter : IMultiValueConverter
    {
        private const double DefaultAspectRatio = 1.4;
        private const double MinPlaceholderHeight = 120;
        private const double DefaultDecodeWidth = 1600;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
            {
                return 0d;
            }

            if (values[0] != null)
            {
                return 0d;
            }

            double decodeWidth = GetDouble(values[2], DefaultDecodeWidth);
            if (decodeWidth <= 0)
            {
                decodeWidth = DefaultDecodeWidth;
            }

            if (values[1] is BitmapSource thumb && thumb.PixelWidth > 0 && thumb.PixelHeight > 0)
            {
                double ratio = (double)thumb.PixelHeight / thumb.PixelWidth;
                return Math.Max(MinPlaceholderHeight, decodeWidth * ratio);
            }

            return Math.Max(MinPlaceholderHeight, decodeWidth * DefaultAspectRatio);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static double GetDouble(object value, double fallback)
        {
            if (value is double d)
            {
                return d;
            }

            if (value is int i)
            {
                return i;
            }

            if (value != null && double.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }

    public class MangaImageSourceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is BitmapSource full)
            {
                return full;
            }

            if (values.Length > 1 && values[1] is BitmapSource thumb)
            {
                return thumb;
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 图片视图可见性转换器
    /// 用于在瀑布流显示时隐藏单图/双页/漫画视图
    /// </summary>
    public class ImageViewVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
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
            else
            {
                return isMangaMode ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }



    /// <summary>
    /// </summary>
    public class GifVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return Visibility.Collapsed;

            var extension = values[0] as string;
            bool isGif = string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase);

            bool isAnimated = false;
            if (values[1] is int frameCount)
                isAnimated = frameCount > 1;
            else if (values[1] is bool b)
                isAnimated = b;

            bool showGif = parameter?.ToString() == "Gif";

            if (showGif)
            {
                return isGif && isAnimated ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                return isGif && isAnimated ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// </summary>
    public class SpeedToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double speed)
            {
                return $"{speed:F1}x";
            }
            return "1.0x";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// </summary>
    public class FrameProgressConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return "0/0";

            int current = values[0] is int c ? c + 1 : 0;
            int total = values[1] is int t ? t : 0;

            return $"{current}/{total}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


