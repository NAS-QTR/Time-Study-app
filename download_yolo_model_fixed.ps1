# Download YOLOv8 Pose model for person detection with skeleton tracking
# This is a lightweight AI model (~6.5MB) trained to detect people AND their body keypoints

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   YOLOv8 Pose Detection Downloader" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Try multiple sources with fallbacks
$sources = @(
    @{
        Name = "Hugging Face (YOLOv8 Pose - Primary)"
        Url = "https://huggingface.co/Ultralytics/YOLOv8/resolve/main/yolov8n-pose.onnx"
        FileName = "yolov8n-pose.onnx"
        Size = 6500000
        Description = "Pose model with skeleton tracking (RECOMMENDED)"
    },
    @{
        Name = "GitHub (YOLOv8 Pose)"
        Url = "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n-pose.onnx"
        FileName = "yolov8n-pose.onnx"
        Size = 6500000
        Description = "Pose model with skeleton tracking"
    },
    @{
        Name = "Hugging Face (YOLOv8 Detection - Fallback)"
        Url = "https://huggingface.co/Ultralytics/YOLOv8/resolve/main/yolov8n.onnx"
        FileName = "yolov8n.onnx"
        Size = 6200000
        Description = "Basic detection (no skeleton)"
    }
)

$outputDir = $PSScriptRoot
$downloaded = $false
$downloadedFile = ""

# Check if pose model already exists
$poseModelPath = Join-Path $outputDir "yolov8n-pose.onnx"
if (Test-Path $poseModelPath) {
    $existingSize = (Get-Item $poseModelPath).Length
    if ($existingSize -gt 6000000) {
        Write-Host "✓ Pose model already exists!" -ForegroundColor Green
        Write-Host "  Location: $poseModelPath" -ForegroundColor Gray
        Write-Host "  Size: $([math]::Round($existingSize / 1MB, 2)) MB" -ForegroundColor Gray
        Write-Host "`nThe application will use AI with skeleton overlay.`n" -ForegroundColor Cyan
        Write-Host "Press any key to exit..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 0
    }
}

Write-Host "Attempting to download YOLOv8 Pose model...`n" -ForegroundColor Yellow

foreach ($source in $sources) {
    $outputPath = Join-Path $outputDir $source.FileName
    
    Write-Host "Downloading: $($source.Description)" -ForegroundColor Yellow
    Write-Host "  Source: $($source.Name)" -ForegroundColor Gray
    Write-Host "  URL: $($source.Url)" -ForegroundColor DarkGray
    
    try {
        $ProgressPreference = 'SilentlyContinue'
        
        # Download with progress
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($source.Url, $outputPath)
        
        if (Test-Path $outputPath) {
            $fileSize = (Get-Item $outputPath).Length
            $sizeMB = [math]::Round($fileSize / 1MB, 2)
            
            # Validate file size
            if ($fileSize -gt ($source.Size * 0.8) -and $fileSize -lt ($source.Size * 1.2)) {
                $downloaded = $true
                $downloadedFile = $source.FileName
                Write-Host "`n✓ Successfully downloaded!" -ForegroundColor Green
                Write-Host "  File: $($source.FileName)" -ForegroundColor Gray
                Write-Host "  Size: $sizeMB MB" -ForegroundColor Gray
                break
            }
            else {
                Write-Host "  ⚠ Invalid file size: $sizeMB MB" -ForegroundColor Yellow
                Remove-Item $outputPath -ErrorAction SilentlyContinue
            }
        }
    }
    catch {
        Write-Host "  ✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host ""
}

if ($downloaded) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "   Download Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    
    if ($downloadedFile -eq "yolov8n-pose.onnx") {
        Write-Host "`n🎉 SUCCESS! Pose model downloaded!`n" -ForegroundColor Green
        Write-Host "Features enabled:" -ForegroundColor Cyan
        Write-Host "  ✓ 90%+ accurate person detection" -ForegroundColor Green
        Write-Host "  ✓ Stick figure skeleton overlay" -ForegroundColor Green
        Write-Host "  ✓ 17 body keypoints tracked" -ForegroundColor Green
        Write-Host "  ✓ Better filtering of false positives" -ForegroundColor Green
    }
    else {
        Write-Host "`n✓ Basic detection model downloaded" -ForegroundColor Yellow
        Write-Host "`nNote: For skeleton overlays, manually download:" -ForegroundColor Cyan
        Write-Host "  yolov8n-pose.onnx from Hugging Face" -ForegroundColor Gray
    }
    
    Write-Host "`nModel saved to:" -ForegroundColor White
    Write-Host "  $(Join-Path $outputDir $downloadedFile)`n" -ForegroundColor Gray
    
    # Verify the model can be read
    try {
        $testPath = Join-Path $outputDir $downloadedFile
        $testRead = [System.IO.File]::ReadAllBytes($testPath)
        Write-Host "✓ Model file verified and readable" -ForegroundColor Green
    }
    catch {
        Write-Host "⚠ Warning: Could not verify model file" -ForegroundColor Yellow
    }
    
    Write-Host "`nPress any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
else {
    Write-Host "`n✗ All download sources failed." -ForegroundColor Red
    Write-Host "`nAlternative method - Export from Python:" -ForegroundColor Yellow
    Write-Host "1. Install Python and pip" -ForegroundColor White
    Write-Host "2. Run: pip install ultralytics" -ForegroundColor White
    Write-Host "3. Run: yolo export model=yolov8n-pose.pt format=onnx" -ForegroundColor White
    Write-Host "4. Copy yolov8n-pose.onnx to this folder" -ForegroundColor White
    Write-Host "`nOr download manually from:" -ForegroundColor Yellow
    Write-Host "https://huggingface.co/Ultralytics/YOLOv8/tree/main" -ForegroundColor White
    Write-Host "`nPress any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
