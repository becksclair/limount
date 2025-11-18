<#
.SYNOPSIS
    Unmaps a Windows drive letter.

.DESCRIPTION
    This script removes a drive letter mapping created by net use or New-PSDrive.
    Does NOT require Administrator privileges.

    Outputs machine-readable key=value pairs for consumption by the C# app.

.PARAMETER DriveLetter
    The drive letter to unmap (e.g., "L" or "L:").

.EXAMPLE
    .\Unmap-DriveLetter.ps1 -DriveLetter L

.NOTES
    Works with mappings created by net use, New-PSDrive, or subst.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$DriveLetter
)

# Function to output result and exit
function Write-Result {
    param(
        [bool]$Success,
        [string]$DriveLetter = "",
        [string]$ErrorMessage = ""
    )

    if ($Success) {
        Write-Output "STATUS=OK"
        Write-Output "DriveLetter=$DriveLetter"
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

$driveWithColon = "${DriveLetter}:"

Write-Verbose "Unmapping drive letter $driveWithColon..."

# Try to remove using net use
try {
    $netUseResult = net use $driveWithColon /delete /y 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Verbose "Successfully unmapped with net use."
        Write-Result -Success $true -DriveLetter $DriveLetter
    }
} catch {
    Write-Verbose "net use failed or not applicable: $($_.Exception.Message)"
}

# Try to remove using Remove-PSDrive
try {
    $psDrive = Get-PSDrive -Name $DriveLetter -ErrorAction SilentlyContinue
    if ($psDrive) {
        Remove-PSDrive -Name $DriveLetter -Force -ErrorAction Stop
        Write-Verbose "Successfully unmapped with Remove-PSDrive."
        Write-Result -Success $true -DriveLetter $DriveLetter
    }
} catch {
    Write-Verbose "Remove-PSDrive failed: $($_.Exception.Message)"
}

# Try subst (if mapped via subst)
try {
    $substResult = subst $driveWithColon /d 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Verbose "Successfully unmapped with subst."
        Write-Result -Success $true -DriveLetter $DriveLetter
    }
} catch {
    Write-Verbose "subst failed: $($_.Exception.Message)"
}

# If we got here, the drive letter wasn't mapped or was already unmapped
Write-Verbose "Drive letter $driveWithColon was not mapped or already unmapped."
Write-Result -Success $true -DriveLetter $DriveLetter
