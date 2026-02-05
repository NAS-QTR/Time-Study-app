# Segment Marking Feature

## Overview
The application now supports marking timestamps in up to 5 different segments for parallel time study tracking.

## How to Use

### Keyboard Shortcuts
- **Press 1**: Mark timestamp in Segment 1
- **Press 2**: Mark timestamp in Segment 2
- **Press 3**: Mark timestamp in Segment 3
- **Press 4**: Mark timestamp in Segment 4
- **Press 5**: Mark timestamp in Segment 5
- **Press M**: Mark timestamp with no segment (Segment 0)

Both the number row keys (1-5) and numpad keys work.

### What Happens When You Mark a Segment
- A timestamp entry is created at the current video position
- The entry is tagged with the segment number (1-5)
- The segment number appears in the "Seg" column in the data grid
- Status message shows: "Added entry at [time] (Segment X)"
- Description and element fields work the same as before

### Viewing Segments
- The data grid now has a "Seg" column between "Time" and "Duration"
- Segment numbers display in center-aligned, bold, teal text (#4EC9B0)
- Entries marked with M key show "0" in the segment column
- Entries marked with number keys show "1" through "5"

### Export Features
All export formats include the segment information:

**CSV Export**
- Header: `Timestamp,Segment,Duration (sec),Element,Description,Observations,Rating,Category`
- Segment column shows 0-5

**Excel XML Export**
- Segment column added between Timestamp and Duration
- Formatted as numeric data type

**HTML Report**
- Segment column labeled "Seg" in the data table
- Displays "-" for segment 0, or the segment number (1-5)
- Color-coded in teal to match the UI
- Included in JSON export data

## Use Cases

### Parallel Time Studies
- Track multiple operators simultaneously
- Segment 1 = Operator A
- Segment 2 = Operator B
- Etc.

### Multi-Process Analysis
- Segment 1 = Assembly process
- Segment 2 = Quality check
- Segment 3 = Packaging
- Etc.

### Comparative Studies
- Segment 1 = Method A
- Segment 2 = Method B
- Compare efficiency between methods

### Multi-Camera Analysis
- Each segment represents a different camera angle
- Synchronize analysis across views

## Technical Details

### Data Structure
- `TimeStudyEntry` class now has `Segment` property (int, default 0)
- Segment 0 = entries marked with M key
- Segments 1-5 = entries marked with number keys

### Sorting and Calculations
- Entries are sorted chronologically regardless of segment
- Duration calculations work across all segments
- Each entry's timing is independent of its segment

### Backward Compatibility
- Existing data files will load with Segment = 0
- All existing keyboard shortcuts still work
- M key continues to function as before (Segment 0)

## Tips
- You can mix segment and non-segment entries in the same study
- Use segments to organize your analysis without affecting timing calculations
- Filter/sort by segment column in exported Excel files for segment-specific analysis
- Consider documenting your segment numbering scheme in the study notes
