# Builds the self-contained Windows package and zips it for a GitHub release.
param(
    [string]$Version = '0.1.0',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root "publish\$Runtime"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src\CodexQuotaChip\CodexQuotaChip.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $out

Copy-Item (Join-Path $root 'install.ps1') $out -Force
Copy-Item (Join-Path $root 'uninstall.ps1') $out -Force
Copy-Item (Join-Path $root 'README.md') $out -Force
Copy-Item (Join-Path $root 'LICENSE') $out -Force

$zip = Join-Path $root "publish\CodexQuotaChip-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip

Write-Host "exe : $(Join-Path $out 'CodexQuotaChip.exe')"
Write-Host "zip : $zip"
