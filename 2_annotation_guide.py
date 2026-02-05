"""
Step 2: Set up Roboflow for easy annotation
Roboflow provides free annotation tools and handles the hard work
"""

# INSTRUCTIONS FOR ANNOTATION
print("""
=== ANNOTATION SETUP (EASIEST METHOD) ===

Option A: Roboflow (Recommended - Free & Easy)
---------------------------------------------
1. Go to https://roboflow.com/ and create free account
2. Create new project: "Warehouse Person Detection"
3. Select "Object Detection" and "Pose Estimation"
4. Upload your frames from training_data/frames/
5. Use their annotation tool to:
   - Draw bounding boxes around people
   - Mark keypoints (17 points: nose, eyes, shoulders, elbows, wrists, hips, knees, ankles)
6. Aim for 200-300 annotated frames
7. Export as "YOLOv8 Pose" format
8. Download the dataset

Benefits:
- No software to install
- Easy web interface
- Auto-generates train/val split
- Provides data augmentation
- Free for small datasets


Option B: Label Studio (Local, Free)
------------------------------------
1. Install: pip install label-studio
2. Run: label-studio start
3. Open browser: http://localhost:8080
4. Create project with YOLO pose template
5. Import frames
6. Annotate 200-300 frames
7. Export as YOLO format

Benefits:
- Works offline
- No account needed
- Full control


ANNOTATION TIPS:
- Focus on frames where people are clearly visible
- Skip blurry or occluded frames
- Annotate people in various poses (standing, bending, reaching)
- Include different distances from camera
- Mark all visible keypoints, even if partially visible
- Consistency is key - use same rules for all frames


TIME ESTIMATE:
- 200 frames × 2-3 minutes each = 6-10 hours
- Can be done in multiple sessions
- Quality > quantity


After annotation, continue to step 3 (training script)
""")
