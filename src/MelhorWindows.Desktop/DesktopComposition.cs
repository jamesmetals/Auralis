using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Services;
using MelhorWindows.Infrastructure.AI;
using MelhorWindows.Infrastructure.Imaging;
using MelhorWindows.Infrastructure.Security;
using MelhorWindows.Infrastructure.Storage;
using MelhorWindows.Infrastructure.Updates;
using MelhorWindows.WindowsIntegration.Explorer;
using MelhorWindows.WindowsIntegration.Registry;
using MelhorWindows.WindowsIntegration.System;

namespace MelhorWindows.Desktop;

internal static class DesktopComposition
{
    public static DesktopServices Create()
    {
        var appDataPaths = new AppDataPaths();
        var userContext = new DevelopmentUserContext();
        var authorizationService = new AuthorizationService(userContext);
        var protectedStateStore = new DpapiProtectedStateStore(appDataPaths);
        var historyRepository = new JsonIconHistoryRepository(appDataPaths);
        var registryAuditRepository = new JsonRegistryAuditRepository(appDataPaths);
        var iconStorageService = new FileSystemIconStorageService(appDataPaths);
        var imageService = new SystemDrawingIconConversionService();
        var appUpdateService = new GitHubAppUpdateService();
        var folderIconIntegrationService = new DesktopIniFolderIconIntegrationService();
        var registryInspectionService = new WindowsRegistryInspectionService();
        var windowsRestorePointService = new PowerShellRestorePointService();
        var computerDiagnosticsService = new WindowsComputerDiagnosticsService();
        var localAiGameBoosterService = new GoogleGeminiLocalAiGameBoosterService();
        var personalIconLibraryService = new JsonPersonalIconRepository(appDataPaths);
        var folderMonitorRegistrationService = new FolderMonitorRegistrationService(authorizationService);
        var tempCleanerService = new TempCleanerService();
        var startupManagerService = new StartupManagerService();
        var contextMenuCleanerService = new ContextMenuCleanerService();
        var duplicateFinderService = new DuplicateFinderService();
        var rustGameProfileService = new RustGameProfileService();
        var rustGameOptimizationWorkflowService = new RustGameOptimizationWorkflowService(
            protectedStateStore,
            rustGameProfileService);
        var folderIconWorkflowService = new FolderIconWorkflowService(
            authorizationService,
            userContext,
            imageService,
            iconStorageService,
            folderIconIntegrationService,
            historyRepository);
        var explorerVerbRegistrationService = new ExplorerVerbRegistrationService(authorizationService);
        var registryEditingService = new WindowsRegistryEditingService(authorizationService, userContext);
        var windowsFeatureWorkflowService = new WindowsFeatureWorkflowService(
            authorizationService,
            registryEditingService,
            registryInspectionService,
            registryAuditRepository,
            protectedStateStore,
            windowsRestorePointService);
        var gameBoosterWorkflowService = new GameBoosterWorkflowService(
            authorizationService,
            registryEditingService,
            registryInspectionService,
            registryAuditRepository,
            protectedStateStore,
            windowsRestorePointService,
            computerDiagnosticsService);
        var gameBoosterAiWorkflowService = new GameBoosterAiWorkflowService(
            protectedStateStore,
            localAiGameBoosterService,
            gameBoosterWorkflowService,
            rustGameOptimizationWorkflowService);

        return new DesktopServices(
            appDataPaths,
            userContext,
            authorizationService,
            protectedStateStore,
            historyRepository,
            registryAuditRepository,
            appUpdateService,
            imageService,
            folderIconIntegrationService,
            folderIconWorkflowService,
            explorerVerbRegistrationService,
            registryEditingService,
            windowsFeatureWorkflowService,
            gameBoosterWorkflowService,
            gameBoosterAiWorkflowService,
            personalIconLibraryService,
            folderMonitorRegistrationService,
            tempCleanerService,
            startupManagerService,
            contextMenuCleanerService,
            duplicateFinderService);
    }
}

internal sealed record DesktopServices(
    AppDataPaths AppDataPaths,
    IUserContext UserContext,
    IAuthorizationService AuthorizationService,
    IProtectedStateStore ProtectedStateStore,
    IIconHistoryRepository IconHistoryRepository,
    IRegistryAuditRepository RegistryAuditRepository,
    IAppUpdateService AppUpdateService,
    IImageIconConversionService ImageIconConversionService,
    IFolderIconIntegrationService FolderIconIntegrationService,
    FolderIconWorkflowService FolderIconWorkflowService,
    IExplorerVerbRegistrationService ExplorerVerbRegistrationService,
    IRegistryEditingService RegistryEditingService,
    WindowsFeatureWorkflowService WindowsFeatureWorkflowService,
    GameBoosterWorkflowService GameBoosterWorkflowService,
    GameBoosterAiWorkflowService GameBoosterAiWorkflowService,
    IPersonalIconLibraryService PersonalIconLibraryService,
    FolderMonitorRegistrationService FolderMonitorRegistrationService,
    TempCleanerService TempCleanerService,
    StartupManagerService StartupManagerService,
    ContextMenuCleanerService ContextMenuCleanerService,
    DuplicateFinderService DuplicateFinderService);

