<#
.SYNOPSIS
    Removes an Explorer Network Location shortcut from NetHood.

.PARAMETER Name
    Display name of the network location.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Name
)

function Write-Result {
    param(
        [bool]$Success,
        [string]$ErrorMessage = "",
        [string]$NetworkLocationName = ""
    )

    if ($Success) {
        Write-Output "STATUS=OK"
        Write-Output "NetworkLocationName=$NetworkLocationName"
        exit 0
    }

    Write-Output "STATUS=ERROR"
    Write-Output "ErrorMessage=$ErrorMessage"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Name)) {
    Write-Result -Success $false -ErrorMessage "Name cannot be empty."
}

try {
    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
    $safeNameChars = $Name.Trim().ToCharArray() | ForEach-Object {
        if ($invalidChars -contains $_) { '_' } else { $_ }
    }
    $safeName = -join $safeNameChars
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        Write-Result -Success $true -NetworkLocationName ""
    }

    $netHoodPath = Join-Path $env:APPDATA "Microsoft\Windows\Network Shortcuts"
    $shortcutFolder = Join-Path $netHoodPath $safeName

    if (Test-Path -Path $shortcutFolder) {
        Remove-Item -Path $shortcutFolder -Force -Recurse -ErrorAction Stop
    }

    Write-Result -Success $true -NetworkLocationName $safeName
}
catch {
    Write-Result -Success $false -ErrorMessage $_.Exception.Message
}

