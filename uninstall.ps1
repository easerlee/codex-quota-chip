# Removes the autostart entry and the installed folder. -Purge also removes the state folder
# (%LOCALAPPDATA%\CodexQuotaChip), which is where the account history lives.
param(
    [switch]$Purge,
    # The known folder, not $env:LOCALAPPDATA: that variable can be overridden by the caller and
    # would quietly remove something else.
    [string]$Target = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\CodexQuotaChip')
)

$ErrorActionPreference = 'Stop'

Get-Process CodexQuotaChip -ErrorAction SilentlyContinue | Stop-Process -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -Path $runKey -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains 'CodexQuotaChip') {
    Remove-ItemProperty -Path $runKey -Name 'CodexQuotaChip'
    Write-Host 'autostart: removed'
}

if (Test-Path $Target) {
    Remove-Item $Target -Recurse -Force
    Write-Host "removed: $Target"
}

if ($Purge) {
    $state = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexQuotaChip'
    if (Test-Path $state) {
        Remove-Item $state -Recurse -Force
        Write-Host "removed state: $state"
    }
}
