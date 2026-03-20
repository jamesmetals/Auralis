using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace MelhorWindows.Infrastructure.Imaging;

public static class SvgRasterizer
{
    public static (int Width, int Height) ReadImageInfo(string imagePath)
    {
        var drawing = LoadDrawing(imagePath);
        var bounds = ResolveBounds(drawing);
        return ((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
    }

    public static byte[] NormalizeToPng(string imagePath)
    {
        var drawing = LoadDrawing(imagePath);
        var bounds = ResolveBounds(drawing);
        var maxDimension = Math.Max(bounds.Width, bounds.Height);
        var targetMaxDimension = Math.Clamp((int)Math.Ceiling(maxDimension), 512, 1024);
        var scale = targetMaxDimension / maxDimension;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        return RenderToPngBytes(drawing, bounds, pixelWidth, pixelHeight);
    }

    public static byte[] RenderPreviewToPng(string imagePath, int maxPixelSize)
    {
        var drawing = LoadDrawing(imagePath);
        var bounds = ResolveBounds(drawing);
        var (pixelWidth, pixelHeight) = ResolveRenderSize(bounds, maxPixelSize);
        return RenderToPngBytes(drawing, bounds, pixelWidth, pixelHeight);
    }

    private static DrawingGroup LoadDrawing(string imagePath)
    {
        var settings = new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = false,
            EnsureViewboxPosition = true,
            EnsureViewboxSize = true
        };

        using var reader = new FileSvgReader(settings);
        var drawing = reader.Read(imagePath);

        if (drawing is null)
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage);
        }

        if (drawing.CanFreeze)
        {
            drawing.Freeze();
        }

        return drawing;
    }

    private static Rect ResolveBounds(DrawingGroup drawing)
    {
        var bounds = drawing.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new Rect(0, 0, 256, 256);
        }

        return bounds;
    }

    private static (int PixelWidth, int PixelHeight) ResolveRenderSize(Rect bounds, int maxPixelSize)
    {
        var width = Math.Max(1d, bounds.Width);
        var height = Math.Max(1d, bounds.Height);

        if (maxPixelSize > 0)
        {
            var scale = Math.Min(maxPixelSize / width, maxPixelSize / height);

            if (scale > 0)
            {
                width *= scale;
                height *= scale;
            }
        }

        return ((int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    private static byte[] RenderToPngBytes(DrawingGroup drawing, Rect bounds, int pixelWidth, int pixelHeight)
    {
        var brush = new DrawingBrush(drawing)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.Uniform,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds
        };

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, pixelWidth, pixelHeight));
            context.DrawRectangle(brush, null, new Rect(0, 0, pixelWidth, pixelHeight));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
