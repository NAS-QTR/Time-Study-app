import os

file_path = r'c:\Users\jonathan.peters\OneDrive - Nortek, Inc\Programs\Time-Study\Time-Study-app\MainWindow.xaml.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

print(f"Original line count: {len(lines)}")

# Deletion ranges (1-indexed, inclusive)
delete_ranges = [
    (19, 20),      # ONNX using statements
    (44, 76),      # ML/Zone field declarations
    (228, 235),    # InitializeYoloModel() call + diagnostics
    (2165, 2585),  # #region Zone Tracking through #endregion
    (2780, 5143),  # MotionDetectionSettings through corrupted Zone Persistence
]

# Create set of line indices to delete (convert to 0-indexed)
lines_to_delete = set()
for start, end in delete_ranges:
    for i in range(start - 1, end):  # start-1 because 1-indexed to 0-indexed
        lines_to_delete.add(i)

print(f"Total lines to delete: {len(lines_to_delete)}")

# Build new file content
new_lines = []
for i, line in enumerate(lines):
    if i in lines_to_delete:
        continue
    new_lines.append(line)
    # After original line 2779 (0-indexed: 2778), insert #endregion to close Keyboard Shortcuts region
    if i == 2778:
        new_lines.append('\n')
        new_lines.append('    #endregion\n')

print(f"New line count: {len(new_lines)}")
print(f"Net change: {len(new_lines) - len(lines)}")

# Write back
with open(file_path, 'w', encoding='utf-8') as f:
    f.writelines(new_lines)

print("File written successfully.")
