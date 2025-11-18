<#
.SYNOPSIS
    Maps a WSL UNC path to a Windows drive letter.

.DESCRIPTION
    This script maps a UNC path (e.g., \\wsl$\Ubuntu\mnt\wsl\...) to a Windows
    drive letter using 'net use' or 'New-PSDrive'.

    Outputs machine-readable key=value pairs for consumption by the C# app.

    Does NOT require Administrator privileges.

.PARAMETER DriveLetter
    The drive letter to map (e.g., "L" or "L:"). Will be normalized to uppercase.

.PARAMETER TargetUNC
    The UNC path to map (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1).

.EXAMPLE
    .\Map-WSLShareToDrive.ps1 -DriveLetter L -TargetUNC "\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1"

.NOTES
    Consulted: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$DriveLetter,

    [Parameter(Mandatory=$true)]
    [string]$TargetUNC
)

# Function to output result and exit
function Write-Result {
    param(
        [bool]$Success,
        [string]$DriveLetter = "",
        [string]$MappedTo = "",
        [string]$ErrorMessage = ""
    )

    if ($Success) {
        Write-Output "STATUS=OK"
        Write-Output "DriveLetter=$DriveLetter"
        Write-Output "MappedTo=$MappedTo"
        exit 0
    } else {
        Write-Output "STATUS=ERROR"
        Write-Output "ErrorMessage=$ErrorMessage"
        exit 1
    }
}

# Normalize drive letter to single uppercase char A-Z
$DriveLetter = $DriveLetter.Trim().ToUpperInvariant() -replace ':', ''

if ($DriveLetter.Length -ne 1 -or $DriveLetter -notmatch '^[A-Z]$') {
    Write-Result -Success $false -ErrorMessage "Invalid drive letter: $DriveLetter. Must be A-Z."
}

# Validate TargetUNC
if ([string]::IsNullOrWhiteSpace($TargetUNC)) {
    Write-Result -Success $false -ErrorMessage "TargetUNC cannot be empty."
}

$TargetUNC = $TargetUNC.Trim()

# Test if UNC path is reachable
Write-Verbose "Testing UNC path: $TargetUNC"
if (-not (Test-Path -Path $TargetUNC -ErrorAction SilentlyContinue)) {
    Write-Result -Success $false -ErrorMessage "UNC path is not reachable: $TargetUNC"
}

# Check if drive letter is already in use
$driveWithColon = "${DriveLetter}:"

Write-Verbose "Checking if drive letter $driveWithColon is in use..."

$existingDrive = $null
try {
    $existingDrive = Get-PSDrive -Name $DriveLetter -ErrorAction SilentlyContinue
} catch {
    # Drive not found, which is fine
}

if ($existingDrive) {
    # Drive is in use - check if it's mapped to the same target
    $existingRoot = $existingDrive.Root

    if ($existingRoot -eq $TargetUNC) {
        Write-Verbose "Drive $driveWithColon is already mapped to $TargetUNC. Unmapping and remapping..."

        # Unmap first
        try {
            Remove-PSDrive -Name $DriveLetter -Force -ErrorAction SilentlyContinue | Out-Null
        } catch {
            # Ignore errors
        }

        try {
            net use $driveWithColon /delete /y 2>&1 | Out-Null
        } catch {
            # Ignore errors
        }
    } else {
        Write-Result -Success $false -ErrorMessage "Drive letter $driveWithColon is already in use (mapped to $existingRoot)."
    }
}

# Also check net use mappings
try {
    $netUseOutput = net use 2>&1
    if ($netUseOutput -match "$driveWithColon") {
        Write-Verbose "Drive $driveWithColon found in net use, attempting to remove..."
        net use $driveWithColon /delete /y 2>&1 | Out-Null
    }
} catch {
    # Ignore errors
}

# Try to map using 'net use' first
Write-Verbose "Mapping $driveWithColon to $TargetUNC using net use..."

try {
    $netUseResult = net use $driveWithColon $TargetUNC 2>&1
    $netUseExitCode = $LASTEXITCODE

    if ($netUseExitCode -eq 0) {
        # Verify the mapping worked
        if (Test-Path "${driveWithColon}\") {
            Write-Verbose "Successfully mapped with net use."
            Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
        } else {
            Write-Verbose "net use succeeded but path not accessible, trying New-PSDrive..."
        }
    } else {
        Write-Verbose "net use failed (exit code $netUseExitCode): $netUseResult"
        Write-Verbose "Trying New-PSDrive as fallback..."
    }
} catch {
    Write-Verbose "net use threw exception: $($_.Exception.Message)"
    Write-Verbose "Trying New-PSDrive as fallback..."
}

# Fallback: try New-PSDrive
Write-Verbose "Mapping $driveWithColon to $TargetUNC using New-PSDrive..."

try {
    # Remove any existing PSDrive first
    Remove-PSDrive -Name $DriveLetter -Force -ErrorAction SilentlyContinue | Out-Null

    $newDrive = New-PSDrive -Name $DriveLetter -PSProvider FileSystem -Root $TargetUNC -Persist -Scope Global -ErrorAction Stop

    if ($newDrive) {
        # Verify the mapping worked
        if (Test-Path "${driveWithColon}\") {
            Write-Verbose "Successfully mapped with New-PSDrive."
            Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
        } else {
            Write-Result -Success $false -ErrorMessage "New-PSDrive succeeded but path ${driveWithColon}\ is not accessible."
        }
    } else {
        Write-Result -Success $false -ErrorMessage "New-PSDrive returned null."
    }
} catch {
    Write-Result -Success $false -ErrorMessage "Failed to map drive with New-PSDrive: $($_.Exception.Message)"
}
