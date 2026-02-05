# Custom YOLO11 Training Guide for Warehouse Person Detection

## Overview
Train a custom YOLO11 pose estimation model specifically for your warehouse environment to eliminate false positives on equipment and improve accuracy.

## Time Estimate
- **Frame extraction:** 10 minutes
- **Annotation:** 6-10 hours (can be split into sessions)
- **Training:** 2-4 hours on GPU (12-24 hours on CPU)
- **Validation & export:** 30 minutes
- **Total:** ~10-15 hours spread over a few days

## Prerequisites
- Python 3.8 or newer
- Your warehouse video file
- 200-300 GB free disk space (for frames and training)
- GPU recommended (or use free Google Colab)

## Step-by-Step Process

### Step 1: Extract Training Frames
```bash
# Edit 1_extract_frames.py and set your video path
# Then run:
python 1_extract_frames.py
```
This will extract 500 frames from your video. Review them and delete:
- Blurry frames
- Frames with no people
- Duplicate/similar frames

Keep 200-300 diverse frames showing people in different:
- Locations
- Poses (standing, bending, reaching)
- Distances from camera
- Lighting conditions

### Step 2: Annotate Frames

**Option A: Roboflow (Recommended)**
1. Go to https://roboflow.com/
2. Create free account
3. Create project: "Warehouse Person Detection" → Pose Estimation
4. Upload your frames
5. Annotate each person with:
   - Bounding box
   - 17 keypoints (nose, eyes, ears, shoulders, elbows, wrists, hips, knees, ankles)
6. Export as "YOLOv8 Pose" format
7. Download dataset

**Option B: Label Studio (Local)**
```bash
pip install label-studio
label-studio start
# Open http://localhost:8080
```

**Annotation Quality Tips:**
- Annotate ONLY actual people (not equipment, reflections, etc.)
- Be consistent with keypoint placement
- Mark occluded keypoints if you can estimate location
- Skip frames where people are too small/blurry

### Step 3: Train Model

**Option A: Local Training (if you have GPU)**
```bash
python 3_train_model.py
```

**Option B: Google Colab (Free GPU - Recommended)**
```bash
python 3_train_model.py colab
# Follow the printed instructions
```

Training will take 2-4 hours on GPU. You'll see:
- Loss decreasing over time (good!)
- Metrics improving (mAP should reach 0.7+)
- Best model saved automatically

### Step 4: Validate Results
```bash
python 4_validate_model.py
```
Check `validation_results/` folder:
- Are people correctly detected?
- Are skeletons accurate?
- Any false positives on equipment?

If accuracy is poor:
- Add more diverse training images
- Re-check annotations for errors
- Train for more epochs

### Step 5: Export to ONNX
```bash
python 5_export_to_onnx.py
```

This creates `warehouse_yolo11n-pose.onnx` - your custom model!

### Step 6: Use Custom Model
1. Backup original: `copy yolo11n-pose.onnx yolo11n-pose.onnx.backup`
2. Replace with custom: `copy warehouse_yolo11n-pose.onnx yolo11n-pose.onnx`
3. Restart your application
4. Test on your warehouse video!

## Expected Results
- **Before:** Detects equipment as people, ~30-50% false positives
- **After:** Only detects actual people, <5% false positives
- **Accuracy:** 85-95% (depends on annotation quality)

## Troubleshooting

### Training is too slow
→ Use Google Colab free GPU (see step 3)

### Model not improving
→ Check annotations for errors
→ Add more diverse training images
→ Reduce confidence threshold in app

### Still getting false positives
→ Add more negative examples (frames with equipment but no people)
→ Increase confidence threshold in app
→ Train for more epochs

### Out of memory during training
→ Reduce batch size: `BATCH_SIZE = 8` in 3_train_model.py

## Alternative: Quick Fix Without Training

If training seems too complex, try these simpler options:

1. **Increase confidence threshold** (already done - 70%)
2. **Add region filtering** - only detect in specific zones
3. **Use larger YOLO model** - yolo11m-pose.onnx (more accurate but slower)
4. **Temporal filtering** - ignore detections that only appear for 1 frame

Let me know if you want help with any of these alternatives!

## Support
If you get stuck at any step, share:
- Which step you're on
- Error message (if any)
- What you've tried

I can help debug or provide more specific guidance.
