import onnx
from onnx import version_converter

# Load the YOLO11 pose model
model = onnx.load("yolo11n-pose.onnx")

# Convert to opset 21
converted_model = version_converter.convert_version(model, 21)

# Save the converted model
onnx.save(converted_model, "yolo11n-pose-opset21.onnx")
print("Converted yolo11n-pose.onnx to opset 21 → yolo11n-pose-opset21.onnx")
