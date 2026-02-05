"""
Step 3: Train custom YOLO11 pose model on your warehouse data
Run this after you've annotated your frames
"""
from ultralytics import YOLO
import torch

# Configuration
DATASET_YAML = "training_data/data.yaml"  # Path to your dataset.yaml from Roboflow/Label Studio
EPOCHS = 100  # Number of training iterations
IMAGE_SIZE = 640  # Input image size
BATCH_SIZE = 16  # Adjust based on your GPU memory (8, 16, or 32)
MODEL_NAME = "yolo11n-pose.pt"  # Start from pretrained YOLO11 nano pose model

def train_model():
    # Check if GPU is available
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    print(f"Training on: {device}")
    
    if device == 'cpu':
        print("WARNING: Training on CPU will be very slow!")
        print("Consider using Google Colab (free GPU) or reducing dataset size")
    
    # Load pretrained model
    print(f"\nLoading base model: {MODEL_NAME}")
    model = YOLO(MODEL_NAME)
    
    # Train the model
    print(f"\nStarting training for {EPOCHS} epochs...")
    results = model.train(
        data=DATASET_YAML,
        epochs=EPOCHS,
        imgsz=IMAGE_SIZE,
        batch=BATCH_SIZE,
        device=device,
        project="warehouse_training",
        name="warehouse_pose_v1",
        
        # Optimization settings
        patience=20,  # Early stopping if no improvement
        save=True,
        save_period=10,  # Save checkpoint every 10 epochs
        
        # Data augmentation (reduces overfitting)
        hsv_h=0.015,
        hsv_s=0.7,
        hsv_v=0.4,
        degrees=10,
        translate=0.1,
        scale=0.5,
        flipud=0.0,
        fliplr=0.5,
        mosaic=1.0,
        
        # Performance
        workers=4,
        verbose=True
    )
    
    print("\n=== Training Complete! ===")
    print(f"Best model saved to: warehouse_training/warehouse_pose_v1/weights/best.pt")
    print("\nNext steps:")
    print("1. Review training metrics in warehouse_training/warehouse_pose_v1/")
    print("2. Run step 4 to validate the model")
    print("3. Run step 5 to export to ONNX for your app")

def train_on_colab():
    print("""
    === TRAINING ON GOOGLE COLAB (Free GPU) ===
    
    If training is too slow on your PC:
    
    1. Go to https://colab.research.google.com/
    2. Create new notebook
    3. Enable GPU: Runtime → Change runtime type → GPU → T4 GPU
    4. Upload your dataset.zip (from Roboflow)
    5. Run this code:
    
    ```python
    !pip install ultralytics
    
    from ultralytics import YOLO
    import zipfile
    
    # Upload and extract dataset
    !unzip dataset.zip -d ./
    
    # Train model
    model = YOLO('yolo11n-pose.pt')
    results = model.train(
        data='data.yaml',
        epochs=100,
        imgsz=640,
        batch=16,
        device=0
    )
    
    # Download best model
    from google.colab import files
    files.download('runs/pose/train/weights/best.pt')
    ```
    
    Training time: 2-4 hours on free GPU (vs 12-24 hours on CPU)
    """)

if __name__ == "__main__":
    import sys
    
    if len(sys.argv) > 1 and sys.argv[1] == "colab":
        train_on_colab()
    else:
        # Check if ultralytics is installed
        try:
            import ultralytics
        except ImportError:
            print("Installing required packages...")
            import subprocess
            subprocess.check_call(["pip", "install", "ultralytics", "torch", "torchvision"])
        
        train_model()
