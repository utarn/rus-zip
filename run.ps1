<#
.SYNOPSIS
  rus-zip runner script for Windows PowerShell and PowerShell 7+
.DESCRIPTION
  Dispatches execution to RusZip Desktop, CLI, Test suite, or Build.
.EXAMPLE
  .\run.ps1 desktop
.EXAMPLE
  .\run.ps1 cli compress src/ backup.zrus --profile high
.EXAMPLE
  .\run.ps1 test
.EXAMPLE
  .\run.ps1 build
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$Command,

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = (Get-Item .).FullName
}

function Show-Usage {
    Write-Host @"
rus-zip runner script for Windows

Usage:
  .\run.ps1 <command> [args...]

Commands:
  desktop, gui     Run the RusZip Avalonia Desktop application
  cli              Run the RusZip CLI tool (passes remaining args)
  test             Run all unit and integration tests
  build            Build the solution
  help, -h, --help Show this help message

Examples:
  .\run.ps1 desktop
  .\run.ps1 cli compress src\ backup.zrus --profile high
  .\run.ps1 cli list backup.zrus
  .\run.ps1 test
  .\run.ps1 build -c Release
"@
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

if ([string]::IsNullOrWhiteSpace($Command)) {
    Show-Usage
    exit 0
}

switch ($Command.ToLowerInvariant()) {
    { $_ -in 'help', '-h', '--help', '/?' } {
        Show-Usage
        exit 0
    }
    { $_ -in 'desktop', 'gui' } {
        Assert-DotNetSdk
        $desktopProject = Join-Path $ScriptDir "src\RusZip.Desktop"
        if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
            & dotnet run --project $desktopProject -- @RemainingArgs
        } else {
            & dotnet run --project $desktopProject
        }
        exit $LASTEXITCODE
    }
    'cli' {
        Assert-DotNetSdk
        $cliProject = Join-Path $ScriptDir "src\RusZip.Cli"
        if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
            & dotnet run --project $cliProject -- @RemainingArgs
        } else {
            & dotnet run --project $cliProject
        }
        exit $LASTEXITCODE
    }
    'test' {
        Assert-DotNetSdk
        $slnxFile = Join-Path $ScriptDir "RusZip.slnx"
        if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
            & dotnet test @RemainingArgs
        } else {
            & dotnet test $slnxFile
        }
        exit $LASTEXITCODE
    }
    'build' {
        Assert-DotNetSdk
        $slnxFile = Join-Path $ScriptDir "RusZip.slnx"
        if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
            & dotnet build @RemainingArgs
        } else {
            & dotnet build $slnxFile
        }
        exit $LASTEXITCODE
    }
    Default {
        Write-Error "Error: Unknown command '$Command'."
        Show-Usage
        exit 1
    }
}
