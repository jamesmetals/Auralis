using MelhorWindows.Application.Models;
using Microsoft.Win32;

namespace MelhorWindows.Application.Services;

public static class WindowsFeatureCatalog
{
    public static IReadOnlyList<WindowsFeatureDefinition> DefaultFeatures { get; } =
    [
        new(
            "explorer.show-file-extensions",
            "Show file extensions",
            "Toggles file extension visibility in File Explorer.",
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "HideFileExt",
                    0,
                    RegistryValueKind.DWord)
            ],
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "HideFileExt",
                    1,
                    RegistryValueKind.DWord)
            ]),
        new(
            "explorer.show-hidden-files",
            "Show hidden files",
            "Toggles hidden file visibility in File Explorer.",
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "Hidden",
                    1,
                    RegistryValueKind.DWord)
            ],
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "Hidden",
                    2,
                    RegistryValueKind.DWord)
            ]),
        new(
            "explorer.open-to-this-pc",
            "Open Explorer in This PC",
            "Changes the default File Explorer start location between This PC and Home/Quick Access.",
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "LaunchTo",
                    1,
                    RegistryValueKind.DWord)
            ],
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "LaunchTo",
                    2,
                    RegistryValueKind.DWord)
            ]),
        new(
            "theme.use-dark-app-mode",
            "Use dark app mode",
            "Changes the Windows app theme preference for the current user.",
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    0,
                    RegistryValueKind.DWord)
            ],
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1,
                    RegistryValueKind.DWord)
            ])
    ];
}
