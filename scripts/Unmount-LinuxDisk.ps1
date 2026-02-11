<#
.SYNOPSIS
    Unmounts a Linux disk from WSL2.

.DESCRIPTION
    This script must run with Administrator privileges.
    It unmounts a physical disk using 'wsl --unmount'.

    Outputs machine-readable key=value pairs for consumption by the C# app.

.PARAMETER DiskIndex
    The physical disk index (0-based, e.g., 2 for \\.\PHYSICALDRIVE2).

.PARAMETER OutputFile
    Required: GUID-based temp file path where output will be written.
    This prevents predictable temp file attacks (CWE-377).

.PARAMETER SkipAdminCheck
    Optional: bypasses the local admin-role preflight check.
    Intended for automated hardware-in-loop test environments where
    `wsl --unmount` is permitted without an elevated PowerShell host.

.EXAMPLE
    .\Unmount-LinuxDisk.ps1 -DiskIndex 2 -OutputFile "C:\Users\...\Temp\limount_unmount_abc123.txt"

.NOTES
    Requires Windows 11 Build 22000+ or Microsoft Store WSL.
    Consulted: https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [int]$DiskIndex,

    [Parameter(Mandatory=$true)]
    [string]$OutputFile,

    [Parameter(Mandatory=$false)]
    [switch]$SkipAdminCheck
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

    # Write to the GUID-based temp file provided by the caller (prevents predictable temp file attacks)
    try {
        $output | Out-File -FilePath $OutputFile -Encoding UTF8 -Force
    } catch {
        # Ignore errors writing temp file
    }

    exit $exitCode
}

function Normalize-CommandText {
    param(
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $withoutNull = $Text -replace "`0", ""
    $withoutControls = [regex]::Replace($withoutNull, "[\x01-\x1F]+", " ")
    return ([regex]::Replace($withoutControls, "\s+", " ")).Trim()
}

# Require Administrator (unless explicitly bypassed for HIL test automation)
if (-not $SkipAdminCheck.IsPresent) {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Result -Success $false -DiskIndex $DiskIndex -ErrorMessage "This script requires Administrator privileges."
    }
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
        $errorText = Normalize-CommandText -Text ($unmountOutput -join " ")

        if ($errorText -match "not currently attached" -or
            $errorText -match "not mounted" -or
            $errorText -match "ERROR_FILE_NOT_FOUND" -or
            $errorText -match "Wsl/Service/DetachDisk" -or
            $errorText -match "The system cannot find the file specified") {
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
