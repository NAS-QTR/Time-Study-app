# Deployment Guide - Video Time Study

This guide explains how to create shareable versions of the Video Time Study application that don't require admin rights.

## Quick Start

We provide **two options** for deployment:

### Option 1: Portable Version (RECOMMENDED - Easiest)
**Best for:** Quick sharing, USB drives, network shares, or users without installation permissions

```powershell
.\build-portable.ps1
```

This creates a ZIP file that users can extract and run anywhere. No installation needed!

**Output:** `Releases\VideoTimeStudy-v1.0.0-Portable.zip`

**User Instructions:**
1. Extract the ZIP file
2. Double-click `Launch Video Time Study.bat` or `VideoTimeStudy.exe`
3. That's it!

### Option 2: Squirrel Installer (Advanced)
**Best for:** Professional distribution with auto-update support

```powershell
.\build-installer.ps1
```

This creates a proper installer that:
- Doesn't require admin rights
- Installs to user's AppData folder
- Creates Start Menu shortcuts
- Provides clean uninstall experience
- Supports auto-updates

**Output:** `Releases\VideoTimeStudySetup.exe`

## Prerequisites

Before building, ensure you have:
- .NET 10.0 SDK installed
- Windows 10 or later

For Option 2 (Squirrel), also install:
```powershell
dotnet tool install --global Clowd.Squirrel
```

## Build Options

### Portable Build
```powershell
# Build with default version (1.0.0)
.\build-portable.ps1

# Build with custom version
.\build-portable.ps1 -Version "1.2.3"
```

### Installer Build
```powershell
# Build with default version (1.0.0)
.\build-installer.ps1

# Build with custom version and output directory
.\build-installer.ps1 -Version "1.2.3" -OutputDir ".\MyReleases"
```

## What Gets Included

Both deployment methods include:
- ✓ Video Time Study application
- ✓ .NET 10.0 Runtime (self-contained)
- ✓ All required DLLs and dependencies

## File Sizes

Expect the following approximate sizes:
- **Portable ZIP:** ~150-250 MB (compressed)
- **Extracted Folder:** ~400-500 MB
- **Squirrel Installer:** ~150-250 MB

The size is larger because it's self-contained (includes .NET runtime), but this ensures users don't need to install anything separately.

## Sharing with Users

### For Portable Version:
1. Build using `build-portable.ps1`
2. Share the ZIP file: `VideoTimeStudy-v1.0.0-Portable.zip`
3. Users extract and run - that's it!

### For Installer Version:
1. Build using `build-installer.ps1`
2. Share the Setup.exe: `VideoTimeStudySetup.exe`
3. Users run the installer - installs to their AppData folder

## Troubleshooting

### Build fails with "SDK not found"
- Install .NET 10.0 SDK from https://dotnet.microsoft.com/download

### "Squirrel not found" error
- Run: `dotnet tool install --global Clowd.Squirrel`

### Large file size
- This is normal for self-contained apps
- The benefit: users don't need to install .NET separately
- To reduce size, you could create a framework-dependent build (requires users to have .NET 10.0 installed)

## Advanced: Framework-Dependent Build

If your users already have .NET 10.0 installed, you can create a smaller package:

```powershell
dotnet publish VideoTimeStudy.csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o ".\Releases\VideoTimeStudy-Small"
```

This will be much smaller (~50 MB) but requires users to have .NET 10.0 installed.

## Version Management

To update the version:
1. Edit `VideoTimeStudy.csproj` - update the `<Version>` tag
2. Run the build script with `-Version` parameter
3. The version appears in:
   - File names
   - Application properties
   - About dialog (if implemented)

## Distribution Checklist

Before sharing with users:

- [ ] Test the built package on a clean Windows machine
- [ ] Verify all features work (video playback, marking, export)
- [ ] Test on both Windows 10 and Windows 11
- [ ] Include user documentation (README)
- [ ] Provide support contact information

## Security Notes

When sharing installers:
- ✓ Build on a trusted machine
- ✓ Scan with antivirus before distribution
- ✓ Consider code signing (prevents "Unknown Publisher" warnings)
- ✓ Host on secure download location

## Support

For issues or questions:
- Check the main README.md for application usage
- Contact: Nortek, Inc.

---

© 2026 Nortek, Inc.
