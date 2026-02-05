# Create Portable Package for Video Time Study
# No admin rights required for users!

param(
    [string]$Version = "1.0.0"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Video Time Study - Portable Package Builder" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
$publishDir = "bin\PublishOutput"
$releasesDir = "Releases"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $releasesDir) {
    Remove-Item $releasesDir -Recurse -Force
}

# Publish self-contained application
Write-Host "[2/5] Publishing self-contained application..." -ForegroundColor Yellow
Write-Host "      (This may take a few minutes...)" -ForegroundColor Gray

dotnet publish VideoTimeStudy.csproj `
    /p:Configuration=Release `
    /p:RuntimeIdentifier=win-x64 `
    /p:SelfContained=true `
    /p:PublishDir=$publishDir `
    /p:PublishReadyToRun=true

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "      Build completed successfully!" -ForegroundColor Green

# Create release directory structure
Write-Host "[3/5] Organizing files..." -ForegroundColor Yellow
$appDir = "$releasesDir\VideoTimeStudy-v$Version"
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

# Copy published files
Copy-Item -Path "$publishDir\*" -Destination $appDir -Recurse -Force

# Create launch batch file
$launchScript = @"
@echo off
REM Video Time Study Launcher
start "" "%~dp0VideoTimeStudy.exe"
"@
Set-Content -Path "$appDir\Launch Video Time Study.bat" -Value $launchScript

# Create README
$readme = @"
========================================
Video Time Study - Portable Version $Version
========================================

QUICK START:
1. No installation needed!
2. Double-click "Launch Video Time Study.bat" or "VideoTimeStudy.exe"
3. Start analyzing videos!

FEATURES:
  * Video playback with precise timeline controls
  * Timestamp marking with descriptions
  * Excel-like data grid for time study entries
  * CSV export functionality
  * Person detection and tracking (YOLO models)
  * Motion tracking and analysis

REQUIREMENTS:
  * Windows 10 or later (64-bit)
  * ~500MB disk space
  * Video files (MP4, AVI, MKV, WMV supported)

PORTABLE:
  This is a self-contained application. You can:
  * Run from any location (Desktop, USB drive, network share)
  * Copy to multiple computers
  * Run without administrator rights
  * Run without installing .NET (included)

USAGE TIPS:
  * Place video files in an accessible folder
  * Export results to CSV for further analysis
  * Use keyboard shortcuts for faster workflow
  * YOLO models enable automatic person detection

SUPPORT:
  For questions or issues, contact: Nortek, Inc.

========================================
© $((Get-Date).Year) Nortek, Inc.
All rights reserved.
========================================
"@
Set-Content -Path "$appDir\README.txt" -Value $readme

# Create a CHANGELOG
$changelog = @"
Video Time Study - Change Log
==============================

Version $Version
-----------------
* Initial portable release
* Self-contained deployment (no installation required)
* All dependencies included
* YOLO models for person detection
* CSV export functionality
* Video playback and timeline controls

"@
Set-Content -Path "$appDir\CHANGELOG.txt" -Value $changelog

# Create ZIP package
Write-Host "[4/5] Creating ZIP package..." -ForegroundColor Yellow
$zipFile = "$releasesDir\VideoTimeStudy-v$Version-Portable.zip"
Compress-Archive -Path "$appDir\*" -DestinationPath $zipFile -Force

Write-Host "      ZIP package created!" -ForegroundColor Green

# Calculate sizes
Write-Host "[5/5] Finalizing..." -ForegroundColor Yellow
$zipSize = (Get-Item $zipFile).Length / 1MB
$folderSize = (Get-ChildItem $appDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "SUCCESS! Package created!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "PACKAGE LOCATIONS:" -ForegroundColor Cyan
Write-Host "  ZIP File: " -NoNewline; Write-Host "$zipFile" -ForegroundColor Yellow
Write-Host "  Folder:   " -NoNewline; Write-Host "$appDir\" -ForegroundColor Yellow
Write-Host ""
Write-Host "PACKAGE SIZES:" -ForegroundColor Cyan
Write-Host "  ZIP File: " -NoNewline; Write-Host "$([math]::Round($zipSize, 1)) MB" -ForegroundColor Yellow
Write-Host "  Extracted: " -NoNewline; Write-Host "$([math]::Round($folderSize, 1)) MB" -ForegroundColor Yellow
Write-Host ""
Write-Host "DEPLOYMENT OPTIONS:" -ForegroundColor Cyan
Write-Host "  1. Share the ZIP file ($([math]::Round($zipSize, 1)) MB)" -ForegroundColor White
Write-Host "     Users extract and run - that's it!" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Share the entire folder" -ForegroundColor White
Write-Host "     Copy to USB drive, network share, etc." -ForegroundColor Gray
Write-Host ""
Write-Host "KEY FEATURES:" -ForegroundColor Cyan
Write-Host "  [X] No installation required" -ForegroundColor Green
Write-Host "  [X] No admin rights required" -ForegroundColor Green
Write-Host "  [X] Self-contained (.NET included)" -ForegroundColor Green
Write-Host "  [X] All dependencies included" -ForegroundColor Green
Write-Host "  [X] ONNX models included" -ForegroundColor Green
Write-Host "  [X] Portable (run from anywhere)" -ForegroundColor Green
Write-Host ""
Write-Host "READY TO SHARE!" -ForegroundColor Green
Write-Host ""
