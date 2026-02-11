<#
.SYNOPSIS
    Maps a WSL UNC path to a Windows drive letter.

.DESCRIPTION
    This script maps a UNC path (e.g., \\wsl$\Ubuntu\mnt\wsl\...) to a Windows
    drive letter using `subst` for WSL paths and `net use`/`New-PSDrive` for
    non-WSL paths.

    Outputs machine-readable key=value pairs for consumption by the C# app.

.PARAMETER DriveLetter
    The drive letter to map (e.g., "L" or "L:"). Will be normalized to uppercase.

.PARAMETER TargetUNC
    The UNC path to map (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$DriveLetter,

    [Parameter(Mandatory=$true)]
    [string]$TargetUNC
)

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
    }

    Write-Output "STATUS=ERROR"
    Write-Output "ErrorMessage=$ErrorMessage"
    exit 1
}

function Normalize-UncPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $normalized = $Path.Trim().Trim('"')
    $normalized = $normalized -replace '/', '\'
    $normalized = $normalized -replace '^(?i)\\\\\?\\UNC\\', '\'
    $normalized = $normalized -replace '^(?i)UNC\\', '\'

    if ($normalized -match '^\\[^\\]') {
        $normalized = "\" + $normalized
    }

    return $normalized.TrimEnd('\\')
}

function Test-UncPathWithTimeout {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [int]$TimeoutMs = 12000,

        [int]$ProbeDelayMs = 500
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (Test-Path -Path $Path -ErrorAction SilentlyContinue) {
            return $true
        }

        Start-Sleep -Milliseconds $ProbeDelayMs
    }

    return $false
}

function Get-SubstTargetForDriveLetter {
    param([Parameter(Mandatory=$true)][string]$DriveLetter)

    try {
        $substOutput = subst 2>&1
        foreach ($line in $substOutput) {
            if ($line -match "^${DriveLetter}:\\: => (.+)$") {
                return $Matches[1].Trim()
            }
        }
    } catch {
        Write-Verbose "Failed reading subst mappings: $($_.Exception.Message)"
    }

    return $null
}

function Invoke-ScheduledTask {
    param(
        [Parameter(Mandatory=$true)]
        [string]$TaskName,

        [Parameter(Mandatory=$true)]
        [string]$Action,

        [Parameter(Mandatory=$true)]
        [string]$RunAsUser,

        [int]$PostRunDelayMs = 1200
    )

    try {
        $createOutput = schtasks /create /tn $TaskName /tr $Action /sc once /st 00:00 /ru $RunAsUser /f 2>&1
        $createExitCode = $LASTEXITCODE
        if ($createExitCode -ne 0) {
            return @{
                Success = $false
                Error = "Failed creating scheduled task (exit $createExitCode): $($createOutput -join ' ')"
            }
        }

        $runOutput = schtasks /run /tn $TaskName 2>&1
        $runExitCode = $LASTEXITCODE
        if ($runExitCode -ne 0) {
            return @{
                Success = $false
                Error = "Failed starting scheduled task (exit $runExitCode): $($runOutput -join ' ')"
            }
        }

        Start-Sleep -Milliseconds $PostRunDelayMs

        return @{ Success = $true }
    }
    finally {
        schtasks /delete /tn $TaskName /f 2>&1 | Out-Null
    }
}

function Get-UserContextSubstTarget {
    param(
        [Parameter(Mandatory=$true)]
        [string]$DriveLetter,

        [Parameter(Mandatory=$true)]
        [string]$RunAsUser
    )

    $tempFile = Join-Path $env:TEMP ("limount_subst_query_{0}.txt" -f ([Guid]::NewGuid().ToString("N")))
    $taskName = "LiMount_Query_$DriveLetter`_$(Get-Random)"
    $taskAction = "cmd /c subst > `"$tempFile`""

    try {
        $taskResult = Invoke-ScheduledTask -TaskName $taskName -Action $taskAction -RunAsUser $RunAsUser
        if (-not $taskResult.Success) {
            Write-Verbose $taskResult.Error
            return $null
        }

        $isFileReady = Test-UncPathWithTimeout -Path $tempFile -TimeoutMs 5000 -ProbeDelayMs 250
        if (-not $isFileReady) {
            Write-Verbose "User-context subst query output file was not produced in time."
            return $null
        }

        $lines = Get-Content -Path $tempFile -ErrorAction SilentlyContinue
        foreach ($line in $lines) {
            if ($line -match "^${DriveLetter}:\\: => (.+)$") {
                return $Matches[1].Trim()
            }
        }

        return $null
    }
    finally {
        if (Test-Path $tempFile) {
            Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

$DriveLetter = $DriveLetter.Trim().ToUpperInvariant() -replace ':', ''
if ($DriveLetter.Length -ne 1 -or $DriveLetter -notmatch '^[A-Z]$') {
    Write-Result -Success $false -ErrorMessage "Invalid drive letter: $DriveLetter. Must be A-Z."
}

if ([string]::IsNullOrWhiteSpace($TargetUNC)) {
    Write-Result -Success $false -ErrorMessage "TargetUNC cannot be empty."
}

$TargetUNC = $TargetUNC.Trim()
$normalizedTargetUNC = Normalize-UncPath -Path $TargetUNC
$isWslPath = $TargetUNC -match '^\\\\wsl(?:\$|\.localhost)\\'

if ($isWslPath -and $TargetUNC -notmatch '^\\\\wsl(?:\$|\.localhost)\\[^\\]+\\mnt\\wsl\\[^\\]+') {
    Write-Result -Success $false -ErrorMessage "WSL path must target a concrete mount under \\wsl$ or \\wsl.localhost (for example \\wsl.localhost\\Ubuntu\\mnt\\wsl\\PHYSICALDRIVE2p1)."
}

$uncProbeTimeoutMs = 12000
$uncProbeDelayMs = 500
if (-not (Test-UncPathWithTimeout -Path $TargetUNC -TimeoutMs $uncProbeTimeoutMs -ProbeDelayMs $uncProbeDelayMs)) {
    Write-Result -Success $false -ErrorMessage "UNC path is not reachable within ${uncProbeTimeoutMs}ms: $TargetUNC"
}

$driveWithColon = "${DriveLetter}:"

$existingSubstTarget = Get-SubstTargetForDriveLetter -DriveLetter $DriveLetter
if ($existingSubstTarget) {
    if ((Normalize-UncPath $existingSubstTarget) -ieq $normalizedTargetUNC) {
        Write-Verbose "Drive $driveWithColon is already mapped to the same target via subst."
        Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
    }

    Write-Result -Success $false -ErrorMessage "Drive letter $driveWithColon is already mapped via subst to $existingSubstTarget."
}

$existingDrive = $null
try {
    $existingDrive = Get-PSDrive -Name $DriveLetter -ErrorAction SilentlyContinue
} catch {
    $existingDrive = $null
}

if ($existingDrive) {
    $existingRoot = Normalize-UncPath $existingDrive.Root
    if ($existingRoot -ieq $normalizedTargetUNC) {
        Write-Verbose "Drive $driveWithColon is already mapped to the same target via PSDrive."
        Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
    }

    Write-Result -Success $false -ErrorMessage "Drive letter $driveWithColon is already in use (mapped to $($existingDrive.Root))."
}

if ($isWslPath) {
    Write-Verbose "Detected WSL path, using subst command."

    $isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isElevated) {
        if ($TargetUNC -match '[\r\n"''&|<>^%]') {
            Write-Result -Success $false -ErrorMessage "TargetUNC contains unsafe characters that cannot be used in elevated scheduled-task mapping."
        }

        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $mapTaskName = "LiMount_Map_$DriveLetter`_$(Get-Random)"
        $mapAction = "cmd /c subst $driveWithColon `"$TargetUNC`""

        $mapTaskResult = Invoke-ScheduledTask -TaskName $mapTaskName -Action $mapAction -RunAsUser $currentUser
        if (-not $mapTaskResult.Success) {
            Write-Result -Success $false -ErrorMessage $mapTaskResult.Error
        }

        $mappedTarget = Get-UserContextSubstTarget -DriveLetter $DriveLetter -RunAsUser $currentUser
        if (-not $mappedTarget) {
            Write-Result -Success $false -ErrorMessage "Mapping verification failed: drive $driveWithColon is not visible in user context after elevated mapping."
        }

        if ((Normalize-UncPath $mappedTarget) -ine $normalizedTargetUNC) {
            Write-Result -Success $false -ErrorMessage "Mapping verification failed: drive $driveWithColon points to '$mappedTarget' instead of '$TargetUNC'."
        }

        Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
    }

    try {
        subst $driveWithColon $TargetUNC 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Result -Success $false -ErrorMessage "subst command failed with exit code $LASTEXITCODE while mapping $driveWithColon to $TargetUNC."
        }
    } catch {
        Write-Result -Success $false -ErrorMessage "Failed to map drive with subst: $($_.Exception.Message)"
    }

    Start-Sleep -Milliseconds 300
    $mappedTarget = Get-SubstTargetForDriveLetter -DriveLetter $DriveLetter
    if (-not $mappedTarget) {
        Write-Result -Success $false -ErrorMessage "Mapping verification failed: drive $driveWithColon is not present in subst output."
    }

    if ((Normalize-UncPath $mappedTarget) -ine $normalizedTargetUNC) {
        Write-Result -Success $false -ErrorMessage "Mapping verification failed: drive $driveWithColon points to '$mappedTarget' instead of '$TargetUNC'."
    }

    if (-not (Test-Path "${driveWithColon}\\" -ErrorAction SilentlyContinue)) {
        Write-Result -Success $false -ErrorMessage "Mapping verification failed: ${driveWithColon}\\ is not accessible."
    }

    Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
}

Write-Verbose "Mapping $driveWithColon to $TargetUNC using net use..."

try {
    $netUseResult = net use $driveWithColon $TargetUNC 2>&1
    $netUseExitCode = $LASTEXITCODE

    if ($netUseExitCode -eq 0 -and (Test-Path "${driveWithColon}\\")) {
        Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
    }

    Write-Verbose "net use did not produce an accessible mapping (exit $netUseExitCode): $netUseResult"
} catch {
    Write-Verbose "net use threw exception: $($_.Exception.Message)"
}

Write-Verbose "Trying New-PSDrive fallback..."

try {
    Remove-PSDrive -Name $DriveLetter -Force -ErrorAction SilentlyContinue | Out-Null

    $newDrive = New-PSDrive -Name $DriveLetter -PSProvider FileSystem -Root $TargetUNC -Persist -Scope Global -ErrorAction Stop
    if (-not $newDrive) {
        Write-Result -Success $false -ErrorMessage "New-PSDrive returned null."
    }

    if (-not (Test-Path "${driveWithColon}\\" -ErrorAction SilentlyContinue)) {
        Write-Result -Success $false -ErrorMessage "New-PSDrive succeeded but path ${driveWithColon}\\ is not accessible."
    }

    Write-Result -Success $true -DriveLetter $DriveLetter -MappedTo $TargetUNC
} catch {
    Write-Result -Success $false -ErrorMessage "Failed to map drive with New-PSDrive: $($_.Exception.Message)"
}
