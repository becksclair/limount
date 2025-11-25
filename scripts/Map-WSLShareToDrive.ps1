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

# Test if UNC path is reachable (with retries for WSL startup delay)
Write-Verbose "Testing UNC path: $TargetUNC"
$maxRetries = 5
$retryDelayMs = 1000
$uncReachable = $false

for ($i = 0; $i -lt $maxRetries; $i++) {
    if (Test-Path -Path $TargetUNC -ErrorAction SilentlyContinue) {
        $uncReachable = $true
        break
    }
    Write-Verbose "UNC path not yet reachable, retry $($i + 1)/$maxRetries..."
    Start-Sleep -Milliseconds $retryDelayMs
}

if (-not $uncReachable) {
    Write-Result -Success $false -ErrorMessage "UNC path is not reachable after $maxRetries retries: $TargetUNC"
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

# For WSL paths (\\wsl$ or \\wsl.localhost), use subst command which works better
# net use and New-PSDrive often fail with "network resource type is not correct"
$isWslPath = $TargetUNC -match '^\\\\wsl[\$\.]'

if ($isWslPath) {
    Write-Verbose "Detected WSL path, using subst command..."
    
    # Check if running elevated - affects how we create the mapping
    $isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    
    # Clean up any existing subst mappings pointing to the same target
    # This prevents accumulating multiple drive letters for the same disk
    try {
        $existingSubst = subst 2>&1
        if ($existingSubst) {
            # Extract the mount point name (e.g., PHYSICALDRIVE1p1) from target path
            $targetMountPoint = if ($TargetUNC -match 'PHYSICALDRIVE\d+p\d+') { $Matches[0] } else { $null }
            
            foreach ($line in $existingSubst) {
                if ($line -match '^([A-Z]):\\: => (.+)$') {
                    $existingLetter = $Matches[1]
                    $existingPath = $Matches[2]
                    
                    # Remove if it points to the same disk mount point
                    if ($targetMountPoint -and $existingPath -match $targetMountPoint) {
                        Write-Verbose "Removing existing mapping $existingLetter`: pointing to same disk"
                        subst "$($existingLetter):" /d 2>&1 | Out-Null
                    }
                }
            }
        }
    } catch {
        Write-Verbose "Could not check existing subst mappings: $($_.Exception.Message)"
    }
    
    # Remove any existing subst mapping for the requested drive letter
    try {
        subst $driveWithColon /d 2>&1 | Out-Null
    } catch {
        # Ignore errors
    }
    
    try {
        if ($isElevated) {
            # When elevated, use a scheduled task to run subst in user context
            # This makes the drive visible in non-elevated Explorer
            Write-Verbose "Running elevated, using scheduled task to create mapping in user context..."
            
            $taskName = "LiMount_Map_$DriveLetter"
            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            
            # Delete any existing task
            schtasks /delete /tn $taskName /f 2>&1 | Out-Null
            
            # Create and run task to execute subst in user context
            $action = "subst $driveWithColon `"$TargetUNC`""
            schtasks /create /tn $taskName /tr "cmd /c $action" /sc once /st 00:00 /ru $currentUser /f 2>&1 | Out-Null
            schtasks /run /tn $taskName 2>&1 | Out-Null
            
            # Wait for task to complete
            Start-Sleep -Milliseconds 1500
            
            # Clean up the task
            schtasks /delete /tn $taskName /f 2>&1 | Out-Null
        } else {
            subst $driveWithColon $TargetUNC 2>&1 | Out-Null
        }
        
        # Verify the mapping worked
        Start-Sleep -Milliseconds 500
        
        if ($isElevated) {
            # When elevated, we can't see the user-context subst mapping
            # Verify by checking the target UNC path is accessible
            if (Test-Path $TargetUNC) {
                Write-Verbose "Target UNC accessible, mapping created in user context."
                Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
            } else {
                Write-Result -Success $false -ErrorMessage "Target UNC path not accessible: $TargetUNC"
            }
        } else {
            if (Test-Path "${driveWithColon}\") {
                Write-Verbose "Successfully mapped with subst."
                Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
            } else {
                Write-Result -Success $false -ErrorMessage "subst mapping not accessible at ${driveWithColon}\"
            }
        }
    } catch {
        Write-Result -Success $false -ErrorMessage "Failed to map drive with subst: $($_.Exception.Message)"
    }
} else {
    # For non-WSL paths, try net use first
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
}
