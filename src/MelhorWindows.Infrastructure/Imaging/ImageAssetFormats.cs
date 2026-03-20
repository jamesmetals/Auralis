using System.Runtime.InteropServices;

namespace MelhorWindows.Infrastructure.Imaging;

public static class ImageAssetFormats
{
    public const string UnsupportedImageMessage = "Nao foi possivel ler essa imagem. Use SVG, PNG, JPG, JPEG, BMP, ICO, WEBP, GIF ou TIFF.";
    public const string SupportedImageDialogFilter = "Image Files|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.webp;*.gif;*.tif;*.tiff|All Files|*.*";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg",
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".ico",
        ".webp",
        ".gif",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> ImageSharpExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".webp",
        ".gif",
        ".tif",
        ".tiff"
    };

    public static bool IsSupportedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
    }

    public static bool IsSvgPath(string path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    public static bool UsesImageSharpPipeline(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && ImageSharpExtensions.Contains(extension);
    }

    public static bool IsUnsupportedImageException(Exception exception) =>
        exception is
            NotSupportedException or
            ArgumentException or
            OutOfMemoryException or
            COMException or
            System.Xml.XmlException or
            SixLabors.ImageSharp.UnknownImageFormatException or
            SixLabors.ImageSharp.InvalidImageContentException;
}
