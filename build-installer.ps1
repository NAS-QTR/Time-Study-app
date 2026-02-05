# Build and Package Video Time Study Installer
# This creates a no-admin-required installer using Squirrel

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = ".\Releases"
)

Write-Host "Building Video Time Study v$Version..." -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\bin\Release") {
    Remove-Item ".\bin\Release" -Recurse -Force
}
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

# Build the application in Release mode
Write-Host "Building application..." -ForegroundColor Yellow
dotnet build VideoTimeStudy.csproj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Publish self-contained application for Windows
Write-Host "Publishing self-contained application..." -ForegroundColor Yellow
dotnet publish VideoTimeStudy.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -o ".\bin\Publish"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Create output directory
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Package with Squirrel
Write-Host "Creating installer with Squirrel..." -ForegroundColor Yellow

# Download squirrel tool if not present
$squirrelExe = "$OutputDir\squirrel.exe"
if (!(Test-Path $squirrelExe)) {
    Write-Host "Downloading Squirrel..." -ForegroundColor Yellow
    dotnet tool install --global Clowd.Squirrel
}

# Use the squirrel command line tool
Write-Host "Packaging application..." -ForegroundColor Yellow
squirrel pack `
    --packId "VideoTimeStudy" `
    --packVersion $Version `
    --packDirectory ".\bin\Publish" `
    --releaseDir $OutputDir `
    --icon ".\bin\Publish\VideoTimeStudy.exe" `
    --packTitle "Video Time Study" `
    --packAuthors "Nortek" `
    --noDelta

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n==================================" -ForegroundColor Green
    Write-Host "SUCCESS! Installer created!" -ForegroundColor Green
    Write-Host "==================================" -ForegroundColor Green
    Write-Host "`nInstaller location:" -ForegroundColor Cyan
    Write-Host "$OutputDir\VideoTimeStudySetup.exe" -ForegroundColor Yellow
    Write-Host "`nThis installer:" -ForegroundColor Cyan
    Write-Host "  ✓ Does NOT require admin rights" -ForegroundColor Green
    Write-Host "  ✓ Installs to user's AppData folder" -ForegroundColor Green
    Write-Host "  ✓ Creates Start Menu shortcuts" -ForegroundColor Green
    Write-Host "  ✓ Includes uninstaller" -ForegroundColor Green
    Write-Host "`nShare the Setup.exe file with users!" -ForegroundColor Cyan
} else {
    Write-Host "`nPackaging failed. Trying alternative method..." -ForegroundColor Yellow
    
    # Alternative: Use squirrel directly from NuGet package
    $squirrelPath = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\clowd.squirrel" -Recurse -Filter "Squirrel.exe" | Select-Object -First 1
    
    if ($squirrelPath) {
        Write-Host "Using Squirrel from: $($squirrelPath.FullName)" -ForegroundColor Yellow
        & $squirrelPath.FullName pack `
            --packId "VideoTimeStudy" `
            --packVersion $Version `
            --packDirectory ".\bin\Publish" `
            --releaseDir $OutputDir `
            --packTitle "Video Time Study" `
            --packAuthors "Nortek" `
            --noDelta
            
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`nInstaller created successfully!" -ForegroundColor Green
            Write-Host "Location: $OutputDir\" -ForegroundColor Cyan
        }
    } else {
        Write-Host "Could not find Squirrel executable. Please install: dotnet tool install --global Clowd.Squirrel" -ForegroundColor Red
    }
}

Write-Host "`nBuild complete!" -ForegroundColor Green
