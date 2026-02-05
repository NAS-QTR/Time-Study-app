"""
Step 1: Extract training frames from your warehouse video
This will save frames at intervals for you to label
"""
import cv2
import os
from pathlib import Path

# Configuration
VIDEO_PATH = "your_warehouse_video.mp4"  # Change to your video file
OUTPUT_DIR = "training_data/frames"
FRAME_INTERVAL = 30  # Extract every 30 frames (1 per second at 30fps)
MAX_FRAMES = 500  # Maximum frames to extract

def extract_frames():
    # Create output directory
    Path(OUTPUT_DIR).mkdir(parents=True, exist_ok=True)
    
    # Open video
    cap = cv2.VideoCapture(VIDEO_PATH)
    if not cap.isOpened():
        print(f"Error: Could not open video {VIDEO_PATH}")
        return
    
    fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    
    print(f"Video: {VIDEO_PATH}")
    print(f"FPS: {fps}, Total frames: {total_frames}")
    print(f"Extracting every {FRAME_INTERVAL} frames (max {MAX_FRAMES})")
    
    frame_count = 0
    saved_count = 0
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        # Save frame at intervals
        if frame_count % FRAME_INTERVAL == 0 and saved_count < MAX_FRAMES:
            output_path = os.path.join(OUTPUT_DIR, f"frame_{frame_count:06d}.jpg")
            cv2.imwrite(output_path, frame, [cv2.IMWRITE_JPEG_QUALITY, 95])
            saved_count += 1
            print(f"Saved {saved_count}/{MAX_FRAMES}: {output_path}")
        
        frame_count += 1
        
        if saved_count >= MAX_FRAMES:
            break
    
    cap.release()
    print(f"\nExtraction complete! Saved {saved_count} frames to {OUTPUT_DIR}")
    print("\nNext steps:")
    print("1. Review frames and delete ones without people or with poor quality")
    print("2. Keep 200-300 diverse frames showing people in different poses/locations")
    print("3. Run step 2 to set up annotation tool")

if __name__ == "__main__":
    extract_frames()
