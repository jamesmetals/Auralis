param(
    [string]$PublishDirectory = "$PSScriptRoot\..\publish\MelhorWindows.Desktop",
    [string]$OutputDirectory = "$PSScriptRoot\dist"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$publishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$installerRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$buildDirectory = Join-Path $installerRoot "build"
$payloadStage = Join-Path $buildDirectory "payload"
$packageStage = Join-Path $buildDirectory "package"
$bootstrapperPublishDirectory = Join-Path $buildDirectory "bootstrapper"
$payloadZip = Join-Path $packageStage "Auralis.Payload.zip"
$bootstrapperProject = Join-Path $installerRoot "MelhorWindows.SetupBootstrapper\MelhorWindows.SetupBootstrapper.csproj"
$targetInstaller = Join-Path $outputDirectory "Auralis-Setup.exe"
$targetPayload = Join-Path $outputDirectory "Auralis.Payload.zip"
$targetInstallScript = Join-Path $outputDirectory "Install-Auralis.ps1"
$bundleDirectory = Join-Path $buildDirectory "bundle"
$bundleZip = Join-Path $outputDirectory "Auralis-Installer.zip"

if (!(Test-Path $publishDirectory)) {
    throw "Publish directory not found: $publishDirectory"
}

foreach ($requiredScript in @("Install-MelhorWindows.ps1", "Uninstall-MelhorWindows.ps1")) {
    $scriptPath = Join-Path $installerRoot $requiredScript
    if (!(Test-Path $scriptPath)) {
        throw "Required installer script not found: $scriptPath"
    }
}

if (!(Test-Path $bootstrapperProject)) {
    throw "Bootstrapper project not found: $bootstrapperProject"
}

Remove-Item -Path $buildDirectory -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $outputDirectory) {
    Remove-Item -Path (Join-Path $outputDirectory "*") -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $payloadStage -Force | Out-Null
New-Item -ItemType Directory -Path $packageStage -Force | Out-Null
New-Item -ItemType Directory -Path $bootstrapperPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $payloadStage -Recurse -Force
Copy-Item -Path (Join-Path $installerRoot "Uninstall-MelhorWindows.ps1") -Destination (Join-Path $payloadStage "Uninstall-Auralis.ps1") -Force

if (Test-Path $payloadZip) {
    Remove-Item -Path $payloadZip -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadStage,
    $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)
Copy-Item -Path (Join-Path $installerRoot "Install-MelhorWindows.ps1") -Destination (Join-Path $packageStage "Install-Auralis.ps1") -Force

dotnet publish $bootstrapperProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $bootstrapperPublishDirectory | Out-Null

if (Test-Path $targetInstaller) {
    Remove-Item -Path $targetInstaller -Force
}

if (Test-Path $targetPayload) {
    Remove-Item -Path $targetPayload -Force
}

if (Test-Path $targetInstallScript) {
    Remove-Item -Path $targetInstallScript -Force
}

if (Test-Path $bundleZip) {
    Remove-Item -Path $bundleZip -Force
}

$publishedBootstrapper = Join-Path $bootstrapperPublishDirectory "Auralis.Setup.exe"
if (!(Test-Path $publishedBootstrapper)) {
    throw "Bootstrapper was not generated: $publishedBootstrapper"
}

Copy-Item -Path $publishedBootstrapper -Destination $targetInstaller -Force
Copy-Item -Path $payloadZip -Destination $targetPayload -Force
Copy-Item -Path (Join-Path $installerRoot "Install-MelhorWindows.ps1") -Destination $targetInstallScript -Force

Copy-Item -Path $targetInstaller -Destination $bundleDirectory -Force
Copy-Item -Path $targetPayload -Destination $bundleDirectory -Force
Copy-Item -Path $targetInstallScript -Destination $bundleDirectory -Force

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $bundleDirectory,
    $bundleZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

Get-Item $targetInstaller, $bundleZip | Select-Object FullName, Length
