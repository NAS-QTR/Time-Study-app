# Video Time Study Application

A Windows desktop application for conducting video time studies with timestamp marking and data collection.

## Features

- **Video Playback**: Play, pause, and stop video files (supports MP4, AVI, MKV, WMV)
- **Timeline Control**: Seek through video with interactive timeline slider
- **Timestamp Marking**: Capture timestamps with descriptions while watching
- **Data Grid**: Excel-like spreadsheet interface for viewing and editing entries
- **Data Export**: Export time study data to CSV format
- **Categories**: Organize timestamps by category
- **Duration Tracking**: Record duration for each activity

## Getting Started

### Prerequisites

- .NET 10.0 or later
- Windows OS

### Running the Application

1. **Using VS Code**:
   - Press `F5` to build and launch the application in debug mode
   - Or use `Ctrl+Shift+B` to build, then run manually

2. **Using Command Line**:
   ```powershell
   dotnet run --project VideoTimeStudy.csproj
   ```

3. **Using the Build Task**:
   - Press `Ctrl+Shift+P` and select "Tasks: Run Task"
   - Choose "run" from the list

## How to Use

### Loading a Video

1. Click **File > Open Video**
2. Select a video file from your computer
3. The video will load in the player

### Marking Timestamps

1. Play the video and pause at the moment you want to mark
2. Type a description in the text box below the video player
3. Click **Add Timestamp Entry**
4. The entry appears in the data grid on the right

### Managing Data

- **Edit entries**: Click on any cell in the data grid to edit
- **Add categories**: Type in the Category column to organize your entries
- **Delete entries**: Select a row and click **Delete Selected**
- **Clear all**: Click **Clear All** to remove all entries (with confirmation)

### Exporting Data

1. Click **File > Export Data**
2. Choose a location and filename
3. Your data will be saved as a CSV file that can be opened in Excel

## Data Grid Columns

- **Timestamp**: Time in the video (HH:MM:SS format)
- **Time (sec)**: Time in seconds for calculations
- **Description**: What is happening at this timestamp
- **Category**: Optional category for grouping activities
- **Duration**: Optional duration field for time tracking

## Keyboard Shortcuts

- `Ctrl+O`: Open video (when implemented)
- `Space`: Play/Pause (when focus is on player)

## Building from Source

```powershell
dotnet build VideoTimeStudy.csproj
```

## Project Structure

- `MainWindow.xaml`: UI layout and design
- `MainWindow.xaml.cs`: Application logic and event handlers
- `VideoTimeStudy.csproj`: Project configuration

## Future Enhancements

- Keyboard shortcuts for quick timestamp marking
- Video frame advance/rewind controls
- Automatic duration calculation between timestamps
- Import/export formats (JSON, Excel)
- Video annotation overlay
- Multiple video comparison view

## License

This project is for internal use.
