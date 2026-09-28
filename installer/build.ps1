# Buduje instalator MeshForge od zera: publikuje appkę (self-contained, jeden plik .exe),
# potem kompiluje installer/Setup.iss do installer_output/MeshForge-Setup-<wersja>.exe.
#
# Wymaga: .NET 9 SDK oraz Inno Setup 6 (https://jrsoftware.org/isdl.php, albo `winget install JRSoftware.InnoSetup`).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== Publikowanie MeshForge (self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish src/MeshForge.App/MeshForge.App.vbproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiodło się." }

$iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", `
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $iscc) { throw "Nie znaleziono ISCC.exe - zainstaluj Inno Setup 6 (winget install JRSoftware.InnoSetup)." }

Write-Host "== Kompilowanie instalatora ($iscc) ==" -ForegroundColor Cyan
& $iscc.FullName "installer\Setup.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC nie powiodło się." }

Write-Host "Gotowe: installer_output\" -ForegroundColor Green
