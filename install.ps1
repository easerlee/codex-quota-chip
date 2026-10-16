# Copies this folder to %LOCALAPPDATA%\Programs\CodexQuotaChip and, if asked, registers autostart.
# No administrator rights needed, nothing is written outside the two places below.
param(
    [switch]$Autostart,
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\CodexQuotaChip')
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Get-ChildItem -Path $source -File | Copy-Item -Destination $Target -Force

$exe = Join-Path $Target 'CodexQuotaChip.exe'
Write-Host "installed: $exe"

if ($Autostart) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $runKey -Name 'CodexQuotaChip' -Value ('"' + $exe + '"')
    Write-Host 'autostart: enabled'
}

Write-Host ('start it with: ' + $exe)
Write-Host 'uninstall: run uninstall.ps1 in the same folder (or in the install folder)'
