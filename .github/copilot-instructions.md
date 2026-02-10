<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

# Video Time Study Application

This is a Windows WPF application built with C# and .NET 10.0 for conducting video time studies.

## Project Overview

The application provides:
- Video playback with timeline controls
- Timestamp marking with descriptions
- Excel-like data grid for time study entries
- CSV export functionality

## Project Structure

```
├── .github/              # GitHub/Copilot config
├── .vscode/              # VS Code tasks & launch config
├── docs/                 # Documentation (deployment, test plan, etc.)
├── scripts/              # Build & packaging scripts
├── VideoTimeStudy.Tests/ # xUnit test project
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs   # Main UI & logic
├── ElementEditorWindow.xaml / .cs         # Element editor dialog
├── AssemblyInfo.cs
├── VideoTimeStudy.csproj
└── Time Study app.sln
```

## Key Files

- `MainWindow.xaml`: Main UI layout
- `MainWindow.xaml.cs`: Application logic
- `ElementEditorWindow.xaml` / `.cs`: Element library editor
- `VideoTimeStudy.csproj`: Project configuration
- `scripts/build-portable.ps1`: Build portable release
- `scripts/build-installer.ps1`: Build Squirrel installer

## Development Guidelines

- Use WPF controls and MVVM patterns where appropriate
- Maintain clean separation between UI and logic
- Follow C# naming conventions
- Test video playback with various formats (MP4, AVI, MKV, WMV)

## Build and Run

- Build: `dotnet build VideoTimeStudy.csproj`
- Run: `dotnet run --project VideoTimeStudy.csproj`
- Debug: Press F5 in VS Code
- Package: `.\scripts\build-portable.ps1`

