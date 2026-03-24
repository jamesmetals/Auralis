param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Auralis"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Stop-InstalledProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory
    )

    Get-Process Auralis -ErrorAction SilentlyContinue | Where-Object {
        if (-not $_.Path) {
            return $false
        }

        $processPath = [System.IO.Path]::GetFullPath($_.Path)
        $processPath.StartsWith($InstallDirectory, [System.StringComparison]::OrdinalIgnoreCase)
    } | Stop-Process -Force
}

function Invoke-RobocopyMirror {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    $null = New-Item -ItemType Directory -Path $DestinationDirectory -Force

    & robocopy $SourceDirectory $DestinationDirectory /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    $exitCode = $LASTEXITCODE

    if ($exitCode -gt 7) {
        throw "Falha ao sincronizar o Auralis instalado via robocopy. Codigo: $exitCode"
    }
}

$projectFullPath = [System.IO.Path]::GetFullPath($ProjectPath)
$normalizedInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$publishDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("Auralis.Sync." + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

    & dotnet publish $projectFullPath `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:SkipSyncInstalledAuralis=true `
        -o $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao publicar o projeto desktop para sincronizar o Auralis instalado."
    }

    Stop-InstalledProcesses -InstallDirectory $normalizedInstallRoot
    Start-Sleep -Milliseconds 300
    Invoke-RobocopyMirror -SourceDirectory $publishDirectory -DestinationDirectory $normalizedInstallRoot
}
finally {
    if (Test-Path $publishDirectory) {
        Remove-Item -Path $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
