using System;

namespace ImageViewer.Models
{
    public sealed class ImageFilterOptions
    {
        public bool FiltersEnabled { get; init; }
        public bool SizeFilterEnabled { get; init; }
        public int MinWidth { get; init; }
        public int MinHeight { get; init; }
        public int MaxWidth { get; init; }
        public int MaxHeight { get; init; }
        public bool FileSizeFilterEnabled { get; init; }
        public int MinFileSizeKB { get; init; }
        public int MaxFileSizeMB { get; init; }

        public bool HasSizeConstraints =>
            MinWidth > 0 || MinHeight > 0 || MaxWidth > 0 || MaxHeight > 0;

        public bool HasFileSizeConstraints =>
            MinFileSizeKB > 0 || MaxFileSizeMB > 0;

        public bool ShouldCheckFileSize =>
            FiltersEnabled && FileSizeFilterEnabled && HasFileSizeConstraints;

        public bool ShouldCheckDimensions =>
            FiltersEnabled && SizeFilterEnabled && HasSizeConstraints;

        public long MinFileSizeBytes => MinFileSizeKB > 0 ? (long)MinFileSizeKB * 1024L : 0L;

        public long MaxFileSizeBytes =>
            MaxFileSizeMB > 0 ? (long)MaxFileSizeMB * 1024L * 1024L : long.MaxValue;

        public static ImageFilterOptions FromSettings(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new ImageFilterOptions
            {
                FiltersEnabled = settings.FiltersEnabled,
                SizeFilterEnabled = settings.SizeFilterEnabled,
                MinWidth = settings.MinWidth,
                MinHeight = settings.MinHeight,
                MaxWidth = settings.MaxWidth,
                MaxHeight = settings.MaxHeight,
                FileSizeFilterEnabled = settings.FileSizeFilterEnabled,
                MinFileSizeKB = settings.MinFileSizeKB,
                MaxFileSizeMB = settings.MaxFileSizeMB
            };
        }
    }
}
