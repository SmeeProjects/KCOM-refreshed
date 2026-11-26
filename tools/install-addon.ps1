<#
.SYNOPSIS
    Installs KCOM's game addon (kiwimp_alyx) into Half-Life: Alyx (UPGRADE_PLAN.md W9).

.DESCRIPTION
    Finds Half-Life: Alyx through Steam's libraryfolders.vdf, then replaces
    game\hlvr_addons\kiwimp_alyx with a copy of the addon. KCOM's "File > Install Addon"
    menu item does the same. An addon folder linked to a clone (README "Symlinks") is left alone.

.PARAMETER GamePath
    The Half-Life Alyx folder (the one holding "game"). Found through Steam when left out.

.PARAMETER Source
    The addon folder to copy. Defaults to the repository's WorkshopAddon\game\hlvr_addons\kiwimp_alyx,
    or to addon\kiwimp_alyx next to this script (a KCOM release).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install-addon.ps1

.EXAMPLE
    pwsh tools/install-addon.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Half-Life Alyx' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GamePath,
    [string]$Source
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$AppId = '546560'
$AddonName = 'kiwimp_alyx'

function Get-SteamDirectories {
    $directories = @()
    $user = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue
    if ($user) { $directories += $user.SteamPath -replace '/', '\' }
    $machine = Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue
    if ($machine) { $directories += $machine.InstallPath }
    if (${env:ProgramFiles(x86)}) { $directories += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    return $directories
}

# The "path" values of libraryfolders.vdf, or the numbered keys of its older format. The sizes
# under "apps" also have numbered keys: their values are numbers, not paths.
function Get-LibraryFolders([string]$Vdf) {
    $folders = @()
    foreach ($match in [regex]::Matches($Vdf, '"(?:path|\d+)"\s+"((?:[^"\\]|\\.)*)"', 'IgnoreCase')) {
        $value = $match.Groups[1].Value -replace '\\(.)', '$1'
        if ($value -notmatch '^\d*$') { $folders += $value }
    }
    return $folders
}

function Test-GameDirectory([string]$Path) {
    return Test-Path -LiteralPath (Join-Path (Join-Path $Path 'game') 'hlvr') -PathType Container
}

function Find-GameDirectory {
    foreach ($steam in Get-SteamDirectories) {
        $libraries = @($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $libraries += Get-LibraryFolders (Get-Content -LiteralPath $vdf -Raw)
        }
        foreach ($library in $libraries) {
            $steamapps = Join-Path $library 'steamapps'
            $installDir = 'Half-Life Alyx'
            $manifest = Join-Path $steamapps "appmanifest_$AppId.acf"
            if (Test-Path -LiteralPath $manifest) {
                $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"]+)"', 'IgnoreCase')
                if ($match.Success) { $installDir = $match.Groups[1].Value }
            }
            $game = Join-Path (Join-Path $steamapps 'common') $installDir
            if (Test-GameDirectory $game) { return $game }
        }
    }
    return $null
}

function Test-ContainsLink([string]$Path) {
    $reparse = [IO.FileAttributes]::ReparsePoint
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band $reparse) { return $true }
    foreach ($item in Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue) {
        return $true
    }
    return $false
}

# "Run with PowerShell" (started from Explorer) closes the window when the script ends: keep it open
# so the player can read the result.
function Wait-IfStartedFromExplorer {
    try {
        $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId = $PID").ParentProcessId
        if ((Get-Process -Id $parentId).ProcessName -eq 'explorer') {
            Read-Host 'Press Enter to close this window' | Out-Null
        }
    } catch {
        # Not on Windows, or the parent is gone: nothing to wait for.
    }
}

trap {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Wait-IfStartedFromExplorer
    exit 1
}

if (-not $Source) {
    $candidates = @(
        (Join-Path $PSScriptRoot "..\WorkshopAddon\game\hlvr_addons\$AddonName"),
        (Join-Path $PSScriptRoot "addon\$AddonName")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'addoninfo.txt')) { $Source = $candidate; break }
    }
}
if (-not $Source -or -not (Test-Path -LiteralPath (Join-Path $Source 'addoninfo.txt'))) {
    throw "No addon found. Pass -Source with the $AddonName folder (the one holding addoninfo.txt)."
}
$Source = (Resolve-Path -LiteralPath $Source).Path

if (-not $GamePath) {
    $GamePath = Find-GameDirectory
    if (-not $GamePath) {
        throw "Half-Life: Alyx wasn't found in your Steam libraries. Pass -GamePath with its folder, for example 'C:\Program Files (x86)\Steam\steamapps\common\Half-Life Alyx'."
    }
}
if (-not (Test-GameDirectory $GamePath)) {
    throw "$GamePath is not a Half-Life: Alyx folder (it has no game\hlvr folder)."
}

$addons = Join-Path (Join-Path $GamePath 'game') 'hlvr_addons'
$target = Join-Path $addons $AddonName
if (Test-Path -LiteralPath $target) {
    if (Test-ContainsLink $target) {
        throw "$target is linked to another folder (a development setup), so it was left as is."
    }
    if ($PSCmdlet.ShouldProcess($target, 'Remove the old addon')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
if ($PSCmdlet.ShouldProcess($target, "Copy the addon from $Source")) {
    New-Item -ItemType Directory -Path $addons -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Recurse
    Write-Host "Installed the KCOM addon to $target"
}
Wait-IfStartedFromExplorer
