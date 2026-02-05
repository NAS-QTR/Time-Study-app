# 🎉 Video Time Study - Ready to Share!

## What You Have

Your application is now packaged and ready to distribute! The package is located at:

**`Releases\VideoTimeStudy-v1.0.0-Portable.zip`** (84.5 MB)

## ✅ No Admin Rights Required!

This portable version:
- ✓ **No installation needed** - just extract and run
- ✓ **No admin rights required** - installs to user space
- ✓ **Self-contained** - includes .NET 10.0 runtime
- ✓ **All dependencies included** - ONNX Runtime, models, everything!
- ✓ **Portable** - run from anywhere (desktop, USB drive, network share)

## 📦 How to Share

### Option 1: Send the ZIP File (Recommended)
1. Share `VideoTimeStudy-v1.0.0-Portable.zip` via email, cloud storage, or USB drive
2. Recipients extract the ZIP file to any location
3. They run `Launch Video Time Study.bat` or `VideoTimeStudy.exe`
4. Done!

### Option 2: Share the Folder Directly
1. Copy the entire `Releases\VideoTimeStudy-v1.0.0\` folder
2. Share via network drive, USB, or cloud storage
3. Recipients can run it directly without extracting

## 👥 User Instructions

Provide these simple steps to your users:

```
Video Time Study - Installation
================================

1. Extract the ZIP file to any folder
   (e.g., Desktop, Documents, or USB drive)

2. Double-click "Launch Video Time Study.bat"
   or "VideoTimeStudy.exe"

3. That's it! No installation required.

The application will open and you can start
analyzing videos immediately.
```

## 📋 What's Included

Inside the package:
- ✓ VideoTimeStudy.exe - Main application
- ✓ .NET 10.0 runtime (self-contained)
- ✓ ONNX Runtime libraries
- ✓ YOLO models (yolo11n.onnx, yolo11n-pose.onnx)
- ✓ All required DLLs and dependencies
- ✓ README.txt - User documentation
- ✓ CHANGELOG.txt - Version history
- ✓ Launch Video Time Study.bat - Quick launcher

## 🔄 Updating the Version

To create a new version:

```powershell
.\package.ps1 -Version "1.1.0"
```

This will create:
- `Releases\VideoTimeStudy-v1.1.0-Portable.zip`
- `Releases\VideoTimeStudy-v1.1.0\` folder

## 🧪 Testing Before Distribution

Before sharing, test on a clean machine:
1. Extract the ZIP to a test location
2. Run the application
3. Test all features (video playback, marking, export)
4. Verify YOLO models load correctly
5. Test CSV export functionality

## 📊 System Requirements for Users

Users need:
- **Operating System:** Windows 10 or later (64-bit)
- **Disk Space:** ~200 MB free space
- **No other requirements!** Everything is included.

## 🐛 Known Limitations

- DirectML GPU acceleration temporarily disabled (CPU-based ONNX inference still works)
- File size is larger (~85 MB) because it's self-contained
  - Benefit: Users don't need to install anything separately!

## 📧 Support

Users can contact: Nortek, Inc.

## 🔐 Security Notes

- The package is safe to share
- Built from trusted source code
- Contains no malware or viruses
- Users may see "Windows protected your PC" warning (normal for unsigned apps)
  - Click "More info" → "Run anyway"
- Consider code signing for professional distribution to avoid this warning

## 🚀 Quick Distribution Checklist

- [ ] Test the package on a clean Windows machine
- [ ] Verify all features work correctly
- [ ] Include user instructions (README.txt is included automatically)
- [ ] Provide support contact information
- [ ] Upload to secure location (OneDrive, SharePoint, etc.)
- [ ] Share the download link with users

---

## 📝 Advanced: Creating a Traditional Installer

If you want a traditional installer (like Setup.exe), you can use the Squirrel installer:

1. Install the tool:
   ```powershell
   dotnet tool install --global Clowd.Squirrel
   ```

2. Run:
   ```powershell
   .\build-installer.ps1
   ```

This creates a proper installer that:
- Installs to user's AppData (no admin required)
- Creates Start Menu shortcuts
- Provides uninstall functionality
- Supports auto-updates

However, the **portable version is recommended** for simplicity!

---

© 2026 Nortek, Inc.
