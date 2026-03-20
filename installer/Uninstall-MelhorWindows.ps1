param(
    [switch]$Silent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "MelhorWindows.lnk"
$startMenuFolder = Join-Path ([Environment]::GetFolderPath("Programs")) "MelhorWindows"
$startMenuShortcut = Join-Path $startMenuFolder "MelhorWindows.lnk"
$directoryVerbKey = "HKCU:\Software\Classes\Directory\shell\MelhorWindows.ChangeFolderIcon"
$folderVerbKey = "HKCU:\Software\Classes\Folder\shell\MelhorWindows.ChangeFolderIcon"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MelhorWindows"

Get-Process MelhorWindows.Desktop -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path -like "$installRoot*"
} | Stop-Process -Force

Remove-Item -Path $directoryVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $folderVerbKey -Recurse -Force -ErrorAction SilentlyContinue
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
    [System.Windows.MessageBox]::Show("MelhorWindows removido.", "MelhorWindows")
}
