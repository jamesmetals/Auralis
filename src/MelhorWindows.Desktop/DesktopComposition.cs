using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Services;
using MelhorWindows.Infrastructure.Imaging;
using MelhorWindows.Infrastructure.Security;
using MelhorWindows.Infrastructure.Storage;
using MelhorWindows.Infrastructure.Updates;
using MelhorWindows.WindowsIntegration.Explorer;
using MelhorWindows.WindowsIntegration.Registry;

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
            registryAuditRepository);

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
            windowsFeatureWorkflowService);
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
    WindowsFeatureWorkflowService WindowsFeatureWorkflowService);
