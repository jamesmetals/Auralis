using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Enums;
using SixLabors.ImageSharp.Formats.Png;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace MelhorWindows.Infrastructure.Imaging;

public sealed class SystemDrawingIconConversionService : IImageIconConversionService
{
    // Common shell sizes used by Explorer across zoom levels and display scales.
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public async Task<PreparedIconAsset> PrepareIconAsync(
        PrepareIconRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(request.SourceImagePath))
        {
            throw new FileNotFoundException("The source image could not be found.", request.SourceImagePath);
        }

        var normalizedSourceBytes = await RunStaAsync(
            () => NormalizeSourceImageToPng(request.SourceImagePath),
            cancellationToken);

        // CPU-bound: execute off the UI thread to keep the WPF interface responsiva.
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sourceStream = new MemoryStream(normalizedSourceBytes);
            using var sourceImage = Image.FromStream(sourceStream, useEmbeddedColorManagement: false, validateImageData: true);
            using var baseBitmap = CreateBaseSquareBitmap(sourceImage, request);

            var previewBytes = EncodePng(baseBitmap);
            var iconBytes = BuildIconFile(baseBitmap, cancellationToken);
            var suggestedFileName = request.OutputBaseName ?? Path.GetFileNameWithoutExtension(request.SourceImagePath);

            return new PreparedIconAsset(
                Path.GetFileName(request.SourceImagePath),
                suggestedFileName,
                iconBytes,
                previewBytes);
        }, cancellationToken);
    }

    private static byte[] NormalizeSourceImageToPng(string sourceImagePath)
    {
        try
        {
            if (ImageAssetFormats.IsSvgPath(sourceImagePath))
            {
                return SvgRasterizer.NormalizeToPng(sourceImagePath);
            }

            return ImageAssetFormats.UsesImageSharpPipeline(sourceImagePath)
                ? NormalizeSourceImageToPngWithImageSharp(sourceImagePath)
                : NormalizeSourceImageToPngWithWpfDecoder(sourceImagePath);
        }
        catch (Exception exception) when (ImageAssetFormats.IsUnsupportedImageException(exception))
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage, exception);
        }
    }

    private static byte[] NormalizeSourceImageToPngWithImageSharp(string sourceImagePath)
    {
        using var stream = File.Open(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var image = ImageSharpImage.Load(stream);
        using var normalizedStream = new MemoryStream();
        image.Save(normalizedStream, new PngEncoder());
        return normalizedStream.ToArray();
    }

    private static byte[] NormalizeSourceImageToPngWithWpfDecoder(string sourceImagePath)
    {
        using var stream = File.Open(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException(ImageAssetFormats.UnsupportedImageMessage);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));

        using var normalizedStream = new MemoryStream();
        encoder.Save(normalizedStream);
        return normalizedStream.ToArray();
    }

    private static Bitmap CreateBaseSquareBitmap(Image sourceImage, PrepareIconRequest request)
    {
        var destination = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(destination);
        graphics.Clear(System.Drawing.Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        switch (request.FitMode)
        {
            case ImageFitMode.CropToSquare:
                var cropArea = ResolveCropArea(sourceImage, request.CropSelection);
                graphics.DrawImage(
                    sourceImage,
                    new Rectangle(0, 0, 256, 256),
                    cropArea,
                    GraphicsUnit.Pixel);
                break;

            case ImageFitMode.FitInsideSquare:
                var scale = Math.Min(256d / sourceImage.Width, 256d / sourceImage.Height);
                var targetWidth = Math.Max(1, (int)Math.Round(sourceImage.Width * scale));
                var targetHeight = Math.Max(1, (int)Math.Round(sourceImage.Height * scale));
                var offsetX = (256 - targetWidth) / 2;
                var offsetY = (256 - targetHeight) / 2;

                graphics.DrawImage(sourceImage, offsetX, offsetY, targetWidth, targetHeight);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request.FitMode), request.FitMode, null);
        }

        return destination;
    }

    private static Rectangle ResolveCropArea(Image sourceImage, CropSelection? cropSelection)
    {
        if (cropSelection is not null)
        {
            var safeWidth = Math.Clamp(cropSelection.Width, 1, sourceImage.Width);
            var safeHeight = Math.Clamp(cropSelection.Height, 1, sourceImage.Height);
            var safeX = Math.Clamp(cropSelection.X, 0, Math.Max(sourceImage.Width - safeWidth, 0));
            var safeY = Math.Clamp(cropSelection.Y, 0, Math.Max(sourceImage.Height - safeHeight, 0));

            return new Rectangle(safeX, safeY, safeWidth, safeHeight);
        }

        var squareSide = Math.Min(sourceImage.Width, sourceImage.Height);
        var offsetX = (sourceImage.Width - squareSide) / 2;
        var offsetY = (sourceImage.Height - squareSide) / 2;

        return new Rectangle(offsetX, offsetY, squareSide, squareSide);
    }

    private static byte[] BuildIconFile(Bitmap baseBitmap, CancellationToken cancellationToken)
    {
        var frames = new List<(int Size, byte[] Bytes)>(IconSizes.Length);

        foreach (var size in IconSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var resized = ResizeBitmap(baseBitmap, size);
            frames.Add((size, EncodePng(resized)));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var imageOffset = 6 + (16 * frames.Count);

        foreach (var frame in frames)
        {
            writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
            writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Bytes.Length);
            writer.Write(imageOffset);
            imageOffset += frame.Bytes.Length;
        }

        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(frame.Bytes);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static Bitmap ResizeBitmap(Bitmap source, int size)
    {
        var resized = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(System.Drawing.Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, size, size);

        return resized;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Task<T> RunStaAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    return;
                }

                completionSource.TrySetResult(action());
            }
            catch (OperationCanceledException)
            {
                completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        });

        thread.IsBackground = true;
        thread.Name = "Auralis.ImageDecode";
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        }

        return completionSource.Task;
    }
}
