param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\MelhorWindows"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,
        [string]$WorkingDirectory
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

function Register-FolderVerb {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    $directoryShellPath = "Software\Classes\Directory\shell"
    $folderShellPath = "Software\Classes\Folder\shell"
    $verbName = "MelhorWindows.ChangeFolderIcon"
    $commandValue = "`"$ExecutablePath`" `"%1`""

    $directoryShellKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($directoryShellPath, $true)
    $directoryVerbKey = $directoryShellKey.CreateSubKey($verbName, $true)

    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("$folderShellPath\$verbName", $false)

    $directoryVerbKey.SetValue("MUIVerb", "Alterar icone com MelhorWindows")
    $directoryVerbKey.SetValue("Icon", $ExecutablePath)
    $directoryVerbKey.SetValue("Position", "Top")
    $directoryVerbKey.SetValue("MultiSelectModel", "Single")
    $directoryVerbKey.SetValue("NeverDefault", "")

    $commandKey = $directoryVerbKey.CreateSubKey("command", $true)
    $commandKey.SetValue("", $commandValue)

    $commandKey.Dispose()
    $directoryVerbKey.Dispose()
    $directoryShellKey.Dispose()
}

function Register-UninstallEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    $uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MelhorWindows"
    $uninstallScript = Join-Path $InstallDirectory "Uninstall-MelhorWindows.ps1"
    $uninstallCommand = "powershell.exe -ExecutionPolicy Bypass -NoProfile -File `"$uninstallScript`""

    $displayVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).FileVersion
    if ([string]::IsNullOrWhiteSpace($displayVersion)) {
        $displayVersion = "1.0.0"
    }

    $null = New-Item -Path $uninstallKey -Force
    New-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MelhorWindows" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value $displayVersion -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "MelhorWindows" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallDirectory -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $ExecutablePath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value $uninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "QuietUninstallString" -Value "$uninstallCommand -Silent" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null
}

function Refresh-Explorer {
    $refreshTool = Join-Path $env:SystemRoot "System32\ie4uinit.exe"

    if (Test-Path $refreshTool) {
        Start-Process -FilePath $refreshTool -ArgumentList "-ClearIconCache" -WindowStyle Hidden -Wait
        Start-Process -FilePath $refreshTool -ArgumentList "-show" -WindowStyle Hidden -Wait
    }
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$payloadZip = Join-Path $scriptDirectory "MelhorWindows.Payload.zip"

if (!(Test-Path $payloadZip)) {
    throw "Payload not found: $payloadZip"
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("MelhorWindows.Setup." + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
    Expand-Archive -Path $payloadZip -DestinationPath $temporaryDirectory -Force

    $processes = Get-Process MelhorWindows.Desktop -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path -like "$InstallRoot*"
    }

    if ($processes) {
        $processes | Stop-Process -Force
    }

    if (Test-Path $InstallRoot) {
        Remove-Item -Path $InstallRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $temporaryDirectory "*") -Destination $InstallRoot -Recurse -Force

    $executablePath = Join-Path $InstallRoot "MelhorWindows.Desktop.exe"
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "MelhorWindows.lnk"
    $startMenuShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "MelhorWindows\MelhorWindows.lnk"

    Register-FolderVerb -ExecutablePath $executablePath
    Register-UninstallEntry -InstallDirectory $InstallRoot -ExecutablePath $executablePath
    New-Shortcut -ShortcutPath $desktopShortcut -TargetPath $executablePath -WorkingDirectory $InstallRoot
    New-Shortcut -ShortcutPath $startMenuShortcut -TargetPath $executablePath -WorkingDirectory $InstallRoot
    Refresh-Explorer

    Write-Output "Installed MelhorWindows to $InstallRoot"
}
finally {
    if (Test-Path $temporaryDirectory) {
        Remove-Item -Path $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
