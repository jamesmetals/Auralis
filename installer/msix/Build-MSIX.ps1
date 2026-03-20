param(
    [string]$PublishDirectory = "$PSScriptRoot\..\..\publish\MelhorWindows.Desktop",
    [string]$OutputDirectory = "$PSScriptRoot\dist",
    [string]$Publisher = "CN=Auralis Dev",
    [string]$PackageName = "Auralis.AuralisDesktop",
    [string]$CertificatePassword = "AuralisDev!"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Get-WindowsKitToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $candidates = Get-ChildItem $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$ToolName" }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Tool not found in Windows SDK: $ToolName"
}

function New-PngAsset {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $sourceImage = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $graphics.DrawImage($sourceImage, 0, 0, $Size, $Size)
        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $sourceImage.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-CodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Subject,
        [Parameter(Mandatory = $true)]
        [string]$PfxPath,
        [Parameter(Mandatory = $true)]
        [string]$CerPath,
        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    $existingCertificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.Subject -eq $Subject -and $_.HasPrivateKey
    } | Sort-Object NotAfter -Descending | Select-Object -First 1

    if (-not $existingCertificate) {
        $existingCertificate = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Subject `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears(5)
    }

    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    Export-PfxCertificate -Cert $existingCertificate -FilePath $PfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $existingCertificate -FilePath $CerPath | Out-Null
}

$publishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$msixRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$buildDirectory = Join-Path $msixRoot "build"
$packageRoot = Join-Path $buildDirectory "package"
$assetsDirectory = Join-Path $packageRoot "Assets"
$certsDirectory = Join-Path $msixRoot "certs"
$makeAppxPath = Get-WindowsKitToolPath -ToolName "makeappx.exe"
$signToolPath = Get-WindowsKitToolPath -ToolName "signtool.exe"
$dayOfYear = (Get-Date).DayOfYear.ToString("000")
$version = "1.0.$((Get-Date).ToString('yy'))$dayOfYear.$((Get-Date).ToString('HHmm'))"
$packageFileName = "Auralis_${version}_x64.msix"
$packagePath = Join-Path $outputDirectory $packageFileName
$certificateBaseName = "Auralis.Dev"
$certificatePfxPath = Join-Path $certsDirectory "$certificateBaseName.pfx"
$certificateCerPath = Join-Path $outputDirectory "$certificateBaseName.cer"
$installScriptSource = Join-Path $msixRoot "Install-MSIX.ps1"
$uninstallScriptSource = Join-Path $msixRoot "Uninstall-MSIX.ps1"
$installCommandSource = Join-Path $msixRoot "Install-MSIX.cmd"
$uninstallCommandSource = Join-Path $msixRoot "Uninstall-MSIX.cmd"
$installScriptOutput = Join-Path $outputDirectory "Install-Auralis-MSIX.ps1"
$uninstallScriptOutput = Join-Path $outputDirectory "Uninstall-Auralis-MSIX.ps1"
$installCommandOutput = Join-Path $outputDirectory "Install-Auralis-MSIX.cmd"
$uninstallCommandOutput = Join-Path $outputDirectory "Uninstall-Auralis-MSIX.cmd"
$bundleDirectory = Join-Path $buildDirectory "bundle"
$bundleZipPath = Join-Path $outputDirectory "Auralis-MSIX.zip"

if (!(Test-Path $publishDirectory)) {
    throw "Publish directory not found: $publishDirectory"
}

foreach ($requiredFile in @($installScriptSource, $uninstallScriptSource, $installCommandSource, $uninstallCommandSource)) {
    if (!(Test-Path $requiredFile)) {
        throw "Required file not found: $requiredFile"
    }
}

Remove-Item -Path $buildDirectory -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $outputDirectory) {
    Remove-Item -Path (Join-Path $outputDirectory "*") -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $certsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null

Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $packageRoot -Recurse -Force
Remove-Item -Path (Join-Path $packageRoot "desktop.ini") -Force -ErrorAction SilentlyContinue

$sourceIcon = Join-Path $publishDirectory "AppIcon.png"
if (!(Test-Path $sourceIcon)) {
    $sourceIcon = Join-Path $msixRoot "..\..\src\MelhorWindows.Desktop\Assets\AppIcon.png"
}

New-PngAsset -SourcePath $sourceIcon -DestinationPath (Join-Path $assetsDirectory "StoreLogo.png") -Size 50
New-PngAsset -SourcePath $sourceIcon -DestinationPath (Join-Path $assetsDirectory "Square44x44Logo.png") -Size 44
New-PngAsset -SourcePath $sourceIcon -DestinationPath (Join-Path $assetsDirectory "Square150x150Logo.png") -Size 150

$manifestPath = Join-Path $packageRoot "AppxManifest.xml"
$manifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 desktop6 rescap">
  <Identity Name="$PackageName" Version="$version" Publisher="$Publisher" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Auralis</DisplayName>
    <PublisherDisplayName>Auralis</PublisherDisplayName>
    <Description>Hub de personalizacao, automacao e otimizacao do Windows.</Description>
    <Logo>Assets\StoreLogo.png</Logo>
    <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
  </Properties>
  <Resources>
    <Resource Language="pt-BR" />
  </Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <rescap:Capability Name="unvirtualizedResources" />
  </Capabilities>
  <Applications>
    <Application
      Id="AuralisDesktop"
      Executable="Auralis.exe"
      uap10:RuntimeBehavior="packagedClassicApp"
      uap10:TrustLevel="mediumIL">
      <uap:VisualElements
        DisplayName="Auralis"
        Description="Hub de personalizacao, automacao e otimizacao do Windows."
        BackgroundColor="#0B1730"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
</Package>
"@

Set-Content -Path $manifestPath -Value $manifestContent -Encoding UTF8

if (Test-Path $packagePath) {
    Remove-Item -Path $packagePath -Force
}

& $makeAppxPath pack /d $packageRoot /p $packagePath /o | Out-Null

New-CodeSigningCertificate `
    -Subject $Publisher `
    -PfxPath $certificatePfxPath `
    -CerPath $certificateCerPath `
    -Password $CertificatePassword

& $signToolPath sign /fd SHA256 /f $certificatePfxPath /p $CertificatePassword $packagePath | Out-Null

Copy-Item -Path $installScriptSource -Destination $installScriptOutput -Force
Copy-Item -Path $uninstallScriptSource -Destination $uninstallScriptOutput -Force
Copy-Item -Path $installCommandSource -Destination $installCommandOutput -Force
Copy-Item -Path $uninstallCommandSource -Destination $uninstallCommandOutput -Force

Copy-Item -Path $packagePath -Destination $bundleDirectory -Force
Copy-Item -Path $certificateCerPath -Destination $bundleDirectory -Force
Copy-Item -Path $installScriptOutput -Destination $bundleDirectory -Force
Copy-Item -Path $uninstallScriptOutput -Destination $bundleDirectory -Force
Copy-Item -Path $installCommandOutput -Destination $bundleDirectory -Force
Copy-Item -Path $uninstallCommandOutput -Destination $bundleDirectory -Force

Compress-Archive -Path (Join-Path $bundleDirectory "*") -DestinationPath $bundleZipPath -CompressionLevel Optimal -Force

Get-Item $packagePath, $certificateCerPath, $installScriptOutput, $uninstallScriptOutput, $installCommandOutput, $uninstallCommandOutput, $bundleZipPath |
    Select-Object FullName, Length
