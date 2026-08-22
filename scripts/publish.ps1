<#
.SYNOPSIS
  Automated self-contained publishing script for Windows (win-x64) and cross-platform targets.
.DESCRIPTION
  Publishes self-contained, single-file executables for RusZip CLI and Desktop.
.PARAMETER Rid
  Runtime Identifier (default: win-x64).
.PARAMETER Configuration
  Build configuration (default: Release).
.PARAMETER OutputDir
  Target output directory (default: dist/win-x64).
.EXAMPLE
  .\scripts\publish.ps1
.EXAMPLE
  .\scripts\publish.ps1 -Rid win-x64 -Configuration Release
.EXAMPLE
  .\scripts\publish.ps1 -OutputDir "dist\custom-output"
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$Rid = "win-x64",

    [Parameter()]
    [string]$Configuration = "Release",

    [Parameter()]
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = (Get-Item .).FullName
}

$RootDir = (Resolve-Path (Join-Path $ScriptDir "..")).Path
if (-not $OutputDir) {
    $OutputDir = Join-Path $RootDir "dist\$Rid"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $RootDir $OutputDir
}

function Assert-DotNetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "Error: 'dotnet' CLI not found. Please install the .NET 10 SDK."
        exit 1
    }

    $sdks = dotnet --list-sdks 2>$null
    $hasDotnet10 = $sdks | Where-Object { $_ -match '^10\.' }
    if (-not $hasDotnet10) {
        Write-Error "Error: .NET 10 SDK is required but not found in 'dotnet --list-sdks'."
        if ($sdks) {
            Write-Host "Installed SDKs:"
            $sdks | ForEach-Object { Write-Host "  $_" }
        }
        exit 1
    }
}

Assert-DotNetSdk

Write-Host "=================================================="
Write-Host "Publishing rus-zip"
Write-Host "  Target RID:        $Rid"
Write-Host "  Configuration:     $Configuration"
Write-Host "  Output Directory:  $OutputDir"
Write-Host "=================================================="

$tempStaging = Join-Path ([System.IO.Path]::GetTempPath()) "ruszip_publish_$([System.Guid]::NewGuid().ToString('N'))"
$tempCliDir = Join-Path $tempStaging "publish_cli_$Rid"
$tempDesktopDir = Join-Path $tempStaging "publish_desktop_$Rid"

try {
    # Clean and create target output directory
    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    # 1. Publish CLI (Self-contained, single file)
    Write-Host ""
    Write-Host "--> Publishing RusZip CLI..."
    $cliProject = Join-Path $RootDir "src\RusZip.Cli\RusZip.Cli.csproj"
    & dotnet publish $cliProject `
        -c $Configuration `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $tempCliDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to publish RusZip CLI."
        exit $LASTEXITCODE
    }

    # Locate and copy CLI executable
    $cliSourceCandidates = @(
        (Join-Path $tempCliDir "RusZip.Cli.exe"),
        (Join-Path $tempCliDir "rus-zip.exe"),
        (Join-Path $tempCliDir "RusZip.Cli"),
        (Join-Path $tempCliDir "rus-zip")
    )
    $cliSource = $cliSourceCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $cliSource) {
        Write-Error "Error: Published CLI executable not found in $tempCliDir."
        exit 1
    }

    $cliDestName = if ($Rid -like "win*") { "rus-zip.exe" } else { "rus-zip" }
    $cliDest = Join-Path $OutputDir $cliDestName
    Copy-Item -Path $cliSource -Destination $cliDest -Force

    # 2. Publish Desktop (Self-contained, single file)
    Write-Host ""
    Write-Host "--> Publishing RusZip Desktop..."
    $desktopProject = Join-Path $RootDir "src\RusZip.Desktop\RusZip.Desktop.csproj"
    & dotnet publish $desktopProject `
        -c $Configuration `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $tempDesktopDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to publish RusZip Desktop."
        exit $LASTEXITCODE
    }

    # Locate and copy Desktop executable
    $desktopSourceCandidates = @(
        (Join-Path $tempDesktopDir "RusZip.Desktop.exe"),
        (Join-Path $tempDesktopDir "RusZip.exe"),
        (Join-Path $tempDesktopDir "RusZip.Desktop"),
        (Join-Path $tempDesktopDir "RusZip")
    )
    $desktopSource = $desktopSourceCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $desktopSource) {
        Write-Error "Error: Published Desktop executable not found in $tempDesktopDir."
        exit 1
    }

    $desktopDestName = if ($Rid -like "win*") { "RusZip.Desktop.exe" } else { "RusZip.Desktop" }
    $desktopDest = Join-Path $OutputDir $desktopDestName
    Copy-Item -Path $desktopSource -Destination $desktopDest -Force

    # Output Summary
    Write-Host ""
    Write-Host "=================================================="
    Write-Host "Publish completed successfully!"
    Write-Host "=================================================="
    Write-Host "Output Directory: $OutputDir"
    if (Test-Path $cliDest) {
        $sizeMB = ((Get-Item $cliDest).Length / 1MB).ToString("F2")
        Write-Host "  - CLI Executable:     $cliDest ($sizeMB MB)"
    }
    if (Test-Path $desktopDest) {
        $sizeMB = ((Get-Item $desktopDest).Length / 1MB).ToString("F2")
        Write-Host "  - Desktop Executable: $desktopDest ($sizeMB MB)"
    }
    Write-Host "=================================================="
}
finally {
    if (Test-Path $tempStaging) {
        Remove-Item -Recurse -Force $tempStaging -ErrorAction SilentlyContinue
    }
}
