"""
Step 4: Validate your trained model
Test it on some frames to see if it works well
"""
from ultralytics import YOLO
import cv2
from pathlib import Path

# Configuration
MODEL_PATH = "warehouse_training/warehouse_pose_v1/weights/best.pt"
TEST_IMAGES = "training_data/frames"  # Directory with test images
OUTPUT_DIR = "validation_results"
CONFIDENCE = 0.5

def validate_model():
    # Create output directory
    Path(OUTPUT_DIR).mkdir(parents=True, exist_ok=True)
    
    # Load your trained model
    print(f"Loading model: {MODEL_PATH}")
    model = YOLO(MODEL_PATH)
    
    # Get test images
    test_images = list(Path(TEST_IMAGES).glob("*.jpg"))[:20]  # Test on 20 images
    
    print(f"\nTesting on {len(test_images)} images...")
    
    for img_path in test_images:
        # Run inference
        results = model(str(img_path), conf=CONFIDENCE)
        
        # Save annotated image
        for r in results:
            im_bgr = r.plot()  # Draw predictions on image
            output_path = Path(OUTPUT_DIR) / img_path.name
            cv2.imwrite(str(output_path), im_bgr)
        
        print(f"Processed: {img_path.name}")
    
    print(f"\n=== Validation Complete! ===")
    print(f"Check results in: {OUTPUT_DIR}/")
    print("\nLook for:")
    print("- Are people correctly detected?")
    print("- Are skeletons accurate?")
    print("- Any false positives on equipment?")
    print("\nIf results look good, continue to step 5 to export to ONNX")

if __name__ == "__main__":
    validate_model()
