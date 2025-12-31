namespace ImageViewer.Models
{
    public sealed record ExifMetadata(
        string? DateTaken,
        string? CameraModel,
        string? Aperture,
        string? ShutterSpeed,
        string? Iso,
        string? FocalLength,
        string? GpsLocation);
}
