<#
.SYNOPSIS
    Publishes the WPF app (LiMount.App) using the existing win-x64 profile (ReadyToRun, single-file, self-contained).

.PARAMETER Configuration
    Build configuration (default: Release)
#>

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$dotnet = Join-Path $HOME ".dotnet\\8\\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    Write-Error "Expected dotnet at $dotnet (install .NET SDK 8.0.402+)."
    exit 1
}

& $dotnet publish `
    "LiMount.App/LiMount.App.csproj" `
    -c $Configuration `
    -p:PublishProfile=win-x64

Write-Host "Publish complete. Output: LiMount.App/bin/publish/win-x64/" -ForegroundColor Green
