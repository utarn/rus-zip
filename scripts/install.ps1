<#
.SYNOPSIS
  rus-zip one-command CLI PATH installer for Windows.
.DESCRIPTION
  Installs rus-zip.exe into the user's local programs directory ($env:LOCALAPPDATA\Programs\rus-zip)
  and optionally appends the directory to the Windows User PATH environment variable.
.PARAMETER InstallDir
  Target directory for installation (default: $env:LOCALAPPDATA\Programs\rus-zip).
.PARAMETER AddToPath
  Whether to register the installation directory in the User PATH environment variable (default: $true).
.PARAMETER Rid
  Target Runtime Identifier (default: win-x64).
.PARAMETER Configuration
  Build configuration when publishing from source (default: Release).
.PARAMETER Build
  Force rebuilding CLI from source even if a pre-built binary exists in dist\.
.PARAMETER Help
  Show help and usage information.
.EXAMPLE
  .\scripts\install.ps1
.EXAMPLE
  .\scripts\install.ps1 -InstallDir "C:\Tools\rus-zip"
.EXAMPLE
  .\scripts\install.ps1 -AddToPath:$false
.EXAMPLE
  .\scripts\install.ps1 -Build
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$InstallDir,

    [Parameter()]
    [bool]$AddToPath = $true,

    [Parameter()]
    [string]$Rid = "win-x64",

    [Parameter()]
    [string]$Configuration = "Release",

    [Parameter()]
    [switch]$Build,

    [Parameter()]
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = (Get-Item .).FullName
}

$RootDir = (Resolve-Path (Join-Path $ScriptDir "..")).Path

function Show-Usage {
    Write-Host @"
rus-zip CLI Installer for Windows

Usage:
  .\scripts\install.ps1 [options]

Parameters:
  -InstallDir <DIR>       Target install directory (default: `$env:LOCALAPPDATA\Programs\rus-zip)
  -AddToPath <`$true|`$false> Register install directory in User PATH (default: `$true)
  -Rid <RID>              Runtime Identifier (default: win-x64)
  -Configuration <CONFIG> Build configuration when building from source (default: Release)
  -Build                  Force rebuilding CLI from source even if pre-built binary exists
  -Help                   Show this help message

Examples:
  .\scripts\install.ps1
  .\scripts\install.ps1 -InstallDir "C:\Tools\rus-zip"
  .\scripts\install.ps1 -AddToPath:`$false
  .\scripts\install.ps1 -Build
"@
}

if ($Help) {
    Show-Usage
    exit 0
}

# Resolve default install directory
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $localAppData = $env:LOCALAPPDATA
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $userProfile = $env:USERPROFILE
        if ([string]::IsNullOrWhiteSpace($userProfile)) {
            $userProfile = $HOME
        }
        $localAppData = Join-Path $userProfile "AppData\Local"
    }
    $InstallDir = Join-Path $localAppData "Programs\rus-zip"
} elseif (-not [System.IO.Path]::IsPathRooted($InstallDir)) {
    $InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
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

Write-Host "=================================================="
Write-Host "Installing rus-zip CLI"
Write-Host "  Target RID:        $Rid"
Write-Host "  Install Directory: $InstallDir"
Write-Host "=================================================="

$cliSource = $null
$tempStaging = $null

try {
    # 1. Locate pre-built binary or publish from source
    if (-not $Build) {
        $candidatePaths = @(
            (Join-Path $RootDir "dist\$Rid\rus-zip.exe"),
            (Join-Path $RootDir "dist\rus-zip.exe"),
            (Join-Path $RootDir "dist\$Rid\rus-zip"),
            (Join-Path $RootDir "dist\rus-zip")
        )
        $cliSource = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($cliSource) {
            Write-Host "[+] Found pre-built binary: $cliSource"
        }
    }

    if (-not $cliSource) {
        Write-Host "[*] Building self-contained CLI binary from source ($Rid, $Configuration)..."
        Assert-DotNetSdk

        $tempStaging = Join-Path ([System.IO.Path]::GetTempPath()) "ruszip_install_$([System.Guid]::NewGuid().ToString('N'))"
        $tempCliDir = Join-Path $tempStaging "publish_cli_$Rid"

        $cliProject = Join-Path $RootDir "src\RusZip.Cli\RusZip.Cli.csproj"
        & dotnet publish $cliProject `
            -c $Configuration `
            -r $Rid `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $tempCliDir

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build RusZip CLI."
            exit $LASTEXITCODE
        }

        $sourceCandidates = @(
            (Join-Path $tempCliDir "RusZip.Cli.exe"),
            (Join-Path $tempCliDir "rus-zip.exe"),
            (Join-Path $tempCliDir "RusZip.Cli"),
            (Join-Path $tempCliDir "rus-zip")
        )
        $cliSource = $sourceCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

        if (-not $cliSource) {
            Write-Error "Error: Published CLI executable not found in $tempCliDir."
            exit 1
        }
    }

    # 2. Ensure install directory exists and copy binary
    Write-Host "[*] Installing binary into $InstallDir..."
    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    $destFileName = if ($Rid -like "win*") { "rus-zip.exe" } else { "rus-zip" }
    $destFilePath = Join-Path $InstallDir $destFileName
    Copy-Item -Path $cliSource -Destination $destFilePath -Force

    $destFileInfo = Get-Item $destFilePath
    $sizeMB = ($destFileInfo.Length / 1MB).ToString("F2")

    # 3. Add to User PATH if requested
    $pathUpdated = $false
    if ($AddToPath) {
        try {
            $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
            $userPathEntries = if ($userPath) {
                $userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            } else {
                @()
            }

            $normalizedInstallDir = $InstallDir.TrimEnd('\', '/')
            $alreadyPresent = $userPathEntries | Where-Object {
                $_.TrimEnd('\', '/') -ieq $normalizedInstallDir
            }

            if (-not $alreadyPresent) {
                $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
                    $InstallDir
                } else {
                    "$userPath;$InstallDir"
                }
                [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
                Write-Host "[+] Added '$InstallDir' to User PATH environment variable."
                $pathUpdated = $true
            } else {
                Write-Host "[✓] '$InstallDir' is already in your User PATH."
                $pathUpdated = $true
            }

            # Update current PowerShell process PATH
            $processPathEntries = $env:Path -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            $inProcessPath = $processPathEntries | Where-Object {
                $_.TrimEnd('\', '/') -ieq $normalizedInstallDir
            }
            if (-not $inProcessPath) {
                $env:Path = "$InstallDir;$env:Path"
            }
        } catch {
            Write-Warning "Could not update User PATH automatically: $($_.Exception.Message)"
        }
    }

    # 4. Output Summary and Sample Commands
    Write-Host ""
    Write-Host "=================================================="
    Write-Host "rus-zip CLI successfully installed!"
    Write-Host "=================================================="
    Write-Host "  Location: $destFilePath ($sizeMB MB)"
    Write-Host ""

    if (-not $AddToPath -or -not $pathUpdated) {
        Write-Host "[!] Note: To run 'rus-zip' directly from any terminal, add the following directory to your PATH:"
        Write-Host "    $InstallDir"
        Write-Host ""
    }

    Write-Host "Quick Start Commands:"
    Write-Host "  rus-zip compress <source> <archive.zrus> --profile high   # Create a .zrus archive"
    Write-Host "  rus-zip extract <archive.zrus> -o <destination>           # Extract archive"
    Write-Host "  rus-zip list <archive.zrus>                               # List archive contents"
    Write-Host "  rus-zip info <archive.zrus>                               # Inspect archive metadata & stats"
    Write-Host "  rus-zip --help                                            # Show all CLI commands & options"
    Write-Host "=================================================="
}
finally {
    if ($tempStaging -and (Test-Path $tempStaging)) {
        Remove-Item -Recurse -Force $tempStaging -ErrorAction SilentlyContinue
    }
}
