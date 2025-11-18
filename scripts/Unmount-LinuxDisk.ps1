<#
.SYNOPSIS
    Unmounts a Linux disk from WSL2.

.DESCRIPTION
    This script must run with Administrator privileges.
    It unmounts a physical disk using 'wsl --unmount'.

    Outputs machine-readable key=value pairs for consumption by the C# app.

.PARAMETER DiskIndex
    The physical disk index (0-based, e.g., 2 for \\.\PHYSICALDRIVE2).

.EXAMPLE
    .\Unmount-LinuxDisk.ps1 -DiskIndex 2

.NOTES
    Requires Windows 11 Build 22000+ or Microsoft Store WSL.
    Consulted: https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [int]$DiskIndex
)

# Function to output result and exit
function Write-Result {
    param(
        [bool]$Success,
        [int]$DiskIndex = 0,
        [string]$ErrorMessage = ""
    )

    $output = @()

    if ($Success) {
        $output += "STATUS=OK"
        $exitCode = 0
    } else {
        $output += "STATUS=ERROR"
        $exitCode = 1
    }

    # Always include DiskIndex so the .NET parser can identify which disk was processed
    $output += "DiskIndex=$DiskIndex"

    if (-not $Success) {
        $output += "ErrorMessage=$ErrorMessage"
    }

    # Write to stdout
    $output | ForEach-Object { Write-Output $_ }

    # Also write to temp file for elevated scenarios
    try {
        $tempFile = Join-Path $env:TEMP "limount_unmount_${DiskIndex}.txt"
        $output | Out-File -FilePath $tempFile -Encoding UTF8 -Force
    } catch {
        # Ignore errors writing temp file
    }

    exit $exitCode
}

# Require Administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Result -Success $false -DiskIndex $DiskIndex -ErrorMessage "This script requires Administrator privileges."
}

# Build physical disk path
$diskPath = "\\.\PHYSICALDRIVE$DiskIndex"

# Execute wsl --unmount
Write-Verbose "Unmounting $diskPath from WSL..."

try {
    $unmountOutput = & wsl.exe --unmount $diskPath 2>&1
    $unmountExitCode = $LASTEXITCODE

    if ($unmountExitCode -ne 0) {
        # Check for common error messages
        $errorText = $unmountOutput -join " "

        if ($errorText -match "not currently attached" -or $errorText -match "not mounted") {
            Write-Verbose "Disk was not mounted, considering this a success..."
            Write-Result -Success $true -DiskIndex $DiskIndex
        } elseif ($errorText -match "not recognized" -or $errorText -match "invalid option") {
            Write-Result -Success $false -DiskIndex $DiskIndex -ErrorMessage "wsl --unmount is not supported. Ensure you are running Windows 11 Build 22000+ or Microsoft Store WSL."
        } else {
            Write-Result -Success $false -DiskIndex $DiskIndex -ErrorMessage "wsl --unmount failed (exit code $unmountExitCode): $errorText"
        }
    }

    # Success
    Write-Result -Success $true -DiskIndex $DiskIndex
} catch {
    Write-Result -Success $false -DiskIndex $DiskIndex -ErrorMessage "Failed to execute wsl --unmount: $($_.Exception.Message)"
}
