param(
    [switch]$RemoveCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell elevado para remover o certificado do MSIX da maquina."
    }
}

$packageName = "Auralis.AuralisDesktop"
$directoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis"
$legacyDirectoryVerbKey = "HKCU:\Software\Classes\Directory\shell\Auralis.ChangeFolderIcon"
$folderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis"
$legacyFolderVerbKey = "HKCU:\Software\Classes\Folder\shell\Auralis.ChangeFolderIcon"
$certificateFile = Get-ChildItem $PSScriptRoot -Filter *.cer | Select-Object -First 1

$installedPackage = Get-AppxPackage $packageName -ErrorAction SilentlyContinue
if ($installedPackage) {
    $executablePath = Join-Path $installedPackage.InstallLocation "Auralis.exe"
    if (Test-Path $executablePath) {
        Start-Process -FilePath $executablePath -ArgumentList "--unregister-folder-verb" -WindowStyle Hidden -Wait
    }

    Remove-AppxPackage -Package $installedPackage.PackageFullName
}

Remove-Item -Path $directoryVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $legacyDirectoryVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $folderVerbKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $legacyFolderVerbKey -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveCertificate -and $certificateFile) {
    Assert-Administrator
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificateFile.FullName)
    foreach ($storeName in @("TrustedPeople", "Root")) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "LocalMachine")
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            $trustedCertificate = $store.Certificates | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
            if ($trustedCertificate) {
                $store.Remove($trustedCertificate)
            }
        }
        finally {
            $store.Close()
        }
    }
}

Write-Output "Auralis MSIX removed."
