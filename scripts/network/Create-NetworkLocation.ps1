<#
.SYNOPSIS
    Creates an Explorer Network Location shortcut under NetHood.

.PARAMETER Name
    Display name of the network location.

.PARAMETER TargetUNC
    UNC path target for the network location shortcut.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [string]$TargetUNC
)

function Write-Result {
    param(
        [bool]$Success,
        [string]$ErrorMessage = "",
        [string]$NetworkLocationName = "",
        [string]$NetworkLocationPath = "",
        [string]$TargetUNC = ""
    )

    if ($Success) {
        Write-Output "STATUS=OK"
        Write-Output "NetworkLocationName=$NetworkLocationName"
        Write-Output "NetworkLocationPath=$NetworkLocationPath"
        Write-Output "TargetUNC=$TargetUNC"
        exit 0
    }

    Write-Output "STATUS=ERROR"
    Write-Output "ErrorMessage=$ErrorMessage"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Name)) {
    Write-Result -Success $false -ErrorMessage "Name cannot be empty."
}

if ([string]::IsNullOrWhiteSpace($TargetUNC)) {
    Write-Result -Success $false -ErrorMessage "TargetUNC cannot be empty."
}

try {
    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
    $safeNameChars = $Name.Trim().ToCharArray() | ForEach-Object {
        if ($invalidChars -contains $_) { '_' } else { $_ }
    }
    $safeName = -join $safeNameChars
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        $safeName = "LiMount Mount"
    }

    $netHoodPath = Join-Path $env:APPDATA "Microsoft\Windows\Network Shortcuts"
    if (-not (Test-Path -Path $netHoodPath)) {
        New-Item -ItemType Directory -Path $netHoodPath -Force | Out-Null
    }

    $shortcutFolder = Join-Path $netHoodPath $safeName
    if (-not (Test-Path -Path $shortcutFolder)) {
        New-Item -ItemType Directory -Path $shortcutFolder -Force | Out-Null
    }

    $desktopIniPath = Join-Path $shortcutFolder "desktop.ini"
    $desktopIniContent = @"
[.ShellClassInfo]
CLSID2={0AFACED1-E828-11D1-9187-B532F1E9575D}
Flags=2
"@
    Set-Content -Path $desktopIniPath -Value $desktopIniContent -Encoding ASCII -Force

    $targetShortcutPath = Join-Path $shortcutFolder "target.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($targetShortcutPath)
    $shortcut.TargetPath = $TargetUNC
    $shortcut.Save()

    attrib +r "$shortcutFolder" | Out-Null
    attrib +h +s "$desktopIniPath" | Out-Null

    Write-Result -Success $true `
        -NetworkLocationName $safeName `
        -NetworkLocationPath $shortcutFolder `
        -TargetUNC $TargetUNC
}
catch {
    Write-Result -Success $false -ErrorMessage $_.Exception.Message
}

