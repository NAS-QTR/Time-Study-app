# YOLOv8 Model Setup Instructions

## ⚠️ AI Model Required

To enable AI-based person detection, you need to download the YOLOv8 model file.

### Option 1: Hugging Face (Recommended)
1. Visit: https://huggingface.co/Ultralytics/YOLOv8/tree/main
2. Click on "yolov8n.onnx" (6.2 MB)
3. Click the download button (⬇️)
4. Save in the same folder as VideoTimeStudy.exe

### Option 2: Export from Python
If you have Python installed:
```bash
pip install ultralytics
yolo export model=yolov8n.pt format=onnx
```
This creates `yolov8n.onnx` in the current directory.

### Option 3: Use Pre-converted Model
Direct download link (if available):
```
https://huggingface.co/Ultralytics/YOLOv8/resolve/main/yolov8n.onnx
```

### Option 4: PowerShell Script
Run the included script (may require execution policy change):
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\download_yolo_model.ps1
```

## How It Works

**Without yolov8n.onnx:**
- Uses basic motion blob detection
- Groups moving pixels as "people"
- Less accurate, can confuse shadows/equipment

**With yolov8n.onnx:**
- Uses AI trained on millions of images
- Recognizes human shapes and postures
- 90%+ accuracy even with distant cameras
- Tracks stationary people
- Confidence scores for each detection

## File Location
Place `yolov8n.onnx` in one of these locations:
- Same folder as VideoTimeStudy.exe
- `C:\Users\YourName\AppData\Roaming\VideoTimeStudy\`

The app will display:
- ✓ **"AI person detection enabled"** - model loaded successfully
- ⚠️ **"AI model not found"** - using fallback motion detection
