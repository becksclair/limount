<#
.SYNOPSIS
    Runs LiMount hardware-in-loop mount integration tests with preflight checks.

.DESCRIPTION
    This script is intended for local Windows + WSL testing against a real disk.
    It validates prerequisites, sets environment variables, runs the integration test,
    and prints concise triage output.

.PARAMETER DiskIndex
    Physical disk index to test.

.PARAMETER Partition
    Partition number on the disk to test for single-scenario mode.

.PARAMETER ExpectXfsUnsupported
    In single-scenario mode, set when the target partition is expected to fail due to unsupported XFS features in WSL.

.PARAMETER VerifyDriveEndToEnd
    Runs a real-drive verification workflow:
    1) Optional expected-failure scenario on -FailurePartition
    2) Automatic search for a mountable partition on the same disk that must pass mount+unmount.

.PARAMETER FailurePartition
    Partition number expected to fail (for example unsupported XFS root partition) when -VerifyDriveEndToEnd is used.

.PARAMETER SuccessPartitions
    Optional explicit partition list to try for success in -VerifyDriveEndToEnd mode.

.PARAMETER SkipScriptElevation
    Bypass the app's default elevated script execution path (UAC) by setting
    LIMOUNT_SKIP_SCRIPT_ELEVATION=1. Use this only in environments where
    direct non-elevated `wsl --mount` is allowed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$DiskIndex,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 128)]
    [int]$Partition = 0,

    [Parameter(Mandatory = $false)]
    [switch]$ExpectXfsUnsupported,

    [Parameter(Mandatory = $false)]
    [switch]$VerifyDriveEndToEnd,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 128)]
    [int]$FailurePartition = 0,

    [Parameter(Mandatory = $false)]
    [int[]]$SuccessPartitions,

    [Parameter(Mandatory = $false)]
    [switch]$SkipScriptElevation
)

function Invoke-HilScenario {
    param(
        [Parameter(Mandatory = $true)]
        [int]$PartitionNumber,
        [Parameter(Mandatory = $true)]
        [bool]$ExpectUnsupported,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    Write-Host "`n== $Label ==" -ForegroundColor Cyan
    Write-Host "DiskIndex: $DiskIndex, Partition: $PartitionNumber, ExpectXfsUnsupported: $ExpectUnsupported"

    $env:LIMOUNT_TEST_DISK_INDEX = $DiskIndex.ToString()
    $env:LIMOUNT_TEST_PARTITION = $PartitionNumber.ToString()
    $env:LIMOUNT_EXPECT_XFS_UNSUPPORTED = if ($ExpectUnsupported) { "1" } else { "0" }
    $env:LIMOUNT_REQUIRE_HIL = "1"
    $env:LIMOUNT_SKIP_SCRIPT_ELEVATION = if ($SkipScriptElevation.IsPresent) { "1" } else { "0" }

    $testCmd = @(
        "test",
        "LiMount.Tests",
        "--filter", "FullyQualifiedName~LiMount.Tests.Integration.MountIntegrationTests.FullMountWorkflow_MountsListsAndUnmounts",
        "--logger", "console;verbosity=detailed"
    )

    & dotnet @testCmd
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "$Label passed." -ForegroundColor Green
    } else {
        Write-Host "$Label failed." -ForegroundColor Red
    }

    return [PSCustomObject]@{
        Partition = $PartitionNumber
        ExitCode = $exitCode
        Label = $Label
    }
}

function Get-CandidatePartitions {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Disk
    )

    if ($SuccessPartitions -and $SuccessPartitions.Count -gt 0) {
        return $SuccessPartitions | Where-Object { $_ -gt 0 } | Sort-Object -Unique
    }

    try {
        $partitions = Get-Partition -DiskNumber $Disk -ErrorAction Stop |
            Select-Object -ExpandProperty PartitionNumber
        return $partitions | Where-Object { $_ -gt 0 } | Sort-Object -Unique
    } catch {
        Write-Warning "Get-Partition failed, falling back to Win32_DiskPartition. Error: $($_.Exception.Message)"
    }

    $fallback = Get-CimInstance Win32_DiskPartition -Filter ("DiskIndex = {0}" -f $Disk) |
        ForEach-Object {
            if ($_.DeviceID -match 'Partition #(\d+)') {
                [int]$matches[1] + 1
            } elseif ($_.Index -ge 0) {
                [int]$_.Index + 1
            }
        } |
        Where-Object { $_ -gt 0 } |
        Sort-Object -Unique

    return $fallback
}

$scriptExitCode = 1

try {
    Write-Host "== LiMount HIL Mount Test ==" -ForegroundColor Cyan
    Write-Host "DiskIndex: $DiskIndex"

    $statusOutput = & wsl.exe --status 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "WSL is not available. Output: $statusOutput"
    }

    if ($VerifyDriveEndToEnd.IsPresent) {
        if ($FailurePartition -gt 0) {
            $failureResult = Invoke-HilScenario -PartitionNumber $FailurePartition -ExpectUnsupported $true -Label "Expected-failure scenario"
            if ($failureResult.ExitCode -ne 0) {
                throw "Expected-failure scenario did not pass for partition $FailurePartition."
            }
        }

        $candidates = Get-CandidatePartitions -Disk $DiskIndex
        if ($FailurePartition -gt 0) {
            $candidates = $candidates | Where-Object { $_ -ne $FailurePartition }
        }

        if (-not $candidates -or $candidates.Count -eq 0) {
            throw "No candidate partitions available for success validation on disk $DiskIndex."
        }

        Write-Host "Success candidates: $($candidates -join ', ')"
        foreach ($candidate in $candidates) {
            $result = Invoke-HilScenario -PartitionNumber $candidate -ExpectUnsupported $false -Label "Success scenario (partition $candidate)"
            if ($result.ExitCode -eq 0) {
                Write-Host "Drive-level verification passed. Mount+unmount succeeded on partition $candidate." -ForegroundColor Green
                $scriptExitCode = 0
                break
            }
        }

        if ($scriptExitCode -ne 0) {
            throw "Drive-level verification failed: no candidate partition passed mount+unmount."
        }
    }
    else {
        if ($Partition -le 0) {
            throw "Specify -Partition for single-scenario mode, or use -VerifyDriveEndToEnd."
        }

        $singleResult = Invoke-HilScenario -PartitionNumber $Partition -ExpectUnsupported $ExpectXfsUnsupported.IsPresent -Label "Single scenario"
        $scriptExitCode = $singleResult.ExitCode
    }
}
catch {
    Write-Error $_.Exception.Message
    $scriptExitCode = 1
}
finally {
    Remove-Item Env:\LIMOUNT_TEST_DISK_INDEX -ErrorAction SilentlyContinue
    Remove-Item Env:\LIMOUNT_TEST_PARTITION -ErrorAction SilentlyContinue
    Remove-Item Env:\LIMOUNT_EXPECT_XFS_UNSUPPORTED -ErrorAction SilentlyContinue
    Remove-Item Env:\LIMOUNT_REQUIRE_HIL -ErrorAction SilentlyContinue
    Remove-Item Env:\LIMOUNT_SKIP_SCRIPT_ELEVATION -ErrorAction SilentlyContinue
}

exit $scriptExitCode
