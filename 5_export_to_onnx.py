"""
Step 5: Export trained model to ONNX format for your C# application
"""
from ultralytics import YOLO

# Configuration
MODEL_PATH = "warehouse_training/warehouse_pose_v1/weights/best.pt"
OUTPUT_NAME = "warehouse_yolo11n-pose.onnx"

def export_to_onnx():
    print(f"Loading model: {MODEL_PATH}")
    model = YOLO(MODEL_PATH)
    
    print("\nExporting to ONNX format...")
    print("This may take a few minutes...")
    
    # Export to ONNX with opset 17 (compatible with ONNX Runtime 1.21.0)
    success = model.export(
        format='onnx',
        opset=17,  # Use opset 17 for compatibility
        simplify=True,
        dynamic=False,
        imgsz=640
    )
    
    if success:
        # The exported file will be in the same directory as the .pt file
        import shutil
        from pathlib import Path
        
        pt_dir = Path(MODEL_PATH).parent
        onnx_file = pt_dir / "best.onnx"
        
        if onnx_file.exists():
            # Copy to project root with a descriptive name
            shutil.copy(onnx_file, OUTPUT_NAME)
            print(f"\n=== Export Successful! ===")
            print(f"ONNX model saved to: {OUTPUT_NAME}")
            print(f"\nTo use in your application:")
            print(f"1. Copy {OUTPUT_NAME} to your project directory")
            print(f"2. Rename it to: yolo11n-pose.onnx")
            print(f"3. Restart your application")
            print(f"4. The custom model will automatically be loaded!")
        else:
            print("Error: ONNX file not found after export")
    else:
        print("Error: Export failed")

if __name__ == "__main__":
    export_to_onnx()
