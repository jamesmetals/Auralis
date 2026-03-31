namespace MelhorWindows.Infrastructure.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auralis");

        IconCacheDirectory = Path.Combine(RootDirectory, "icons");
        PreviewCacheDirectory = Path.Combine(RootDirectory, "previews");
        StateDirectory = Path.Combine(RootDirectory, "state");
        SecureStateDirectory = Path.Combine(RootDirectory, "secure-state");
        PersonalIconsDirectory = Path.Combine(RootDirectory, "personal-icons");
        HistoryFilePath = Path.Combine(StateDirectory, "folder-icon-history.json");
        RegistryAuditFilePath = Path.Combine(StateDirectory, "registry-audit.json");
        PersonalIconsFilePath = Path.Combine(StateDirectory, "personal-icons.json");
    }

    public string RootDirectory { get; }

    public string IconCacheDirectory { get; }

    public string PreviewCacheDirectory { get; }

    public string StateDirectory { get; }

    public string SecureStateDirectory { get; }

    public string HistoryFilePath { get; }

    public string RegistryAuditFilePath { get; }

    public string PersonalIconsDirectory { get; }

    public string PersonalIconsFilePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
        Directory.CreateDirectory(PreviewCacheDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(SecureStateDirectory);
        Directory.CreateDirectory(PersonalIconsDirectory);
    }
}
