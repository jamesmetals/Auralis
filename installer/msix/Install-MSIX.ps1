param(
    [string]$PackagePath = (Join-Path $PSScriptRoot (Get-ChildItem $PSScriptRoot -Filter *.msix | Select-Object -First 1 -ExpandProperty Name)),
    [string]$CertificatePath = (Join-Path $PSScriptRoot (Get-ChildItem $PSScriptRoot -Filter *.cer | Select-Object -First 1 -ExpandProperty Name)),
    [switch]$SkipExplorerIntegration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell elevado. O certificado do MSIX precisa ser importado em LocalMachine."
    }
}

$packagePath = [System.IO.Path]::GetFullPath($PackagePath)
$certificatePath = [System.IO.Path]::GetFullPath($CertificatePath)
$packageName = "Auralis.AuralisDesktop"
$directoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis"
$legacyDirectoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis.ChangeFolderIcon"
$folderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis"
$legacyFolderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis.ChangeFolderIcon"

Assert-Administrator

if (!(Test-Path $packagePath)) {
    throw "MSIX package not found: $packagePath"
}

if (!(Test-Path $certificatePath)) {
    throw "Certificate not found: $certificatePath"
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
foreach ($storeName in @("TrustedPeople", "Root")) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "LocalMachine")
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $alreadyTrusted = $store.Certificates | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
        if (-not $alreadyTrusted) {
            $store.Add($certificate)
        }
    }
    finally {
        $store.Close()
    }
}

$existingPackages = Get-AppxPackage $packageName -ErrorAction SilentlyContinue
if ($existingPackages) {
    foreach ($existingPackage in $existingPackages) {
        Remove-AppxPackage -Package $existingPackage.PackageFullName -ErrorAction SilentlyContinue
    }
}

Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

$installedPackage = Get-AppxPackage $packageName -ErrorAction Stop
$executablePath = Join-Path $installedPackage.InstallLocation "Auralis.exe"

if (!(Test-Path $executablePath)) {
    throw "Installed executable not found: $executablePath"
}

if (-not $SkipExplorerIntegration) {
    Start-Process -FilePath $executablePath -ArgumentList "--register-folder-verb" -WindowStyle Hidden -Wait
}

Get-Item $packagePath | Select-Object FullName, Length | Format-Table -AutoSize
Write-Output "Installed package: $($installedPackage.PackageFullName)"
Write-Output "Installed location: $($installedPackage.InstallLocation)"
Write-Output "Explorer verb key: $directoryVerbKey"
if (Test-Path $folderVerbKey) {
    Write-Output "Warning: stale folder verb key still exists: $folderVerbKey"
}
if (Test-Path $legacyDirectoryVerbKey) {
    Write-Output "Warning: stale legacy directory verb key still exists: $legacyDirectoryVerbKey"
}
if (Test-Path $legacyFolderVerbKey) {
    Write-Output "Warning: stale legacy folder verb key still exists: $legacyFolderVerbKey"
}
