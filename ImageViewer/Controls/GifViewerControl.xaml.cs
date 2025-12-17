using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    /// <summary>
    /// GIF 循环模式
    /// </summary>
    public enum GifLoopMode
    {
        /// <summary>无限循环</summary>
        Infinite,
        /// <summary>播放指定次数</summary>
        Count,
        /// <summary>播放一次后停止</summary>
        Once
    }

    /// <summary>
    /// GIF 动画播放控件
    /// 支持从 Stream/byte[] 加载，支持播放控制、速度调节、循环模式
    /// </summary>
    public class GifViewerControl : Control
    {
        #region 私有字段

        private Image? _image;
        private DispatcherTimer? _timer;
        private BitmapFrame[]? _frames;
        private int[]? _frameDelays; // 每帧延时（毫秒）
        private int _loopCompletedCount;

        // 最小帧延时（毫秒），防止0延时导致CPU过载
        private const int MinFrameDelay = 20;
        // 默认帧延时（毫秒），当元数据缺失时使用
        private const int DefaultFrameDelay = 100;

        #endregion

        #region 依赖属性

        /// <summary>
        /// GIF 数据源（byte[] 或 Stream）
        /// </summary>
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(
                nameof(Source),
                typeof(object),
                typeof(GifViewerControl),
                new PropertyMetadata(null, OnSourceChanged));

        public object? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(
                nameof(IsPlaying),
                typeof(bool),
                typeof(GifViewerControl),
                new PropertyMetadata(true, OnIsPlayingChanged));

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        /// <summary>
        /// 播放速度系数（1.0 = 正常速度）
        /// </summary>
        public static readonly DependencyProperty SpeedProperty =
            DependencyProperty.Register(
                nameof(Speed),
                typeof(double),
                typeof(GifViewerControl),
                new PropertyMetadata(1.0, OnSpeedChanged, CoerceSpeed));

        public double Speed
        {
            get => (double)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }

        /// <summary>
        /// 循环模式
        /// </summary>
        public static readonly DependencyProperty LoopModeProperty =
            DependencyProperty.Register(
                nameof(LoopMode),
                typeof(GifLoopMode),
                typeof(GifViewerControl),
                new PropertyMetadata(GifLoopMode.Infinite));

        public GifLoopMode LoopMode
        {
            get => (GifLoopMode)GetValue(LoopModeProperty);
            set => SetValue(LoopModeProperty, value);
        }

        /// <summary>
        /// 循环次数（仅 LoopMode = Count 时有效）
        /// </summary>
        public static readonly DependencyProperty LoopCountProperty =
            DependencyProperty.Register(
                nameof(LoopCount),
                typeof(int),
                typeof(GifViewerControl),
                new PropertyMetadata(1, null, CoerceLoopCount));

        public int LoopCount
        {
            get => (int)GetValue(LoopCountProperty);
            set => SetValue(LoopCountProperty, value);
        }

        /// <summary>
        /// 当前帧索引
        /// </summary>
        public static readonly DependencyProperty CurrentFrameIndexProperty =
            DependencyProperty.Register(
                nameof(CurrentFrameIndex),
                typeof(int),
                typeof(GifViewerControl),
                new PropertyMetadata(0, OnCurrentFrameIndexChanged));

        public int CurrentFrameIndex
        {
            get => (int)GetValue(CurrentFrameIndexProperty);
            set => SetValue(CurrentFrameIndexProperty, value);
        }

        /// <summary>
        /// 总帧数（只读）
        /// </summary>
        public static readonly DependencyPropertyKey FrameCountPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(FrameCount),
                typeof(int),
                typeof(GifViewerControl),
                new PropertyMetadata(0));

        public static readonly DependencyProperty FrameCountProperty = FrameCountPropertyKey.DependencyProperty;

        public int FrameCount
        {
            get => (int)GetValue(FrameCountProperty);
            private set => SetValue(FrameCountPropertyKey, value);
        }

        /// <summary>
        /// 是否为动画 GIF（帧数 > 1）
        /// </summary>
        public static readonly DependencyPropertyKey IsAnimatedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsAnimated),
                typeof(bool),
                typeof(GifViewerControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsAnimatedProperty = IsAnimatedPropertyKey.DependencyProperty;

        public bool IsAnimated
        {
            get => (bool)GetValue(IsAnimatedProperty);
            private set => SetValue(IsAnimatedPropertyKey, value);
        }

        #endregion

        #region 路由事件

        /// <summary>
        /// 播放完成事件（非无限循环模式）
        /// </summary>
        public static readonly RoutedEvent PlaybackCompletedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(PlaybackCompleted),
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(GifViewerControl));

        public event RoutedEventHandler PlaybackCompleted
        {
            add => AddHandler(PlaybackCompletedEvent, value);
            remove => RemoveHandler(PlaybackCompletedEvent, value);
        }

        #endregion

        #region 构造函数

        static GifViewerControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(GifViewerControl),
                new FrameworkPropertyMetadata(typeof(GifViewerControl)));
        }

        public GifViewerControl()
        {
            Unloaded += OnUnloaded;
        }

        #endregion

        #region 重写方法

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _image = GetTemplateChild("PART_Image") as Image;

            // 如果已有帧数据，显示当前帧
            if (_frames != null && _frames.Length > 0 && _image != null)
            {
                DisplayCurrentFrame();
            }
        }

        #endregion

        #region 属性变更处理

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GifViewerControl control)
            {
                control.LoadGif(e.NewValue);
            }
        }

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GifViewerControl control)
            {
                if ((bool)e.NewValue)
                    control.StartPlayback();
                else
                    control.StopPlayback();
            }
        }

        private static void OnSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GifViewerControl control)
            {
                control.UpdateTimerInterval();
            }
        }

        private static object CoerceSpeed(DependencyObject d, object baseValue)
        {
            var speed = (double)baseValue;
            return Math.Max(0.1, Math.Min(10.0, speed)); // 限制在 0.1x ~ 10x
        }

        private static object CoerceLoopCount(DependencyObject d, object baseValue)
        {
            var count = (int)baseValue;
            return Math.Max(1, count);
        }

        private static void OnCurrentFrameIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GifViewerControl control)
            {
                control.DisplayCurrentFrame();
                control.UpdateTimerInterval();
            }
        }

        #endregion

        #region 核心方法

        /// <summary>
        /// 加载 GIF 数据
        /// </summary>
        private void LoadGif(object? source)
        {
            // 清理旧资源
            Cleanup();

            if (source == null) return;

            try
            {
                Stream? stream = null;
                bool disposeStream = false;

                if (source is byte[] bytes)
                {
                    stream = new MemoryStream(bytes, writable: false);
                    disposeStream = true;
                }
                else if (source is Stream s)
                {
                    stream = s;
                    // 确保流可定位
                    if (!stream.CanSeek)
                    {
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        stream = ms;
                        disposeStream = true;
                    }
                }
                else if (source is string filePath && File.Exists(filePath))
                {
                    stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    disposeStream = true;
                }

                if (stream == null) return;

                try
                {
                    DecodeGif(stream);
                }
                finally
                {
                    if (disposeStream)
                        stream.Dispose();
                }

                // 更新属性
                FrameCount = _frames?.Length ?? 0;
                IsAnimated = FrameCount > 1;
                CurrentFrameIndex = 0;

                // 开始播放
                if (IsAnimated && IsPlaying)
                {
                    StartPlayback();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GIF 加载失败: {ex.Message}");
                Cleanup();
            }
        }

        /// <summary>
        /// 解码 GIF 获取所有帧和延时
        /// </summary>
        private void DecodeGif(Stream stream)
        {
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frameCount = decoder.Frames.Count;
            if (frameCount <= 0)
            {
                _frames = null;
                _frameDelays = null;
                return;
            }

            _frames = new BitmapFrame[frameCount];
            _frameDelays = new int[frameCount];

            var (canvasWidth, canvasHeight) = GetGifCanvasSize(decoder);
            canvasWidth = Math.Max(1, canvasWidth);
            canvasHeight = Math.Max(1, canvasHeight);

            var canvasStride = canvasWidth * 4; // Pbgra32
            var canvas = new byte[canvasStride * canvasHeight];
            byte[]? restoreBuffer = null;

            // GIF 通常没有可靠 DPI 信息；固定为 96 可避免 WPF 因 DPI 差异做隐式缩放导致发糊。
            const double dpiX = 96.0;
            const double dpiY = 96.0;

            var (backgroundIndex, backgroundB, backgroundG, backgroundR, backgroundA) = GetGifBackground(decoder);

            for (int i = 0; i < frameCount; i++)
            {
                var frame = decoder.Frames[i];
                var metadata = frame.Metadata as BitmapMetadata;

                var delay = GetFrameDelayMs(metadata);
                _frameDelays[i] = Math.Max(delay, MinFrameDelay);

                var disposal = GetMetadataInt(metadata, "/grctlext/Disposal", 0);
                var left = GetMetadataInt(metadata, "/imgdesc/Left", 0);
                var top = GetMetadataInt(metadata, "/imgdesc/Top", 0);

                var frameSource = EnsurePbgra32(frame);
                var frameWidth = frameSource.PixelWidth;
                var frameHeight = frameSource.PixelHeight;
                var frameStride = frameWidth * 4;
                var framePixels = new byte[frameStride * frameHeight];
                frameSource.CopyPixels(framePixels, frameStride, 0);

                if (disposal == 3)
                {
                    restoreBuffer ??= new byte[canvas.Length];
                    Buffer.BlockCopy(canvas, 0, restoreBuffer, 0, canvas.Length);
                }

                BlendOntoCanvas(
                    canvas,
                    canvasWidth,
                    canvasHeight,
                    canvasStride,
                    framePixels,
                    frameWidth,
                    frameHeight,
                    frameStride,
                    left,
                    top);

                var snapshot = new byte[canvas.Length];
                Buffer.BlockCopy(canvas, 0, snapshot, 0, canvas.Length);

                var bitmap = BitmapSource.Create(
                    canvasWidth,
                    canvasHeight,
                    dpiX,
                    dpiY,
                    PixelFormats.Pbgra32,
                    null,
                    snapshot,
                    canvasStride);
                bitmap.Freeze();
                _frames[i] = BitmapFrame.Create(bitmap);

                if (disposal == 2)
                {
                    var fillB = backgroundB;
                    var fillG = backgroundG;
                    var fillR = backgroundR;
                    var fillA = backgroundA;

                    if (backgroundIndex >= 0 && GetMetadataBool(metadata, "/grctlext/TransparencyFlag", false))
                    {
                        var transparentIndex = GetMetadataInt(metadata, "/grctlext/TransparentColorIndex", -1);
                        if (transparentIndex == backgroundIndex)
                        {
                            fillB = 0;
                            fillG = 0;
                            fillR = 0;
                            fillA = 0;
                        }
                    }

                    ClearRect(canvas, canvasWidth, canvasHeight, canvasStride, left, top, frameWidth, frameHeight, fillB, fillG, fillR, fillA);
                }
                else if (disposal == 3 && restoreBuffer != null)
                {
                    Buffer.BlockCopy(restoreBuffer, 0, canvas, 0, canvas.Length);
                }
            }
        }

        private static double GetSafeDpi(double dpi)
        {
            if (!double.IsFinite(dpi) || dpi <= 0)
            {
                return 96.0;
            }

            return dpi;
        }

        private static (int width, int height) GetGifCanvasSize(GifBitmapDecoder decoder)
        {
            var metadata = decoder.Metadata as BitmapMetadata;
            var width = GetMetadataInt(metadata, "/logscrdesc/Width", 0);
            var height = GetMetadataInt(metadata, "/logscrdesc/Height", 0);

            if (width > 0 && height > 0)
            {
                return (width, height);
            }

            var bestWidth = 0;
            var bestHeight = 0;

            foreach (var frame in decoder.Frames)
            {
                var frameMetadata = frame.Metadata as BitmapMetadata;
                var left = GetMetadataInt(frameMetadata, "/imgdesc/Left", 0);
                var top = GetMetadataInt(frameMetadata, "/imgdesc/Top", 0);

                bestWidth = Math.Max(bestWidth, left + frame.PixelWidth);
                bestHeight = Math.Max(bestHeight, top + frame.PixelHeight);
            }

            if (bestWidth > 0 && bestHeight > 0)
            {
                return (bestWidth, bestHeight);
            }

            if (decoder.Frames.Count > 0)
            {
                return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
            }

            return (1, 1);
        }

        private static int GetFrameDelayMs(BitmapMetadata? metadata)
        {
            var delay = DefaultFrameDelay;

            try
            {
                if (metadata == null)
                {
                    return delay;
                }

                var delayObj = metadata.GetQuery("/grctlext/Delay");
                if (delayObj is ushort d)
                {
                    delay = d * 10;
                }
            }
            catch
            {
                // ignore
            }

            return delay;
        }

        private static int GetMetadataInt(BitmapMetadata? metadata, string query, int defaultValue)
        {
            try
            {
                if (metadata == null)
                {
                    return defaultValue;
                }

                var value = metadata.GetQuery(query);
                return value switch
                {
                    byte b => b,
                    sbyte sb => sb,
                    short s => s,
                    ushort us => us,
                    int i => i,
                    uint ui => ui > int.MaxValue ? defaultValue : (int)ui,
                    _ => defaultValue
                };
            }
            catch
            {
                return defaultValue;
            }
        }

        private static bool GetMetadataBool(BitmapMetadata? metadata, string query, bool defaultValue)
        {
            try
            {
                if (metadata == null)
                {
                    return defaultValue;
                }

                var value = metadata.GetQuery(query);
                return value switch
                {
                    bool b => b,
                    byte b => b != 0,
                    sbyte sb => sb != 0,
                    short s => s != 0,
                    ushort us => us != 0,
                    int i => i != 0,
                    uint ui => ui != 0,
                    _ => defaultValue
                };
            }
            catch
            {
                return defaultValue;
            }
        }

        private static (int index, byte b, byte g, byte r, byte a) GetGifBackground(GifBitmapDecoder decoder)
        {
            try
            {
                var metadata = decoder.Metadata as BitmapMetadata;
                var backgroundIndex = GetMetadataInt(metadata, "/logscrdesc/BackgroundColorIndex", -1);
                if (backgroundIndex < 0)
                {
                    backgroundIndex = GetMetadataInt(metadata, "/logscrdesc/BgColorIndex", -1);
                }

                if (backgroundIndex < 0 || decoder.Frames.Count == 0)
                {
                    return (-1, 0, 0, 0, 0);
                }

                var palette = decoder.Frames[0].Palette;
                if (palette == null || backgroundIndex >= palette.Colors.Count)
                {
                    return (backgroundIndex, 0, 0, 0, 0);
                }

                var color = palette.Colors[backgroundIndex];
                return (backgroundIndex, color.B, color.G, color.R, 255);
            }
            catch
            {
                return (-1, 0, 0, 0, 0);
            }
        }

        private static BitmapSource EnsurePbgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Pbgra32)
            {
                return source;
            }

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Pbgra32;
            converted.EndInit();
            converted.Freeze();
            return converted;
        }

        private static void BlendOntoCanvas(
            byte[] canvas,
            int canvasWidth,
            int canvasHeight,
            int canvasStride,
            byte[] framePixels,
            int frameWidth,
            int frameHeight,
            int frameStride,
            int left,
            int top)
        {
            var destX0 = Math.Max(0, left);
            var destY0 = Math.Max(0, top);
            var srcX0 = destX0 - left;
            var srcY0 = destY0 - top;

            var copyWidth = Math.Min(frameWidth - srcX0, canvasWidth - destX0);
            var copyHeight = Math.Min(frameHeight - srcY0, canvasHeight - destY0);
            if (copyWidth <= 0 || copyHeight <= 0)
            {
                return;
            }

            for (var y = 0; y < copyHeight; y++)
            {
                var srcRow = (srcY0 + y) * frameStride + srcX0 * 4;
                var destRow = (destY0 + y) * canvasStride + destX0 * 4;

                for (var x = 0; x < copyWidth; x++)
                {
                    var srcIndex = srcRow + x * 4;
                    var srcA = framePixels[srcIndex + 3];
                    if (srcA == 0)
                    {
                        continue;
                    }

                    var destIndex = destRow + x * 4;

                    if (srcA == 255)
                    {
                        canvas[destIndex] = framePixels[srcIndex];
                        canvas[destIndex + 1] = framePixels[srcIndex + 1];
                        canvas[destIndex + 2] = framePixels[srcIndex + 2];
                        canvas[destIndex + 3] = 255;
                        continue;
                    }

                    var invA = 255 - srcA;

                    canvas[destIndex] = (byte)(framePixels[srcIndex] + ((canvas[destIndex] * invA) + 127) / 255);
                    canvas[destIndex + 1] = (byte)(framePixels[srcIndex + 1] + ((canvas[destIndex + 1] * invA) + 127) / 255);
                    canvas[destIndex + 2] = (byte)(framePixels[srcIndex + 2] + ((canvas[destIndex + 2] * invA) + 127) / 255);
                    canvas[destIndex + 3] = (byte)(srcA + ((canvas[destIndex + 3] * invA) + 127) / 255);
                }
            }
        }

        private static void ClearRect(
            byte[] canvas,
            int canvasWidth,
            int canvasHeight,
            int canvasStride,
            int left,
            int top,
            int width,
            int height,
            byte fillB,
            byte fillG,
            byte fillR,
            byte fillA)
        {
            var destX0 = Math.Max(0, left);
            var destY0 = Math.Max(0, top);
            var srcX0 = destX0 - left;
            var srcY0 = destY0 - top;

            var clearWidth = Math.Min(width - srcX0, canvasWidth - destX0);
            var clearHeight = Math.Min(height - srcY0, canvasHeight - destY0);
            if (clearWidth <= 0 || clearHeight <= 0)
            {
                return;
            }

            var clearBytesPerRow = clearWidth * 4;
            for (var y = 0; y < clearHeight; y++)
            {
                var destRow = (destY0 + y) * canvasStride + destX0 * 4;

                if (fillA == 0 && fillB == 0 && fillG == 0 && fillR == 0)
                {
                    Array.Clear(canvas, destRow, clearBytesPerRow);
                    continue;
                }

                for (var x = 0; x < clearWidth; x++)
                {
                    var destIndex = destRow + x * 4;
                    canvas[destIndex] = fillB;
                    canvas[destIndex + 1] = fillG;
                    canvas[destIndex + 2] = fillR;
                    canvas[destIndex + 3] = fillA;
                }
            }
        }

        /// <summary>
        /// 显示当前帧
        /// </summary>
        private void DisplayCurrentFrame()
        {
            if (_image == null || _frames == null || _frames.Length == 0)
                return;

            var index = Math.Max(0, Math.Min(CurrentFrameIndex, _frames.Length - 1));
            _image.Source = _frames[index];
        }

        /// <summary>
        /// 开始播放
        /// </summary>
        private void StartPlayback()
        {
            if (_frames == null || _frames.Length <= 1)
                return;

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render);
                _timer.Tick += OnTimerTick;
            }

            _loopCompletedCount = 0;
            UpdateTimerInterval();
            _timer.Start();
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        private void StopPlayback()
        {
            _timer?.Stop();
        }

        /// <summary>
        /// 更新定时器间隔
        /// </summary>
        private void UpdateTimerInterval()
        {
            if (_timer == null || _frameDelays == null || _frameDelays.Length == 0)
                return;

            var index = Math.Max(0, Math.Min(CurrentFrameIndex, _frameDelays.Length - 1));
            var delay = _frameDelays[index];

            // 应用速度系数
            var adjustedDelay = (int)(delay / Speed);
            adjustedDelay = Math.Max(adjustedDelay, MinFrameDelay);

            _timer.Interval = TimeSpan.FromMilliseconds(adjustedDelay);
        }

        /// <summary>
        /// 定时器触发 - 切换下一帧
        /// </summary>
        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_frames == null || _frames.Length <= 1)
                return;

            var nextIndex = CurrentFrameIndex + 1;

            // 检查是否完成一轮
            if (nextIndex >= _frames.Length)
            {
                _loopCompletedCount++;

                // 检查循环模式
                switch (LoopMode)
                {
                    case GifLoopMode.Once:
                        StopPlayback();
                        IsPlaying = false;
                        RaiseEvent(new RoutedEventArgs(PlaybackCompletedEvent));
                        return;

                    case GifLoopMode.Count:
                        if (_loopCompletedCount >= LoopCount)
                        {
                            StopPlayback();
                            IsPlaying = false;
                            RaiseEvent(new RoutedEventArgs(PlaybackCompletedEvent));
                            return;
                        }
                        break;
                }

                nextIndex = 0;
            }

            CurrentFrameIndex = nextIndex;
        }

        /// <summary>
        /// 跳转到指定帧
        /// </summary>
        public void SeekToFrame(int frameIndex)
        {
            if (_frames == null || _frames.Length == 0)
                return;

            CurrentFrameIndex = Math.Max(0, Math.Min(frameIndex, _frames.Length - 1));
        }

        /// <summary>
        /// 切换播放/暂停
        /// </summary>
        public void TogglePlayPause()
        {
            IsPlaying = !IsPlaying;
        }

        /// <summary>
        /// 获取当前帧的静态图像
        /// </summary>
        public BitmapSource? GetCurrentFrame()
        {
            if (_frames == null || _frames.Length == 0)
                return null;

            var index = Math.Max(0, Math.Min(CurrentFrameIndex, _frames.Length - 1));
            return _frames[index];
        }

        #endregion

        #region 清理

        private void Cleanup()
        {
            _timer?.Stop();
            _frames = null;
            _frameDelays = null;
            FrameCount = 0;
            IsAnimated = false;
            _loopCompletedCount = 0;

            if (_image != null)
                _image.Source = null;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Cleanup();
            if (_timer != null)
            {
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }

        #endregion
    }
}
