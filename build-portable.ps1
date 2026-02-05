# Simple Build Script - Creates a portable, no-install-required version
# Users can just extract and run - no admin rights needed

param(
    [string]$Version = "1.0.0"
)

Write-Host "Building Video Time Study v$Version (Portable Version)..." -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\Releases") {
    Remove-Item ".\Releases" -Recurse -Force
}

# Create output directory
New-Item -ItemType Directory -Path ".\Releases" -Force | Out-Null

# Publish self-contained application
Write-Host "Publishing self-contained application..." -ForegroundColor Yellow
$publishPath = Join-Path $PSScriptRoot "Releases\VideoTimeStudy-v$Version"
& dotnet publish VideoTimeStudy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o "`"$publishPath`""

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Create a simple batch file to launch the app
$launchScript = @"
@echo off
start "" "%~dp0VideoTimeStudy.exe"
"@
Set-Content -Path ".\Releases\VideoTimeStudy-v$Version\Launch Video Time Study.bat" -Value $launchScript

# Create a README
$readme = @"
Video Time Study - Portable Version v$Version
==============================================

INSTALLATION:
1. Extract this folder anywhere on your computer
2. No installation or admin rights required!

TO RUN:
- Double-click "Launch Video Time Study.bat" or "VideoTimeStudy.exe"

FEATURES:
✓ Video playback with timeline controls
✓ Timestamp marking with descriptions
✓ Excel-like data grid for time study entries
✓ CSV export functionality
✓ Person detection and tracking (YOLO models included)

SYSTEM REQUIREMENTS:
- Windows 10 or later
- .NET 10.0 Runtime (included - self-contained)

For support, contact: Nortek

© $((Get-Date).Year) Nortek, Inc.
"@
Set-Content -Path ".\Releases\VideoTimeStudy-v$Version\README.txt" -Value $readme

# Create a ZIP file
Write-Host "Creating ZIP package..." -ForegroundColor Yellow
$zipPath = ".\Releases\VideoTimeStudy-v$Version-Portable.zip"
Compress-Archive -Path ".\Releases\VideoTimeStudy-v$Version\*" -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "SUCCESS! Portable version created!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Package location:" -ForegroundColor Cyan
Write-Host "$zipPath" -ForegroundColor Yellow
Write-Host ""
Write-Host "Also available as folder:" -ForegroundColor Cyan
Write-Host ".\Releases\VideoTimeStudy-v$Version\" -ForegroundColor Yellow
Write-Host ""
Write-Host "This portable version:" -ForegroundColor Cyan
Write-Host "  Does NOT require installation" -ForegroundColor Green
Write-Host "  Does NOT require admin rights" -ForegroundColor Green
Write-Host "  Can run from any folder (USB drive, network share, etc.)" -ForegroundColor Green
Write-Host "  Self-contained (includes .NET runtime)" -ForegroundColor Green
Write-Host "  Includes all dependencies and ONNX models" -ForegroundColor Green
Write-Host ""
Write-Host "To share: Send the ZIP file to users!" -ForegroundColor Cyan

# Calculate size
$size = (Get-Item $zipPath).Length / 1MB
$sizeRounded = [math]::Round($size, 2)
Write-Host ""
Write-Host "Package size: $sizeRounded MB" -ForegroundColor Yellow
