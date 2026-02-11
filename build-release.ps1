<#
.SYNOPSIS
    Builds a release binary for the LiMount application.

.DESCRIPTION
    This script cleans, restores, and publishes a self-contained single-file executable
    for the LiMount WinUI 3 application using the win-x64 publish profile.

.PARAMETER Configuration
    Build configuration to use. Default is "Release".

.EXAMPLE
    .\build-release.ps1
    
.EXAMPLE
    .\build-release.ps1 -Configuration Release
#>

param(
    [string]$Configuration = "Release",
    [string]$Platform = "Any CPU"
)

# Set strict error handling
$ErrorActionPreference = "Stop"

# Colors for output
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }
function Write-Error { Write-Host $args -ForegroundColor Red }

# Validate .NET SDK is available
Write-Info "Checking for .NET SDK..."
try {
    $dotnetVersion = dotnet --version
    Write-Success "Found .NET SDK version: $dotnetVersion"
} catch {
    Write-Error "ERROR: .NET SDK not found. Please install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

# Validate script is run from project root
$solutionFile = "LiMount.sln"
if (-not (Test-Path $solutionFile)) {
    Write-Error "ERROR: Solution file '$solutionFile' not found. Please run this script from the project root directory."
    exit 1
}

Write-Success "Validated project root directory"

# Get absolute paths
$projectRoot = (Get-Location).Path
$appProject = Join-Path $projectRoot "LiMount.WinUI\LiMount.WinUI.csproj"
$outputPath = Join-Path $projectRoot "LiMount.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
$artifactDir = Join-Path $projectRoot "bin"
$platformProperty = "-p:Platform=$Platform"

Write-Info "Project root: $projectRoot"
Write-Info "Configuration: $Configuration"
Write-Info "Platform: $Platform"
Write-Info ""

# Step 1: Clean previous builds
Write-Info "Cleaning previous builds..."
try {
    dotnet clean $solutionFile --configuration $Configuration $platformProperty
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet clean failed with exit code $LASTEXITCODE"
    }
    Write-Success "Clean completed successfully"
} catch {
    Write-Error "ERROR: Failed to clean project: $_"
    exit 1
}

Write-Info ""

# Step 2: Restore NuGet packages
Write-Info "Restoring NuGet packages..."
try {
    dotnet restore $solutionFile $platformProperty
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE"
    }
    Write-Success "Restore completed successfully"
} catch {
    Write-Error "ERROR: Failed to restore packages: $_"
    exit 1
}

Write-Info ""

# Step 3: Publish using win-x64 profile
Write-Info "Publishing application (self-contained, single-file)..."
try {
    dotnet publish $appProject -p:PublishProfile=win-x64 --configuration $Configuration $platformProperty
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
    Write-Success "Publish completed successfully"
} catch {
    Write-Error "ERROR: Failed to publish application: $_"
    exit 1
}

Write-Info ""

# Step 4: Display output information
$exePath = Join-Path $outputPath "LiMount.WinUI.exe"

if (Test-Path $exePath) {
    $fileInfo = Get-Item $exePath
    $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)

    # Copy to root-level bin folder for easy pickup
    if (-not (Test-Path $artifactDir)) {
        New-Item -ItemType Directory -Path $artifactDir | Out-Null
    }

    $copiedExePath = Join-Path $artifactDir "LiMount.WinUI.exe"
    Copy-Item -Path $exePath -Destination $copiedExePath -Force
    
    Write-Success "=========================================="
    Write-Success "Build completed successfully!"
    Write-Success "=========================================="
    Write-Info ""
    Write-Info "Output location: $outputPath"
    Write-Info "Executable: LiMount.WinUI.exe"
    Write-Info "Copied artifact: $copiedExePath"
    Write-Info "File size: $fileSizeMB MB"
    Write-Info ""
    Write-Success "The application is ready for distribution."
    Write-Success "This is a self-contained executable that does not require .NET runtime."
    Write-Info ""
} else {
    Write-Error "ERROR: Expected executable not found at: $exePath"
    exit 1
}

exit 0

