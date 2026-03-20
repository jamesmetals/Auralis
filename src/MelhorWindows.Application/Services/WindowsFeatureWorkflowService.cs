using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;

namespace MelhorWindows.Application.Services;

public sealed class WindowsFeatureWorkflowService(
    IAuthorizationService authorizationService,
    IRegistryEditingService registryEditingService,
    IRegistryInspectionService registryInspectionService,
    IRegistryAuditRepository registryAuditRepository)
{
    public async Task<IReadOnlyList<WindowsFeatureState>> GetStatesAsync(CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var states = new List<WindowsFeatureState>(WindowsFeatureCatalog.DefaultFeatures.Count);

        foreach (var feature in WindowsFeatureCatalog.DefaultFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await ResolveStatusAsync(feature, cancellationToken);
            states.Add(new WindowsFeatureState(feature.Id, feature.DisplayName, feature.Description, status));
        }

        return states;
    }

    public async Task<OperationResult<WindowsFeatureState>> SetStateAsync(
        string featureId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var feature = WindowsFeatureCatalog.DefaultFeatures.FirstOrDefault(item =>
            string.Equals(item.Id, featureId, StringComparison.OrdinalIgnoreCase));

        if (feature is null)
        {
            return OperationResult<WindowsFeatureState>.Failure("Feature not found.");
        }

        var changes = enabled ? feature.EnableChanges : feature.DisableChanges;
        var auditEntries = await registryEditingService.ApplyChangesAsync(changes, cancellationToken);
        await registryAuditRepository.AddRangeAsync(auditEntries, cancellationToken);

        var status = await ResolveStatusAsync(feature, cancellationToken);
        var state = new WindowsFeatureState(feature.Id, feature.DisplayName, feature.Description, status);

        return OperationResult<WindowsFeatureState>.Success(
            state,
            $"{feature.DisplayName} set to {(enabled ? "enabled" : "disabled")}.");
    }

    private async Task<WindowsFeatureStatus> ResolveStatusAsync(
        WindowsFeatureDefinition feature,
        CancellationToken cancellationToken)
    {
        if (await MatchesAsync(feature.EnableChanges, cancellationToken))
        {
            return WindowsFeatureStatus.Enabled;
        }

        if (await MatchesAsync(feature.DisableChanges, cancellationToken))
        {
            return WindowsFeatureStatus.Disabled;
        }

        return WindowsFeatureStatus.Custom;
    }

    private async Task<bool> MatchesAsync(
        IReadOnlyList<RegistryChangeRequest> expectedChanges,
        CancellationToken cancellationToken)
    {
        foreach (var expectedChange in expectedChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentValue = await registryInspectionService.GetValueAsync(
                expectedChange.Hive,
                expectedChange.KeyPath,
                expectedChange.ValueName,
                cancellationToken);

            if (!RegistryValuesEqual(currentValue, expectedChange.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RegistryValuesEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return leftBytes.SequenceEqual(rightBytes);
        }

        if (left is string[] leftArray && right is string[] rightArray)
        {
            return leftArray.SequenceEqual(rightArray, StringComparer.Ordinal);
        }

        return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.Ordinal);
    }
}
