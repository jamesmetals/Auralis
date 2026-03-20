namespace MelhorWindows.Application.Models;

public sealed record WindowsFeatureState(
    string Id,
    string DisplayName,
    string Description,
    WindowsFeatureStatus Status);

public enum WindowsFeatureStatus
{
    Unknown = 0,
    Enabled = 1,
    Disabled = 2,
    Custom = 3
}
