<#
.SYNOPSIS
    Publishes the WinUI 3 spike with NativeAOT, setting the required LIB paths.

.DESCRIPTION
    Sets LIB to the Windows SDK + MSVC lib paths then runs dotnet publish with PublishAot=true,
    self-contained, win-x64. Assumes .NET SDK 8.0.402 (or newer) is installed at $HOME\.dotnet\8.

.PARAMETER Configuration
    Build configuration (Default: Release)

.EXAMPLE
    .\scripts\publish-winui-aot.ps1

.EXAMPLE
    .\scripts\publish-winui-aot.ps1 -Configuration Debug
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
    "LiMount.WinUI/LiMount.WinUI.csproj" `
    -c $Configuration `
    -r win-x64 `
    -p:PublishAot=true `
    --self-contained true

Write-Host "Publish complete. Output: LiMount.WinUI/bin/$Configuration/net8.0-windows10.0.22621.0/win-x64/publish/" -ForegroundColor Green
