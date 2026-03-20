using System.Drawing;
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
            var folderIconPath = Path.Combine(folderPath, "melhorwindows-folder-icon.ico");

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(desktopIniPath));
            Assert.True(File.Exists(folderIconPath));
            var desktopIniText = await File.ReadAllTextAsync(desktopIniPath);
            Assert.Contains(@"IconResource=melhorwindows-folder-icon.ico,0", desktopIniText);
            Assert.Contains(@"IconFile=melhorwindows-folder-icon.ico", desktopIniText);
            Assert.True(new FileInfo(folderIconPath).Length > 0);

            var folderAttributes = File.GetAttributes(folderPath);
            Assert.True(folderAttributes.HasFlag(FileAttributes.ReadOnly));
            Assert.False(folderAttributes.HasFlag(FileAttributes.System));

            var desktopIniAttributes = File.GetAttributes(desktopIniPath);
            Assert.True(desktopIniAttributes.HasFlag(FileAttributes.Hidden));
            Assert.True(desktopIniAttributes.HasFlag(FileAttributes.System));

            var copiedRoot = Path.Combine(testRoot, "CopiedFolder");
            CopyDirectory(folderPath, copiedRoot);
            Assert.True(File.Exists(Path.Combine(copiedRoot, "melhorwindows-folder-icon.ico")));
            Assert.Contains(
                @"IconResource=melhorwindows-folder-icon.ico,0",
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
            Assert.Contains(
                @"IconResource=melhorwindows-folder-icon.ico,0",
                await File.ReadAllTextAsync(existingDesktopIniPath));
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
