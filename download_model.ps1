# Download YOLOv8 Pose model
Write-Host "`nYOLOv8 Pose Model Downloader`n" -ForegroundColor Cyan

$outputDir = $PSScriptRoot
$poseModelPath = Join-Path $outputDir "yolov8n-pose.onnx"

# Check if already exists
if (Test-Path $poseModelPath) {
    $size = (Get-Item $poseModelPath).Length
    if ($size -gt 6000000) {
        Write-Host "Pose model already exists ($([math]::Round($size/1MB,2)) MB)" -ForegroundColor Green
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 0
    }
}

Write-Host "Downloading YOLOv8 Pose model...`n" -ForegroundColor Yellow

$urls = @(
    "https://huggingface.co/Ultralytics/YOLOv8/resolve/main/yolov8n-pose.onnx",
    "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n-pose.onnx"
)

$success = $false

foreach ($url in $urls) {
    Write-Host "Trying: $url" -ForegroundColor Gray
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($url, $poseModelPath)
        
        if (Test-Path $poseModelPath) {
            $size = (Get-Item $poseModelPath).Length
            if ($size -gt 6000000) {
                $success = $true
                Write-Host "`nSUCCESS! Downloaded $([math]::Round($size/1MB,2)) MB" -ForegroundColor Green
                break
            }
            Remove-Item $poseModelPath -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

if ($success) {
    Write-Host "`nFeatures enabled:" -ForegroundColor Cyan
    Write-Host "  - 90%+ accurate person detection" -ForegroundColor Green
    Write-Host "  - Skeleton overlay visualization" -ForegroundColor Green
    Write-Host "  - 17 body keypoints tracked" -ForegroundColor Green
    Write-Host "`nModel saved to: $poseModelPath" -ForegroundColor White
}
else {
    Write-Host "`nDownload failed from all sources." -ForegroundColor Red
    Write-Host "Manual download: https://huggingface.co/Ultralytics/YOLOv8/tree/main" -ForegroundColor Yellow
}

Write-Host "`nPress any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
