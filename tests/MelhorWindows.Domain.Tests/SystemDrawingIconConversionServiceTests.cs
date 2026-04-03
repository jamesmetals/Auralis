using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Enums;
using MelhorWindows.Infrastructure.Imaging;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace MelhorWindows.Domain.Tests;

public sealed class SystemDrawingIconConversionServiceTests
{
    [Fact]
    public async Task PrepareIconAsync_AcceptsWebpInput()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var imagePath = Path.Combine(testRoot, "sample-image.webp");
        Directory.CreateDirectory(testRoot);

        try
        {
            using (var image = new SixLabors.ImageSharp.Image<Rgba32>(320, 180, new Rgba32(72, 116, 255)))
            using (var stream = File.Open(imagePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await image.SaveAsync(stream, new WebpEncoder());
            }

            var service = new SystemDrawingIconConversionService();
            var result = await service.PrepareIconAsync(
                new PrepareIconRequest(
                    imagePath,
                    ImageFitMode.CropToSquare,
                    new CropSelection(40, 0, 180, 180),
                    "sample-image"));

            Assert.NotEmpty(result.IconBytes);
            Assert.NotEmpty(result.PreviewPngBytes);
            Assert.Equal("sample-image.webp", result.OriginalFileName);
            Assert.Equal("sample-image", result.SuggestedFileName);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareIconAsync_AcceptsSvgInput()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var imagePath = Path.Combine(testRoot, "sample-image.svg");
        Directory.CreateDirectory(testRoot);

        try
        {
            await File.WriteAllTextAsync(
                imagePath,
                """
                <svg xmlns="http://www.w3.org/2000/svg" width="320" height="180" viewBox="0 0 320 180">
                  <rect width="320" height="180" rx="24" fill="#101A44"/>
                  <circle cx="94" cy="90" r="52" fill="#4A74FF"/>
                  <rect x="156" y="44" width="108" height="92" rx="18" fill="#FFD84C"/>
                </svg>
                """);

            var service = new SystemDrawingIconConversionService();
            var result = await service.PrepareIconAsync(
                new PrepareIconRequest(
                    imagePath,
                    ImageFitMode.CropToSquare,
                    new CropSelection(40, 0, 180, 180),
                    "sample-image"));

            Assert.NotEmpty(result.IconBytes);
            Assert.NotEmpty(result.PreviewPngBytes);
            Assert.Equal("sample-image.svg", result.OriginalFileName);
            Assert.Equal("sample-image", result.SuggestedFileName);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareIconAsync_GeneratesCommonShellResolutions()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var imagePath = Path.Combine(testRoot, "sample-image.png");
        Directory.CreateDirectory(testRoot);

        try
        {
            using (var image = new SixLabors.ImageSharp.Image<Rgba32>(320, 180, new Rgba32(72, 116, 255)))
            using (var stream = File.Open(imagePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await image.SaveAsync(stream, new PngEncoder());
            }

            var service = new SystemDrawingIconConversionService();
            var result = await service.PrepareIconAsync(
                new PrepareIconRequest(
                    imagePath,
                    ImageFitMode.CropToSquare,
                    new CropSelection(40, 0, 180, 180),
                    "sample-image"));

            var sizes = ReadIconFrameSizes(result.IconBytes);
            int[] expectedSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

            Assert.Equal(expectedSizes, sizes.OrderBy(size => size).ToArray());
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static int[] ReadIconFrameSizes(byte[] iconBytes)
    {
        using var stream = new MemoryStream(iconBytes);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());

        var entryCount = reader.ReadUInt16();
        var sizes = new int[entryCount];

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var width = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();

            sizes[entryIndex] = width == 0 ? 256 : width;
        }

        return sizes;
    }
}
