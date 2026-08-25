<#
.SYNOPSIS
  Bumps the semantic version for rus-zip across project files.
.DESCRIPTION
  Updates VERSION and Directory.Build.props for patch, minor, major, or explicit versions.
.PARAMETER BumpType
  The type of version bump (patch, minor, major) or an explicit version string (e.g. 1.2.3). Default: patch.
.EXAMPLE
  .\bump_version.ps1 patch
.EXAMPLE
  .\bump_version.ps1 minor
.EXAMPLE
  .\bump_version.ps1 major
.EXAMPLE
  .\bump_version.ps1 1.2.3
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$BumpType = "patch"
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = (Get-Item .).FullName
}

$RootDir = $ScriptDir
$VersionFile = Join-Path $RootDir "VERSION"
$PropsFile = Join-Path $RootDir "Directory.Build.props"

if (-not (Test-Path $VersionFile)) {
    Set-Content -Path $VersionFile -Value "1.0.0"
}

$CurrentVersion = (Get-Content -Path $VersionFile -Raw).Trim()

$parts = $CurrentVersion.Split('.')
[int]$major = if ($parts.Length -gt 0) { [int]($parts[0] -replace '[^\d]', '') } else { 1 }
[int]$minor = if ($parts.Length -gt 1) { [int]($parts[1] -replace '[^\d]', '') } else { 0 }
[int]$patch = if ($parts.Length -gt 2) { [int]($parts[2].Split('-')[0] -replace '[^\d]', '') } else { 0 }

$newVersion = ""

switch ($BumpType.ToLowerInvariant()) {
    "major" {
        $newVersion = "$($major + 1).0.0"
    }
    "minor" {
        $newVersion = "$major.$($minor + 1).0"
    }
    "patch" {
        $newVersion = "$major.$minor.$($patch + 1)"
    }
    default {
        if ($BumpType -match '^\d+\.\d+\.\d+') {
            $newVersion = $BumpType
        } else {
            Write-Error "Invalid argument '$BumpType'. Usage: .\bump_version.ps1 [major|minor|patch|<version_string>]"
            exit 1
        }
    }
}

Write-Host "=================================================="
Write-Host "Bumping rus-zip version: $CurrentVersion -> $newVersion"
Write-Host "=================================================="

# 1. Update VERSION file
Set-Content -Path $VersionFile -Value $newVersion

# 2. Update Directory.Build.props
if (Test-Path $PropsFile) {
    $content = Get-Content -Path $PropsFile -Raw
    $updated = $content -replace '<VersionPrefix>.*?</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>"
    Set-Content -Path $PropsFile -Value $updated
}

Write-Host "Version successfully updated to $newVersion across project configuration."
