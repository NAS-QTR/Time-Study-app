# Generate YOLOv8 ONNX model for person detection
# This script downloads the YOLOv8 model and exports it to ONNX format

print("Installing ultralytics package...")
import subprocess
import sys

try:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "ultralytics", "--quiet"])
except:
    print("Failed to install ultralytics. Please run: pip install ultralytics")
    input("Press Enter to exit...")
    sys.exit(1)

print("\nDownloading and exporting YOLOv8 Nano model...")
print("This may take a few minutes on first run...\n")

try:
    from ultralytics import YOLO
    
    # Load YOLOv8 nano model (will auto-download if needed)
    model = YOLO('yolov8n.pt')
    
    # Export to ONNX format
    model.export(format='onnx', simplify=True)
    
    print("\n" + "="*50)
    print("✓ SUCCESS! Model created: yolov8n.onnx")
    print("="*50)
    print("\nThe model file is ready to use.")
    print("File location: yolov8n.onnx")
    print("\nRestart your Video Time Study application to enable AI detection!")
    
except Exception as e:
    print(f"\n✗ Error: {e}")
    print("\nTroubleshooting:")
    print("1. Make sure you have Python 3.8+ installed")
    print("2. Try: pip install --upgrade ultralytics")
    print("3. Check your internet connection")

input("\nPress Enter to exit...")
