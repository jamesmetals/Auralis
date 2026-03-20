param(
    [switch]$Silent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Auralis.lnk"
$startMenuFolder = Join-Path ([Environment]::GetFolderPath("Programs")) "Auralis"
$startMenuShortcut = Join-Path $startMenuFolder "Auralis.lnk"
$directoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis"
$legacyDirectoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis.ChangeFolderIcon"
$folderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis"
$legacyFolderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis.ChangeFolderIcon"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Auralis"

Get-Process Auralis -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path -like "$installRoot*"
} | Stop-Process -Force

Remove-Item -Path $directoryVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $legacyDirectoryVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $folderVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $legacyFolderVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -Path $startMenuShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -Path $startMenuFolder -Recurse -Force -ErrorAction SilentlyContinue

$refreshTool = Join-Path $env:SystemRoot "System32\ie4uinit.exe"
if (Test-Path $refreshTool) {
    Start-Process -FilePath $refreshTool -ArgumentList "-show" -WindowStyle Hidden -Wait
}

$cleanupCommand = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q `"$installRoot`""
Start-Process -FilePath "cmd.exe" -ArgumentList $cleanupCommand -WindowStyle Hidden

if (-not $Silent) {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Auralis removido.", "Auralis")
}
