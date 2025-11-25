<#
.SYNOPSIS
    Mounts a Linux disk partition into WSL2 and reports the mount path.

.DESCRIPTION
    This script must run with Administrator privileges.
    It mounts a physical disk partition using 'wsl --mount' and determines
    the mounted path for mapping as a Windows drive letter.

    Outputs machine-readable key=value pairs for consumption by the C# app.

.PARAMETER DiskIndex
    The physical disk index (0-based, e.g., 2 for \\.\PHYSICALDRIVE2).

.PARAMETER Partition
    The partition number to mount (1-based).

.PARAMETER FsType
    The filesystem type (ext4, xfs, etc.). Defaults to ext4.

.PARAMETER DistroName
    Optional: Specific WSL distribution name to use.

.EXAMPLE
    .\Mount-LinuxDiskCore.ps1 -DiskIndex 2 -Partition 1 -FsType ext4

.NOTES
    Requires Windows 11 Build 22000+ or Microsoft Store WSL.
    Consulted: https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [int]$DiskIndex,

    [Parameter(Mandatory=$true)]
    [int]$Partition,

    [Parameter(Mandatory=$false)]
    [ValidateSet("ext4", "xfs", "btrfs", "vfat", "auto")]
    [string]$FsType = "auto",

    [Parameter(Mandatory=$false)]
    [string]$DistroName
)

# Function to output result and exit
function Write-Result {
    param(
        [bool]$Success,
        [string]$DistroName = "",
        [string]$MountPathLinux = "",
        [string]$MountPathUNC = "",
        [string]$ErrorMessage = ""
    )

    $output = @()

    if ($Success) {
        $output += "STATUS=OK"
        $output += "DistroName=$DistroName"
        $output += "MountPathLinux=$MountPathLinux"
        $output += "MountPathUNC=$MountPathUNC"
        $exitCode = 0
    } else {
        $output += "STATUS=ERROR"
        $output += "ErrorMessage=$ErrorMessage"
        $exitCode = 1
    }

    # Write to stdout
    $output | ForEach-Object { Write-Output $_ }

    # Also write to temp file for elevated scenarios where stdout can't be captured
    try {
        $tempFile = Join-Path $env:TEMP "limount_mount_${DiskIndex}_${Partition}.txt"
        $output | Out-File -FilePath $tempFile -Encoding UTF8 -Force
    } catch {
        # Ignore errors writing temp file
    }

    exit $exitCode
}

# Require Administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Result -Success $false -ErrorMessage "This script requires Administrator privileges."
}

# Validate DiskIndex
try {
    $disk = Get-Disk -Number $DiskIndex -ErrorAction Stop
} catch {
    Write-Result -Success $false -ErrorMessage "Disk $DiskIndex not found: $($_.Exception.Message)"
}

# Safety check: reject system/boot disks
if ($disk.IsSystem -or $disk.IsBoot) {
    Write-Result -Success $false -ErrorMessage "Disk $DiskIndex is a system or boot disk. Refusing to mount for safety."
}

# Validate partition number
if ($Partition -lt 1) {
    Write-Result -Success $false -ErrorMessage "Partition number must be 1 or greater."
}

# Build physical disk path
$diskPath = "\\.\PHYSICALDRIVE$DiskIndex"

# Execute wsl --mount
Write-Verbose "Mounting $diskPath partition $Partition as $FsType..."

try {
    # Build mount arguments - omit --type for "auto" to let WSL detect
    if ($FsType -eq "auto") {
        $mountArgs = @("--mount", $diskPath, "--partition", $Partition)
    } else {
        $mountArgs = @("--mount", $diskPath, "--partition", $Partition, "--type", $FsType)
    }

    $mountOutput = & wsl.exe @mountArgs 2>&1
    $mountExitCode = $LASTEXITCODE

    if ($mountExitCode -ne 0) {
        # Check for common error messages
        $errorText = $mountOutput -join " "

        if ($errorText -match "already mounted" -or $errorText -match "is in use") {
            Write-Verbose "Disk appears to already be mounted, continuing..."
        } elseif ($errorText -match "not recognized" -or $errorText -match "invalid option") {
            Write-Result -Success $false -ErrorMessage "wsl --mount is not supported. Ensure you are running Windows 11 Build 22000+ or Microsoft Store WSL."
        } else {
            Write-Result -Success $false -ErrorMessage "wsl --mount failed (exit code $mountExitCode): $errorText"
        }
    }
} catch {
    Write-Result -Success $false -ErrorMessage "Failed to execute wsl --mount: $($_.Exception.Message)"
}

# Determine distro name
Write-Verbose "Determining WSL distro..."

if (-not $DistroName) {
    # Try to enumerate distros via \\wsl$ share first
    try {
        $wslPath = '\\wsl$'
        if (Test-Path $wslPath) {
            $distros = Get-ChildItem -Path $wslPath -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
            if ($distros) {
                $DistroName = $distros | Select-Object -First 1
                Write-Verbose "Found distro via wsl`$: $DistroName"
            }
        }
    } catch {
        Write-Verbose "Could not enumerate wsl`$: $($_.Exception.Message)"
    }

    # Fallback: use wsl -l -q
    if (-not $DistroName) {
        try {
            $distroList = & wsl.exe -l -q 2>&1
            if ($LASTEXITCODE -eq 0 -and $distroList) {
                # Clean non-printable characters and take first non-empty line
                $cleanedDistros = $distroList | ForEach-Object {
                    $_.Trim() -replace '[^\x20-\x7E]', ''
                } | Where-Object { $_ }

                if ($cleanedDistros) {
                    $DistroName = $cleanedDistros | Select-Object -First 1
                    Write-Verbose "Found distro via wsl -l: $DistroName"
                }
            }
        } catch {
            Write-Verbose "Could not run wsl -l: $($_.Exception.Message)"
        }
    }

    if (-not $DistroName) {
        Write-Result -Success $false -ErrorMessage "Could not determine WSL distribution name."
    }
}

# Verify distro is accessible
try {
    $testOutput = & wsl.exe -d $DistroName -- pwd 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Result -Success $false -ErrorMessage "Cannot access WSL distribution '$DistroName': $testOutput"
    }
} catch {
    Write-Result -Success $false -ErrorMessage "Cannot verify WSL distribution '$DistroName': $($_.Exception.Message)"
}

# Determine mount path in WSL
Write-Verbose "Determining mount path..."

$baseMountPath = "/mnt/wsl"
$mountPathLinux = $baseMountPath
$expectedMountName = "PHYSICALDRIVE${DiskIndex}p${Partition}"

try {
    # List contents of /mnt/wsl to find the mounted disk
    $lsOutput = & wsl.exe -d $DistroName -- ls -1 $baseMountPath 2>&1
    if ($LASTEXITCODE -eq 0 -and $lsOutput) {
        $mountedDirs = $lsOutput | Where-Object { $_ }

        # Look for directory containing our disk name
        $foundMount = $mountedDirs | Where-Object { $_ -match "PHYSICALDRIVE$DiskIndex" } | Select-Object -First 1

        if ($foundMount) {
            $mountPathLinux = "$baseMountPath/$foundMount"
            Write-Verbose "Found mount at: $mountPathLinux"
        } else {
            # Fallback: assume standard naming
            $mountPathLinux = "$baseMountPath/$expectedMountName"
            Write-Verbose "Using expected mount path: $mountPathLinux"
        }
    } else {
        # Fallback: assume standard naming
        $mountPathLinux = "$baseMountPath/$expectedMountName"
        Write-Verbose "Could not ls /mnt/wsl, using expected path: $mountPathLinux"
    }
} catch {
    $mountPathLinux = "$baseMountPath/$expectedMountName"
    Write-Verbose "Error listing mounts, using expected path: $mountPathLinux"
}

# Build UNC path - use wsl.localhost instead of wsl$ for better Explorer compatibility
$mountPathUNC = "\\wsl.localhost\$DistroName$($mountPathLinux -replace '/', '\')"

Write-Verbose "UNC path: $mountPathUNC"

# Verify UNC path is accessible (optional, as it may take a moment to appear)
Start-Sleep -Milliseconds 500
if (Test-Path $mountPathUNC) {
    Write-Verbose "UNC path is accessible."
} else {
    Write-Verbose "Warning: UNC path not immediately accessible, but continuing..."
}

# Output success result
Write-Result -Success $true `
    -DistroName $DistroName `
    -MountPathLinux $mountPathLinux `
    -MountPathUNC $mountPathUNC
