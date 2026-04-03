using System.Drawing;
using System.Text;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Application.Services;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;
using MelhorWindows.Domain.Enums;
using MelhorWindows.Infrastructure.Imaging;
using MelhorWindows.Infrastructure.Storage;
using MelhorWindows.WindowsIntegration.Explorer;
using Xunit;

namespace MelhorWindows.Domain.Tests;

public sealed class FolderIconWorkflowIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_CopiesIconIntoFolderAndWritesDesktopIni()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var folderPath = Path.Combine(testRoot, "TargetFolder");
        var imagePath = Path.Combine(testRoot, "sample-image.png");
        var appDataRoot = Path.Combine(testRoot, "AppData");

        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(testRoot);
        CreateSampleImage(imagePath);

        try
        {
            var userContext = new FakeUserContext(BuiltInRoles.User);
            var authorizationService = new AuthorizationService(userContext);
            var workflow = new FolderIconWorkflowService(
                authorizationService,
                userContext,
                new SystemDrawingIconConversionService(),
                new FileSystemIconStorageService(new AppDataPaths(appDataRoot)),
                new DesktopIniFolderIconIntegrationService(),
                new JsonIconHistoryRepository(new AppDataPaths(appDataRoot)));

            var result = await workflow.ExecuteAsync(
                new ApplyFolderIconRequest(
                    folderPath,
                    imagePath,
                    ImageFitMode.CropToSquare,
                    new CropSelection(40, 0, 180, 180)));

            var desktopIniPath = Path.Combine(folderPath, "desktop.ini");
            var folderIconPath = Directory.GetFiles(folderPath, "auralis-folder-icon-*.ico").Single();

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(desktopIniPath));
            Assert.True(File.Exists(folderIconPath));
            var desktopIniText = await File.ReadAllTextAsync(desktopIniPath);
            Assert.Contains($"IconResource=.\\{Path.GetFileName(folderIconPath)},0", desktopIniText);
            Assert.True(new FileInfo(folderIconPath).Length > 0);

            var folderAttributes = File.GetAttributes(folderPath);
            Assert.True(folderAttributes.HasFlag(FileAttributes.ReadOnly));
            Assert.True(folderAttributes.HasFlag(FileAttributes.System));

            var desktopIniAttributes = File.GetAttributes(desktopIniPath);
            Assert.True(desktopIniAttributes.HasFlag(FileAttributes.Hidden));
            Assert.True(desktopIniAttributes.HasFlag(FileAttributes.System));

            var folderIconAttributes = File.GetAttributes(folderIconPath);
            Assert.True(folderIconAttributes.HasFlag(FileAttributes.Hidden));
            Assert.True(folderIconAttributes.HasFlag(FileAttributes.System));
            Assert.DoesNotContain(
                Directory.GetFiles(folderPath, "desktop*.ini"),
                path => !string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase));

            var copiedRoot = Path.Combine(testRoot, "CopiedFolder");
            CopyDirectory(folderPath, copiedRoot);
            var copiedIconPath = Directory.GetFiles(copiedRoot, "auralis-folder-icon-*.ico").Single();
            Assert.True(File.Exists(copiedIconPath));
            Assert.Contains(
                $"IconResource=.\\{Path.GetFileName(copiedIconPath)},0",
                await File.ReadAllTextAsync(Path.Combine(copiedRoot, "desktop.ini")));
        }
        finally
        {
            ResetAttributesRecursively(testRoot);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RewritesExistingProtectedDesktopIni()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var folderPath = Path.Combine(testRoot, "TargetFolder");
        var imagePath = Path.Combine(testRoot, "sample-image.png");
        var appDataRoot = Path.Combine(testRoot, "AppData");

        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(testRoot);
        CreateSampleImage(imagePath);

        var existingDesktopIniPath = Path.Combine(folderPath, "desktop.ini");
        var existingIconPath = Path.Combine(folderPath, "melhorwindows-folder-icon.ico");
        await File.WriteAllTextAsync(existingDesktopIniPath, "[.ShellClassInfo]\r\nIconFile=old.ico");
        await File.WriteAllBytesAsync(existingIconPath, [1, 2, 3, 4]);
        File.SetAttributes(existingDesktopIniPath, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(existingIconPath, FileAttributes.Hidden | FileAttributes.System);

        try
        {
            var userContext = new FakeUserContext(BuiltInRoles.User);
            var authorizationService = new AuthorizationService(userContext);
            var workflow = new FolderIconWorkflowService(
                authorizationService,
                userContext,
                new SystemDrawingIconConversionService(),
                new FileSystemIconStorageService(new AppDataPaths(appDataRoot)),
                new DesktopIniFolderIconIntegrationService(),
                new JsonIconHistoryRepository(new AppDataPaths(appDataRoot)));

            var result = await workflow.ExecuteAsync(
                new ApplyFolderIconRequest(
                    folderPath,
                    imagePath,
                    ImageFitMode.CropToSquare,
                    new CropSelection(40, 0, 180, 180)));

            Assert.True(result.Succeeded);
            var rewrittenIconPath = Directory.GetFiles(folderPath, "auralis-folder-icon-*.ico").Single();
            Assert.False(File.Exists(existingIconPath));
            Assert.Contains(
                $"IconResource=.\\{Path.GetFileName(rewrittenIconPath)},0",
                await File.ReadAllTextAsync(existingDesktopIniPath));
        }
        finally
        {
            ResetAttributesRecursively(testRoot);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveIconAsync_RemovesDesktopIniAndManagedIcons()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var folderPath = Path.Combine(testRoot, "TargetFolder");

        Directory.CreateDirectory(folderPath);

        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");
        var managedIconPath = Path.Combine(folderPath, "auralis-folder-icon-demo.ico");
        var legacyIconPath = Path.Combine(folderPath, "melhorwindows-folder-icon.ico");

        await File.WriteAllTextAsync(
            desktopIniPath,
            "[.ShellClassInfo]\r\nIconFile=auralis-folder-icon-demo.ico\r\nIconIndex=0");
        await File.WriteAllBytesAsync(managedIconPath, [1, 2, 3, 4, 5]);
        await File.WriteAllBytesAsync(legacyIconPath, [6, 7, 8, 9, 0]);

        File.SetAttributes(desktopIniPath, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(managedIconPath, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(legacyIconPath, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(folderPath, File.GetAttributes(folderPath) | FileAttributes.ReadOnly | FileAttributes.System);

        try
        {
            var service = new DesktopIniFolderIconIntegrationService();

            await service.RemoveIconAsync(folderPath);

            Assert.False(File.Exists(desktopIniPath));
            Assert.False(File.Exists(managedIconPath));
            Assert.False(File.Exists(legacyIconPath));

            var folderAttributes = File.GetAttributes(folderPath);
            Assert.False(folderAttributes.HasFlag(FileAttributes.ReadOnly));
            Assert.False(folderAttributes.HasFlag(FileAttributes.System));
        }
        finally
        {
            ResetAttributesRecursively(testRoot);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RepairIconReferenceAsync_MigratesExternalIconReferenceIntoFolder()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MelhorWindows.Tests", Guid.NewGuid().ToString("N"));
        var folderPath = Path.Combine(testRoot, "TargetFolder");
        var externalIconPath = Path.Combine(testRoot, "external-icon.ico");
        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");

        Directory.CreateDirectory(folderPath);
        await File.WriteAllBytesAsync(externalIconPath, [1, 2, 3, 4, 5, 6, 7, 8]);
        await File.WriteAllTextAsync(
            desktopIniPath,
            $"[.ShellClassInfo]{Environment.NewLine}IconResource={externalIconPath},0",
            Encoding.Unicode);

        try
        {
            var service = new DesktopIniFolderIconIntegrationService();

            var repaired = await service.RepairIconReferenceAsync(folderPath);

            Assert.True(repaired);

            var repairedIconPath = Directory.GetFiles(folderPath, "auralis-folder-icon-*.ico").Single();
            var desktopIniText = await File.ReadAllTextAsync(desktopIniPath);

            Assert.Contains($"IconResource=.\\{Path.GetFileName(repairedIconPath)},0", desktopIniText);
            Assert.True(File.Exists(repairedIconPath));
        }
        finally
        {
            ResetAttributesRecursively(testRoot);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void CreateSampleImage(string imagePath)
    {
        using var bitmap = new Bitmap(320, 180);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(14, 17, 22));
        graphics.FillEllipse(Brushes.Orange, 30, 18, 130, 130);
        graphics.FillRectangle(Brushes.DeepSkyBlue, 170, 28, 110, 110);
        bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void ResetAttributesRecursively(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var filePath in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (var directoryPath in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directoryPath, FileAttributes.Directory);
        }

        File.SetAttributes(rootPath, FileAttributes.Directory);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.GetFiles(sourcePath))
        {
            var targetPath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, targetPath, overwrite: true);
        }
    }

    private sealed class FakeUserContext(params string[] roleNames) : IUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public string UserName { get; } = "integration-test";

        public IReadOnlyCollection<string> RoleNames { get; } = roleNames;
    }
}
