using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Navigation;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Linq;

namespace VideoTimeStudy;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<TimeStudyEntry> timeStudyData;
    private DispatcherTimer timer;
    private EventHandler? renderingEventHandler;
    private double currentSpeedRatio = 1.0;
    private double currentZoom = 1.0;
    private double baseVideoWidth = 1920;
    private double baseVideoHeight = 1080;
    private double timelinePixelsPerSecond = 10.0;
    private double timelineZoom = 1.0;
    private string currentVideoFileName = "";
    private List<VideoSegment> videoSegments = new List<VideoSegment>(); // Track multiple videos
    private int currentVideoIndex = 0;
    private double cumulativeVideoTime = 0.0; // Total time of all loaded videos
    
    // Zone tracking
    private ObservableCollection<WorkZone> workZones = new ObservableCollection<WorkZone>();
    private bool isDefiningZone = false;
    private Point zoneStartPoint;
    private Rectangle? currentZoneRect;
    private bool zonesVisible = true;
    private WorkZone? selectedZone = null;
    
    // Motion detection
    private System.Windows.Threading.DispatcherTimer? motionDetectionTimer;
    private bool isMotionTrackingActive = false;
    private RenderTargetBitmap? renderBitmap;
    private int motionDetectionIntervalMs = 500; // Adjustable detection frequency
    private int frameSkipCount = 1; // Process every nth frame
    private WorkZone? lastSelectedZone = null; // For quick zone entry/exit
    
    // Motion trail overlay
    private bool motionTrailEnabled = false;
    private System.Windows.Threading.DispatcherTimer? motionTrailTimer;
    private Queue<BitmapSource> trailFrames = new Queue<BitmapSource>();
    private const int TRAIL_FRAME_COUNT = 5; // Number of frames to overlay
    private const double TRAIL_CAPTURE_INTERVAL_MS = 1000; // Capture every 1 second
    
    // Person tracking and detection
    private bool showPersonNumbers = false; // Toggle for showing person number overlays
    private int nextPersonId = 1; // Global counter for assigning person IDs
    private InferenceSession? yoloSession = null; // YOLO model for person detection
    private const int YOLO_INPUT_SIZE = 640; // YOLOv8 input size
    private const float CONFIDENCE_THRESHOLD = 0.70f; // Minimum confidence for detection (higher to reduce false positives)
    private const float KEYPOINT_CONFIDENCE_THRESHOLD = 0.5f; // Minimum confidence for keypoints
    private bool usePoseModel = false; // True if using pose estimation model
    private bool showSkeletonOverlay = true; // Toggle for skeleton visualization
    private Canvas? skeletonOverlay; // Canvas for drawing skeletons
    
    private List<string> elementLibrary = ElementEditorWindow.GetDefaultElements();
    private Dictionary<int, string> segmentNames = new Dictionary<int, string>
    {
        { 0, "Seg M" },
        { 1, "Seg 1" },
        { 2, "Seg 2" },
        { 3, "Seg 3" },
        { 4, "Seg 4" },
        { 5, "Seg 5" }
    };
    
    // Pan and zoom fields
    private bool isPanning = false;
    private Point panStartPoint;
    private double horizontalOffsetStart;
    private double verticalOffsetStart;
    
    // Timeline drag fields
    private bool isTimelineDragging = false;
    private bool isTimelinePanning = false;
    private bool hasStartedDragging = false;
    private const double DragThreshold = 3.0; // pixels
    private Point timelineDragStartPoint;
    private TimeSpan timelineDragStartPosition;
    private Point timelinePanStartPoint;
    private double timelineHorizontalOffsetStart;
    private DateTime lastTimelineClickTime = DateTime.MinValue;
    private const int DoubleClickMilliseconds = 500;
    private Canvas? lastDraggedCanvas = null;
    private bool smoothingEnabled = true;
    private bool _isScrollSyncing = false; // Prevent scroll sync feedback loops
    
    // Helper properties for MediaElement
    private bool HasVideo => VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan;
    private double VideoDurationSeconds => cumulativeVideoTime > 0 ? cumulativeVideoTime : (HasVideo ? VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds : 0);
    private double CurrentPositionSeconds 
    { 
        get 
        {
            // If we have multiple videos, return cumulative position
            if (videoSegments.Count > 1 && currentVideoIndex >= 0 && currentVideoIndex < videoSegments.Count)
            {
                var currentSegment = videoSegments[currentVideoIndex];
                return currentSegment.StartTime + VideoPlayer.Position.TotalSeconds;
            }
            return VideoPlayer.Position.TotalSeconds;
        }
        set 
        {
            // Check if we need to switch to a different video segment
            if (videoSegments.Count > 0)
            {
                // Find which video segment contains this time
                for (int i = 0; i < videoSegments.Count; i++)
                {
                    var segment = videoSegments[i];
                    
                    // Debug output
                    System.Diagnostics.Debug.WriteLine($"Checking segment {i}: StartTime={segment.StartTime}, EndTime={segment.EndTime}, RequestedTime={value}");
                    
                    if (value >= segment.StartTime && value < segment.EndTime)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found matching segment {i}: {segment.FilePath}");
                        
                        // Need to switch to this video if not already loaded
                        if (i != currentVideoIndex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Switching from video {currentVideoIndex} to video {i}");
                            currentVideoIndex = i;
                            StatusText.Text = $"Loading: {System.IO.Path.GetFileName(segment.FilePath)}...";
                            VideoPlayer.Source = new Uri(segment.FilePath);
                            VideoPlayer.Position = TimeSpan.FromSeconds(value - segment.StartTime);
                            return;
                        }
                        else
                        {
                            // Same video, just set position relative to segment start
                            VideoPlayer.Position = TimeSpan.FromSeconds(value - segment.StartTime);
                            return;
                        }
                    }
                }
                
                // If we're beyond all segments, clamp to last segment
                if (value >= cumulativeVideoTime)
                {
                    var lastSegment = videoSegments[videoSegments.Count - 1];
                    if (currentVideoIndex != videoSegments.Count - 1)
                    {
                        currentVideoIndex = videoSegments.Count - 1;
                        VideoPlayer.Source = new Uri(lastSegment.FilePath);
                    }
                    VideoPlayer.Position = TimeSpan.FromSeconds(lastSegment.Duration);
                }
            }
            else
            {
                // No segments, simple case
                VideoPlayer.Position = TimeSpan.FromSeconds(value);
            }
        }
    }
    
    // Performance optimization fields
    private bool isLowQualityMode = false;
    private DispatcherTimer scrubThrottleTimer;
    private TimeSpan pendingScrubPosition;
    private bool hasPendingScrub = false;
    private bool wasPlayingBeforeScrub = false;
    private bool useDecimalTimeFormat = false; // Toggle for time format
    private int frameSkipCounter = 0; // For skipping render frames
    private bool isScrubbing = false; // Track if user is actively scrubbing

    public MainWindow()
    {
        InitializeComponent();
        
        timeStudyData = new ObservableCollection<TimeStudyEntry>();
        TimeStudyGrid.ItemsSource = timeStudyData;

        // Populate element library dropdown immediately
        PopulateElementLibrary();
        
        // Setup timer for updating video position
        timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += Timer_Tick;
        
        // Setup VSync rendering for smooth playback indicator
        renderingEventHandler = OnRendering;
        CompositionTarget.Rendering += renderingEventHandler;
        
        // Setup scrub throttle timer for performance
        scrubThrottleTimer = new DispatcherTimer();
        scrubThrottleTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
        scrubThrottleTimer.Tick += ScrubThrottleTimer_Tick;
        
        // Setup grid events
        TimeStudyGrid.SelectionChanged += TimeStudyGrid_SelectionChanged;
        TimeStudyGrid.MouseDoubleClick += TimeStudyGrid_MouseDoubleClick;
        
        // Setup keyboard shortcuts
        this.KeyDown += MainWindow_KeyDown;
        
        // Initialize placeholder text color
        AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        
        // Initialize timeline
        Loaded += MainWindow_Loaded;
        
        // Initialize YOLO model for person detection
        InitializeYoloModel();
        
        // Log startup diagnostics
        System.Diagnostics.Debug.WriteLine($"=== APP STARTUP ===");
        System.Diagnostics.Debug.WriteLine($"usePoseModel: {usePoseModel}");
        System.Diagnostics.Debug.WriteLine($"showSkeletonOverlay: {showSkeletonOverlay}");
        System.Diagnostics.Debug.WriteLine($"yoloSession: {(yoloSession != null ? "LOADED" : "NULL")}");
    }

    private void PopulateElementLibrary()
    {
        ElementLibraryComboBox.Items.Clear();
        System.Diagnostics.Debug.WriteLine($"Populating element library with {elementLibrary.Count} elements");
        foreach (var element in elementLibrary)
        {
            ElementLibraryComboBox.Items.Add(new ComboBoxItem { Content = element });
            System.Diagnostics.Debug.WriteLine($"Added element: {element}");
        }
        System.Diagnostics.Debug.WriteLine($"ElementLibraryComboBox now has {ElementLibraryComboBox.Items.Count} items");
    }

    private void RecalculateDurations()
    {
        // Calculate duration from current mark to next mark
        for (int i = 0; i < timeStudyData.Count; i++)
        {
            double currentTime = TimeSpan.Parse(timeStudyData[i].Timestamp).TotalSeconds;
            
            // Find the next mark (regardless of segment)
            if (i < timeStudyData.Count - 1)
            {
                double nextTime = TimeSpan.Parse(timeStudyData[i + 1].Timestamp).TotalSeconds;
                timeStudyData[i].TimeInSeconds = Math.Round(nextTime - currentTime, 2);
            }
            else
            {
                // Last entry: duration until end of video
                if (HasVideo)
                {
                    timeStudyData[i].TimeInSeconds = Math.Round(VideoDurationSeconds - currentTime, 2);
                }
                else
                {
                    timeStudyData[i].TimeInSeconds = 0;
                }
            }
        }
        
        // Force grid refresh
        TimeStudyGrid.Items.Refresh();
        
        // Update segment summary
        UpdateSegmentSummary();
    }

    private void RecalculateDurationsIncremental(int startIndex)
    {
        // Recalculate durations for affected entries (current and previous)
        if (startIndex >= timeStudyData.Count) return;
        
        // Recalculate the previous entry (if exists) since its duration now ends at this new mark
        if (startIndex > 0)
        {
            int prevIndex = startIndex - 1;
            double prevTime = TimeSpan.Parse(timeStudyData[prevIndex].Timestamp).TotalSeconds;
            double currentTime = TimeSpan.Parse(timeStudyData[startIndex].Timestamp).TotalSeconds;
            timeStudyData[prevIndex].TimeInSeconds = Math.Round(currentTime - prevTime, 2);
        }
        
        // Recalculate the current entry's duration (to next mark)
        double thisTime = TimeSpan.Parse(timeStudyData[startIndex].Timestamp).TotalSeconds;
        if (startIndex < timeStudyData.Count - 1)
        {
            double nextTime = TimeSpan.Parse(timeStudyData[startIndex + 1].Timestamp).TotalSeconds;
            timeStudyData[startIndex].TimeInSeconds = Math.Round(nextTime - thisTime, 2);
        }
        else
        {
            // Last entry: duration until end of video
            if (HasVideo)
            {
                timeStudyData[startIndex].TimeInSeconds = Math.Round(VideoDurationSeconds - thisTime, 2);
            }
            else
            {
                timeStudyData[startIndex].TimeInSeconds = 0;
            }
        }
        
        // Defer UI updates
        Dispatcher.InvokeAsync(() =>
        {
            TimeStudyGrid.Items.Refresh();
            UpdateSegmentSummary();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateTimelineAndMarkers()
    {
        // Batch timeline and marker updates together
        UpdateTimeline();
        UpdateVideoMarkers();
    }
    
    private void UpdateSegmentSummary()
    {
        // Group data by segment and calculate totals
        var segmentStats = timeStudyData
            .GroupBy(e => e.Segment)
            .Select(g => new
            {
                Segment = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => e.TimeInSeconds),
                AvgTime = g.Average(e => e.TimeInSeconds)
            })
            .OrderBy(s => s.Segment)
            .ToList();
        
        // Update segment visibility and stats
        var activeSegments = new HashSet<int>(segmentStats.Select(s => s.Segment));
        
        // Segment 0 (M key)
        Segment0Label.Visibility = activeSegments.Contains(0) ? Visibility.Visible : Visibility.Collapsed;
        Segment0Track.Visibility = activeSegments.Contains(0) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(0))
        {
            var stat = segmentStats.First(s => s.Segment == 0);
            Segment0Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Segment 1
        Segment1Label.Visibility = activeSegments.Contains(1) ? Visibility.Visible : Visibility.Collapsed;
        Segment1Track.Visibility = activeSegments.Contains(1) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(1))
        {
            var stat = segmentStats.First(s => s.Segment == 1);
            Segment1Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Segment 2
        Segment2Label.Visibility = activeSegments.Contains(2) ? Visibility.Visible : Visibility.Collapsed;
        Segment2Track.Visibility = activeSegments.Contains(2) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(2))
        {
            var stat = segmentStats.First(s => s.Segment == 2);
            Segment2Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Segment 3
        Segment3Label.Visibility = activeSegments.Contains(3) ? Visibility.Visible : Visibility.Collapsed;
        Segment3Track.Visibility = activeSegments.Contains(3) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(3))
        {
            var stat = segmentStats.First(s => s.Segment == 3);
            Segment3Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Segment 4
        Segment4Label.Visibility = activeSegments.Contains(4) ? Visibility.Visible : Visibility.Collapsed;
        Segment4Track.Visibility = activeSegments.Contains(4) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(4))
        {
            var stat = segmentStats.First(s => s.Segment == 4);
            Segment4Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Segment 5
        Segment5Label.Visibility = activeSegments.Contains(5) ? Visibility.Visible : Visibility.Collapsed;
        Segment5Track.Visibility = activeSegments.Contains(5) ? Visibility.Visible : Visibility.Collapsed;
        if (activeSegments.Contains(5))
        {
            var stat = segmentStats.First(s => s.Segment == 5);
            Segment5Stats.Text = $"{stat.Count} × {stat.AvgTime:F2}s avg\n∑ {stat.TotalTime:F1}s total";
        }
        
        // Build summary text for status bar
        var summaryText = new System.Text.StringBuilder("Segment Totals: ");
        foreach (var stat in segmentStats)
        {
            string segName = stat.Segment == 0 ? "M" : stat.Segment.ToString();
            summaryText.Append($"[{segName}: {stat.Count} entries, {stat.TotalTime:F2}s total, {stat.AvgTime:F2}s avg] ");
        }
        
        // Update status text
        if (segmentStats.Any())
        {
            StatusText.Text = summaryText.ToString().TrimEnd();
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate element library dropdown
        PopulateElementLibrary();
        
        DrawTimeRuler();
        
        // Wire up middle mouse button events for timeline panning
        TimeRulerCanvas.MouseDown += (s, args) =>
        {
            if (args.MiddleButton == MouseButtonState.Pressed)
            {
                TimelineCanvas_MouseMiddleButtonDown(s, args);
            }
        };
        TimeRulerCanvas.MouseUp += (s, args) =>
        {
            if (args.MiddleButton == MouseButtonState.Released)
            {
                TimelineCanvas_MouseMiddleButtonUp(s, args);
            }
        };
        
        // Scale video to fit window on launch
        if (HasVideo)
        {
            FitVideoToWindow();
        }
    }
    
    private void FitVideoToWindow()
    {
        if (!HasVideo || baseVideoWidth == 0 || baseVideoHeight == 0)
            return;
            
        // Get available space in the Border
        double availableWidth = VideoBorder.ActualWidth - 20; // Account for margins
        double availableHeight = VideoBorder.ActualHeight - 20;
        
        if (availableWidth <= 0 || availableHeight <= 0)
            return;
        
        // Calculate zoom to fit based on base video dimensions
        double zoomX = availableWidth / baseVideoWidth;
        double zoomY = availableHeight / baseVideoHeight;
        
        // Use the smaller zoom to ensure it fits both dimensions
        currentZoom = Math.Min(zoomX, zoomY);
        currentZoom = Math.Max(0.1, Math.Min(4.0, currentZoom)); // Clamp between 0.1x and 4x
        
        ApplyZoom();
    }

    // Enhanced Keyboard Shortcuts
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Ignore keyboard shortcuts when typing in a text field
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox)
        {
            return;
        }
        
        // Space bar: Play/Pause toggle
        if (e.Key == Key.Space && HasVideo)
        {
            if (timer.IsEnabled)
            {
                Pause_Click(sender, e);
            }
            else
            {
                Play_Click(sender, e);
            }
            e.Handled = true;
            return;
        }

        // A key: Backward 3 seconds (Shift+A: 10 seconds)
        if (e.Key == Key.A && HasVideo)
        {
            var position = CurrentPositionSeconds;
            var jumpSeconds = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 3;
            CurrentPositionSeconds = Math.Max(0, position - jumpSeconds);
            UpdateTimeline();
            StatusText.Text = $"Jumped back {jumpSeconds}s";
            e.Handled = true;
            return;
        }

        // D key: Forward 3 seconds (Shift+D: 10 seconds)
        if (e.Key == Key.D && HasVideo)
        {
            var position = CurrentPositionSeconds;
            var duration = VideoDurationSeconds;
            var jumpSeconds = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 3;
            CurrentPositionSeconds = Math.Min(duration, position + jumpSeconds);
            UpdateTimeline();
            StatusText.Text = $"Jumped forward {jumpSeconds}s";
            e.Handled = true;
            return;
        }

        // Left Arrow: Rewind 1 second
        if (e.Key == Key.Left && HasVideo)
        {
            var position = CurrentPositionSeconds;
            CurrentPositionSeconds = Math.Max(0, position - 1);
            UpdateTimeline();
            e.Handled = true;
            return;
        }

        // Right Arrow: Forward 1 second
        if (e.Key == Key.Right && HasVideo)
        {
            var position = CurrentPositionSeconds;
            var duration = VideoDurationSeconds;
            CurrentPositionSeconds = Math.Min(duration, position + 1);
            UpdateTimeline();
            e.Handled = true;
            return;
        }

        // Comma key: Previous frame
        if (e.Key == Key.OemComma && HasVideo)
        {
            PrevFrame_Click(sender, e);
            e.Handled = true;
            return;
        }

        // Period key: Next frame
        if (e.Key == Key.OemPeriod && HasVideo)
        {
            NextFrame_Click(sender, e);
            e.Handled = true;
            return;
        }

        // M key or Ctrl+M: Add marker
        if ((e.Key == Key.M || (e.Key == Key.M && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)) && HasVideo)
        {
            AddTimestamp_Click(sender, e);
            e.Handled = true;
            return;
        }

        // S key: Start element
        if (e.Key == Key.S && HasVideo && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            StartElement_Click(sender, e);
            e.Handled = true;
            return;
        }

        // E key: End element
        if (e.Key == Key.E && HasVideo && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            EndElement_Click(sender, e);
            e.Handled = true;
            return;
        }

        // Number keys 1-5: Add marker with segment number
        // Shift+1-5: Add "Away/Waiting" marker with segment number
        if (HasVideo)
        {
            int segmentNumber = 0;
            if (e.Key == Key.D1 || e.Key == Key.NumPad1) segmentNumber = 1;
            else if (e.Key == Key.D2 || e.Key == Key.NumPad2) segmentNumber = 2;
            else if (e.Key == Key.D3 || e.Key == Key.NumPad3) segmentNumber = 3;
            else if (e.Key == Key.D4 || e.Key == Key.NumPad4) segmentNumber = 4;
            else if (e.Key == Key.D5 || e.Key == Key.NumPad5) segmentNumber = 5;

            if (segmentNumber > 0)
            {
                // Check if Shift is pressed
                bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                
                if (shiftPressed)
                {
                    // Shift+Number: Add "Away/Waiting" timestamp
                    AddAwayWaitingTimestamp(segmentNumber);
                }
                else
                {
                    // Just number: Normal timestamp with segment
                    AddTimestampWithSegment(segmentNumber);
                }
                e.Handled = true;
                return;
            }
        }

        // Up Arrow or + key: Increase speed
        if ((e.Key == Key.Up || e.Key == Key.OemPlus || e.Key == Key.Add) && HasVideo)
        {
            int currentIndex = SpeedComboBox.SelectedIndex;
            if (currentIndex < SpeedComboBox.Items.Count - 1)
            {
                SpeedComboBox.SelectedIndex = currentIndex + 1;
            }
            e.Handled = true;
            return;
        }

        // Down Arrow or - key: Decrease speed
        if ((e.Key == Key.Down || e.Key == Key.OemMinus || e.Key == Key.Subtract) && HasVideo)
        {
            int currentIndex = SpeedComboBox.SelectedIndex;
            if (currentIndex > 0)
            {
                SpeedComboBox.SelectedIndex = currentIndex - 1;
            }
            e.Handled = true;
            return;
        }

        // Delete key: Delete selected entry
        if (e.Key == Key.Delete && TimeStudyGrid.SelectedItem != null)
        {
            DeleteSelected_Click(sender, e);
            e.Handled = true;
            return;
        }
    }
    
    private void SetLowQualityMode(bool enable)
    {
        if (enable && !isLowQualityMode)
        {
            // Reduce render quality for better performance
            RenderOptions.SetBitmapScalingMode(VideoPlayer, BitmapScalingMode.LowQuality);
            RenderOptions.SetCachingHint(VideoPlayer, CachingHint.Cache);
            
            // Apply motion blur for smoother high-speed playback
            if (smoothingEnabled && currentSpeedRatio > 1.0 && VideoPlayer.Effect is BlurEffect blur)
            {
                double blurAmount = Math.Min((currentSpeedRatio - 1.0) * 1.5, 4.0);
                blur.Radius = blurAmount;
            }
            
            isLowQualityMode = true;
        }
        else if (!enable && isLowQualityMode)
        {
            // Restore high quality rendering
            RenderOptions.SetBitmapScalingMode(VideoPlayer, BitmapScalingMode.HighQuality);
            RenderOptions.SetCachingHint(VideoPlayer, CachingHint.Unspecified);
            if (VideoPlayer.Effect is BlurEffect blur)
                blur.Radius = 0; // Remove blur
            isLowQualityMode = false;
        }
    }
    
    private void ScrubThrottleTimer_Tick(object? sender, EventArgs e)
    {
        if (hasPendingScrub)
        {
            CurrentPositionSeconds = pendingScrubPosition.TotalSeconds;
            hasPendingScrub = false;
            // Note: UpdatePlaybackIndicator() is now called by VSync handler every frame
        }
    }

    private void DrawTimeRuler()
    {
        TimeRulerCanvas.Children.Clear();
        
        if (!HasVideo)
            return;
        
        // Use centralized width calculation for perfect sync
        double canvasWidth = CalculateCanvasWidth();
        SyncAllCanvasWidths();
        
        double totalSeconds = VideoDurationSeconds;
        double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
        
        // Adjust tick interval based on zoom level to prevent overlap
        // Calculate minimum pixels between labels (timestamp width + padding)
        double minLabelSpacing = 80; // Minimum pixels between label centers (increased for hh:mm:ss format)
        
        // Calculate what time interval gives us that spacing
        double minTimeInterval = minLabelSpacing / effectivePixelsPerSecond;
        
        // Round up to nice intervals: 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, etc.
        double[] niceIntervals = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600 };
        double labelInterval = niceIntervals[niceIntervals.Length - 1];
        foreach (double interval in niceIntervals)
        {
            if (interval >= minTimeInterval)
            {
                labelInterval = interval;
                break;
            }
        }
        
        // Minor ticks at 1/5th of label interval or 1 second minimum
        double tickInterval = Math.Max(1.0, labelInterval / 5.0);
        
        // Draw time markers
        for (double time = 0; time <= totalSeconds; time += tickInterval)
        {
            double x = time * effectivePixelsPerSecond;
            
            // Draw tick mark
            bool isMajor = (time % labelInterval == 0);
            Line tick = new Line
            {
                X1 = x,
                Y1 = isMajor ? 20 : 35,
                X2 = x,
                Y2 = 50,
                Stroke = Brushes.Gray,
                StrokeThickness = isMajor ? 2 : 1
            };
            TimeRulerCanvas.Children.Add(tick);
            
            // Draw time label
            if (isMajor)
            {
                // Create border with background for better readability
                Border labelBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                    Padding = new Thickness(6, 3, 6, 3),
                    CornerRadius = new CornerRadius(3),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };
                
                TextBlock label = new TextBlock
                {
                    Text = TimeSpan.FromSeconds(time).ToString(@"hh\:mm\:ss"),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold
                };
                
                labelBorder.Child = label;
                Canvas.SetLeft(labelBorder, x - 35);
                Canvas.SetTop(labelBorder, 0);
                TimeRulerCanvas.Children.Add(labelBorder);
            }
        }
    }

    private void SegmentTracksScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isScrollSyncing)
            return;
            
        _isScrollSyncing = true;
        
        try
        {
            // Critical: Sync vertical scrolling of segment labels with segment tracks
            if (sender == SegmentTracksScroll && e.VerticalChange != 0)
            {
                SegmentLabelsScroll.ScrollToVerticalOffset(e.VerticalOffset);
            }
            
            // Critical: Sync horizontal scrolling of time ruler with segment tracks
            if (sender == SegmentTracksScroll && e.HorizontalChange != 0)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
        }
        finally
        {
            _isScrollSyncing = false;
        }
    }

    private double CalculateCanvasWidth()
    {
        if (!HasVideo)
            return 2000;
        
        double totalSeconds = VideoDurationSeconds;
        double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
        double canvasWidth = Math.Max(totalSeconds * effectivePixelsPerSecond, 2000);
        
        // Add 10% padding to prevent clipping at the end
        canvasWidth = canvasWidth * 1.1;
        
        return canvasWidth;
    }

    private void SyncAllCanvasWidths()
    {
        double canvasWidth = CalculateCanvasWidth();
        
        // Set time ruler width
        TimeRulerCanvas.Width = canvasWidth;
        
        // Set all segment canvas widths to EXACT same value
        Segment0Canvas.Width = canvasWidth;
        Segment1Canvas.Width = canvasWidth;
        Segment2Canvas.Width = canvasWidth;
        Segment3Canvas.Width = canvasWidth;
        Segment4Canvas.Width = canvasWidth;
        Segment5Canvas.Width = canvasWidth;
    }
    
    private void UpdateTimeline()
    {
        // Clear all segment canvases
        Segment0Canvas.Children.Clear();
        Segment1Canvas.Children.Clear();
        Segment2Canvas.Children.Clear();
        Segment3Canvas.Children.Clear();
        Segment4Canvas.Children.Clear();
        Segment5Canvas.Children.Clear();
        
        if (!HasVideo)
            return;
        
        // Use centralized width calculation for perfect sync
        double canvasWidth = CalculateCanvasWidth();
        SyncAllCanvasWidths();
        
        double totalSeconds = VideoDurationSeconds;
        double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
        
        // Add clickable backgrounds to all canvases for easy scrubbing
        foreach (var canvas in new[] { Segment0Canvas, Segment1Canvas, Segment2Canvas, Segment3Canvas, Segment4Canvas, Segment5Canvas })
        {
            Rectangle clickableBase = new Rectangle
            {
                Width = canvasWidth,
                Height = 60,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            clickableBase.MouseLeftButtonDown += TimelineCanvas_MouseLeftButtonDown;
            clickableBase.MouseDown += (s, args) =>
            {
                if (args.MiddleButton == MouseButtonState.Pressed)
                {
                    TimelineCanvas_MouseMiddleButtonDown(s, args);
                }
            };
            clickableBase.MouseUp += (s, args) =>
            {
                if (args.MiddleButton == MouseButtonState.Released)
                {
                    TimelineCanvas_MouseMiddleButtonUp(s, args);
                }
            };
            clickableBase.MouseMove += TimelineCanvas_MouseMove;
            Canvas.SetLeft(clickableBase, 0);
            Canvas.SetTop(clickableBase, 0);
            canvas.Children.Add(clickableBase);
        }
        
        // Color palette for segments
        var segmentColors = new Dictionary<int, Color>
        {
            { 0, Color.FromRgb(153, 153, 153) },  // Gray
            { 1, Color.FromRgb(255, 140, 0) },    // Orange
            { 2, Color.FromRgb(30, 144, 255) },   // Blue  
            { 3, Color.FromRgb(255, 20, 147) },   // Pink
            { 4, Color.FromRgb(255, 215, 0) },    // Gold
            { 5, Color.FromRgb(50, 205, 50) }     // Green
        };
        
        // Group entries by segment
        var segmentGroups = timeStudyData.GroupBy(e => e.Segment).ToList();
        
        foreach (var segmentGroup in segmentGroups)
        {
            int segment = segmentGroup.Key;
            var entries = segmentGroup.OrderBy(e => TimeSpan.Parse(e.Timestamp)).ToList();
            
            Canvas targetCanvas = segment switch
            {
                0 => Segment0Canvas,
                1 => Segment1Canvas,
                2 => Segment2Canvas,
                3 => Segment3Canvas,
                4 => Segment4Canvas,
                5 => Segment5Canvas,
                _ => Segment0Canvas
            };
            
            Color segmentColor = segmentColors.ContainsKey(segment) ? segmentColors[segment] : segmentColors[0];
            
            // Draw segments for this track
            for (int i = 0; i < entries.Count; i++)
            {
                double startTime = TimeSpan.Parse(entries[i].Timestamp).TotalSeconds;
                double endTime = (i < entries.Count - 1) 
                    ? TimeSpan.Parse(entries[i + 1].Timestamp).TotalSeconds 
                    : totalSeconds;
                
                double startX = startTime * effectivePixelsPerSecond;
                double width = (endTime - startTime) * effectivePixelsPerSecond;
                
                // Draw clickable background for the entire segment (for scrubbing)
                Rectangle clickableBackground = new Rectangle
                {
                    Width = width,
                    Height = 60,
                    Fill = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Tag = entries[i]
                };
                clickableBackground.MouseLeftButtonDown += SegmentBar_Click;
                Canvas.SetLeft(clickableBackground, startX);
                Canvas.SetTop(clickableBackground, 0);
                targetCanvas.Children.Add(clickableBackground);
                
                // Draw segment bar (visual indicator, smaller than clickable area)
                Rectangle segmentBar = new Rectangle
                {
                    Width = width,
                    Height = 35,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(200, segmentColor.R, segmentColor.G, segmentColor.B),
                        Color.FromArgb(150, segmentColor.R, segmentColor.G, segmentColor.B),
                        90),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, segmentColor.R, segmentColor.G, segmentColor.B)),
                    StrokeThickness = 2,
                    RadiusX = 4,
                    RadiusY = 4,
                    IsHitTestVisible = false,
                    Tag = entries[i],
                    ToolTip = $"Entry #{i + 1}\nElement: {entries[i].ElementName}\nTime: {entries[i].Timestamp}\nDuration: {entries[i].TimeInSeconds:F2}s\nDescription: {entries[i].Description}"
                };
                Canvas.SetLeft(segmentBar, startX);
                Canvas.SetTop(segmentBar, 3);
                targetCanvas.Children.Add(segmentBar);
                
                // Draw segment label
                TextBlock segmentLabel = new TextBlock
                {
                    Text = string.IsNullOrEmpty(entries[i].ElementName) 
                        ? $"#{i + 1}" 
                        : entries[i].ElementName,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Padding = new Thickness(3, 1, 3, 1),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(segmentLabel, startX + 5);
                Canvas.SetTop(segmentLabel, 5);
                targetCanvas.Children.Add(segmentLabel);
                
                // Draw duration label
                if (width > 50)
                {
                    TextBlock durationLabel = new TextBlock
                    {
                        Text = $"{entries[i].TimeInSeconds:F1}s",
                        Foreground = Brushes.White,
                        FontSize = 8,
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                        Padding = new Thickness(2, 1, 2, 1),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(durationLabel, startX + width - 35);
                    Canvas.SetTop(durationLabel, 5);
                    targetCanvas.Children.Add(durationLabel);
                }
                
                // Draw timestamp marker
                if (width > 80 && entries[i].ThumbnailImage != null)
                {
                    double thumbnailWidth = Math.Min(50, width - 10);
                    Border thumbnailBorder = new Border
                    {
                        Width = thumbnailWidth,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                        BorderBrush = new SolidColorBrush(segmentColor),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(2),
                        ClipToBounds = true,
                        IsHitTestVisible = false
                    };
                    
                    Image frameImage = new Image
                    {
                        Source = entries[i].ThumbnailImage,
                        Stretch = Stretch.UniformToFill
                    };
                    
                    thumbnailBorder.Child = frameImage;
                    Canvas.SetLeft(thumbnailBorder, startX + width - thumbnailWidth - 5);
                    Canvas.SetTop(thumbnailBorder, 3);
                    targetCanvas.Children.Add(thumbnailBorder);
                }
            }
        }
        
        DrawTimeRuler();
        UpdatePlaybackIndicator();
    }

    private BitmapSource? CaptureVideoFrame(double timeInSeconds)
    {
        try
        {
            if (!HasVideo)
                return null;
            
            // Store current position and video index
            double originalPosition = CurrentPositionSeconds;
            int originalVideoIndex = currentVideoIndex;
            
            // Seek to the target time
            CurrentPositionSeconds = timeInSeconds;
            
            // Give the video time to seek and render the frame
            // Use multiple dispatcher invokes with different priorities to ensure video has rendered
            VideoPlayer.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            VideoPlayer.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Threading.Thread.Sleep(50); // Small delay to ensure frame is fully rendered
            
            // Capture the frame
            RenderTargetBitmap renderTarget = new RenderTargetBitmap(
                (int)VideoPlayer.ActualWidth,
                (int)VideoPlayer.ActualHeight,
                96, 96,
                PixelFormats.Pbgra32);
            
            renderTarget.Render(VideoPlayer);
            
            // Restore original position only if we're still trying to regenerate thumbnails
            // This prevents issues with video switching
            if (originalVideoIndex == currentVideoIndex || videoSegments.Count <= 1)
            {
                CurrentPositionSeconds = originalPosition;
            }
            else
            {
                // We switched videos, restore carefully
                System.Threading.Thread.Sleep(50);
                CurrentPositionSeconds = originalPosition;
            }
            
            return renderTarget;
        }
        catch
        {
            return null;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Called on every frame for VSync-synchronized updates
        // Skip frames for better performance (update every 3rd frame during playback)
        if (!timer.IsEnabled && !isScrubbing)
        {
            // Only update when paused/stopped and not scrubbing
            if (HasVideo)
            {
                UpdatePlaybackIndicator();
            }
            return;
        }
        
        // During playback, skip frames for performance
        frameSkipCounter++;
        if (frameSkipCounter % 2 == 0) // Update every other frame
        {
            if (HasVideo)
            {
                UpdatePlaybackIndicator();
            }
        }
    }
    
    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (HasVideo)
        {
            CurrentTime.Text = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.ff");
            UpdateFrameCounter();
        }
    }

    private void UpdatePlaybackIndicator()
    {
        if (!HasVideo)
        {
            PlaybackIndicatorLine.Visibility = Visibility.Collapsed;
            return;
        }

        double currentSeconds = CurrentPositionSeconds;
        double totalSeconds = VideoDurationSeconds;
        double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
        
        // Calculate position matching the time ruler's coordinate system (canvas coordinates)
        double canvasXPosition = currentSeconds * effectivePixelsPerSecond;
        
        // The Line is in the Grid, not the Canvas, so we need to account for scroll offset
        // Line position = Canvas position - Scroll offset
        double scrollOffset = TimelineScrollViewer.HorizontalOffset;
        double gridXPosition = canvasXPosition - scrollOffset;

        // Position the indicator line with sub-pixel precision
        PlaybackIndicatorLine.X1 = gridXPosition;
        PlaybackIndicatorLine.X2 = gridXPosition;
        PlaybackIndicatorLine.Visibility = Visibility.Visible;
        
        // Use SnapsToDevicePixels for crisp rendering
        PlaybackIndicatorLine.SnapsToDevicePixels = true;
        
        // Auto-scroll to keep indicator in view during playback
        if (timer.IsEnabled)
        {
            double viewportWidth = TimelineScrollViewer.ViewportWidth;
            double currentScroll = TimelineScrollViewer.HorizontalOffset;
            double targetScroll = -1;
            
            // If indicator is near the right edge, scroll forward
            if (canvasXPosition > currentScroll + viewportWidth - 100)
            {
                targetScroll = canvasXPosition - viewportWidth / 2;
            }
            // If indicator is near the left edge, scroll backward
            else if (canvasXPosition < currentScroll + 100)
            {
                targetScroll = Math.Max(0, canvasXPosition - 100);
            }
            
            // Sync scroll to both timeline ruler and segment tracks
            if (targetScroll >= 0)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(targetScroll);
                SegmentTracksScroll.ScrollToHorizontalOffset(targetScroll);
            }
        }
    }

    private void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Video files (*.mp4;*.avi;*.mkv;*.wmv)|*.mp4;*.avi;*.mkv;*.wmv|All files (*.*)|*.*";
        
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                // Reset project for new video
                videoSegments.Clear();
                cumulativeVideoTime = 0.0;
                currentVideoIndex = 0;
                
                currentVideoFileName = openFileDialog.FileName;
                StatusText.Text = $"Loading: {System.IO.Path.GetFileName(openFileDialog.FileName)}...";
                
                VideoPlayer.MediaOpened += VideoPlayer_MediaOpened;
                VideoPlayer.Source = new Uri(openFileDialog.FileName);
                VideoPlayer.Play();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading video: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Video load error: {ex}");
            }
        }
    }

    private void AppendVideo_Click(object sender, RoutedEventArgs e)
    {
        if (!HasVideo)
        {
            MessageBox.Show("Please load a video first before appending.", "No Video Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Video files (*.mp4;*.avi;*.mkv;*.wmv)|*.mp4;*.avi;*.mkv;*.wmv|All files (*.*)|*.*";
        
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string appendedFile = openFileDialog.FileName;
                
                // Get video duration using Shell32
                double newVideoDuration = GetVideoDuration(appendedFile);
                
                if (newVideoDuration <= 0)
                {
                    MessageBox.Show("Could not determine video duration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Add video to segments list
                videoSegments.Add(new VideoSegment
                {
                    FilePath = appendedFile,
                    StartTime = cumulativeVideoTime,
                    Duration = newVideoDuration
                });
                
                cumulativeVideoTime += newVideoDuration;
                
                StatusText.Text = $"Appended: {System.IO.Path.GetFileName(appendedFile)} - Total duration: {TimeSpan.FromSeconds(cumulativeVideoTime):hh\\:mm\\:ss}";
                
                // Redraw timeline with new duration
                DrawTimeRuler();
                UpdateTimeline();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error appending video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private double GetVideoDuration(string filePath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return 0;
            
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return 0;
            
            dynamic folder = shell.NameSpace(System.IO.Path.GetDirectoryName(filePath));
            dynamic file = folder.ParseName(System.IO.Path.GetFileName(filePath));
            
            // Property 27 is the duration
            string duration = folder.GetDetailsOf(file, 27);
            
            if (string.IsNullOrEmpty(duration))
                return 0;
            
            // Parse duration string (format: hh:mm:ss)
            var parts = duration.Split(':');
            if (parts.Length == 3)
            {
                if (int.TryParse(parts[0], out int hours) &&
                    int.TryParse(parts[1], out int minutes) &&
                    int.TryParse(parts[2], out int seconds))
                {
                    return hours * 3600 + minutes * 60 + seconds;
                }
            }
            
            return 0;
        }
        catch
        {
            return 0;
        }
    }
    
    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        VideoPlayer.MediaOpened -= VideoPlayer_MediaOpened;
        
        baseVideoWidth = VideoPlayer.NaturalVideoWidth;
        baseVideoHeight = VideoPlayer.NaturalVideoHeight;
        
        // Update all containers to match video dimensions
        VideoContainer.Width = baseVideoWidth;
        VideoContainer.Height = baseVideoHeight;
        VideoPlayer.Width = baseVideoWidth;
        VideoPlayer.Height = baseVideoHeight;
        MarkerCanvas.Width = baseVideoWidth;
        MarkerCanvas.Height = baseVideoHeight;
        
        // Only add to segments list if this is the initial load (segments is empty)
        if (videoSegments.Count == 0)
        {
            double videoDuration = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            videoSegments.Add(new VideoSegment
            {
                FilePath = currentVideoFileName,
                StartTime = 0,
                Duration = videoDuration
            });
            cumulativeVideoTime = videoDuration;
        }
        
        StatusText.Text = $"Loaded: {System.IO.Path.GetFileName(currentVideoFileName)} - Duration: {VideoPlayer.NaturalDuration.TimeSpan:hh\\:mm\\:ss} - {baseVideoWidth}x{baseVideoHeight}";
        
        DrawTimeRuler();
        UpdateTimeline();
        UpdateVideoMarkers();
        FitVideoToWindow();
        
        // Pause after the first frame to keep the preview visible
        Dispatcher.BeginInvoke(new Action(() =>
        {
            VideoPlayer.Pause();
            VideoPlayer.Position = TimeSpan.Zero;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StatusText.Text = $"Error loading video: {e.ErrorException.Message}";
        Debug.WriteLine($"MediaElement error: {e.ErrorException}");
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            VideoPlayer.Play();
            VideoPlayer.SpeedRatio = currentSpeedRatio;
            timer.Start();
            
            // Enable low quality mode for speeds > 1.5x
            SetLowQualityMode(currentSpeedRatio > 1.5);
            
            StatusText.Text = $"Playing at {currentSpeedRatio}x speed...";
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Pause();
        timer.Stop();
        SetLowQualityMode(false);
        StatusText.Text = "Paused";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Stop();
        timer.Stop();
        SetLowQualityMode(false);
        CurrentTime.Text = "00:00:00";
        
        StatusText.Text = "Stopped";
    }

    private void Rewind10_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            double newPosition = Math.Max(0, CurrentPositionSeconds - 10);
            CurrentPositionSeconds = newPosition;
            UpdateTimeline();
            StatusText.Text = $"Rewound 10 seconds - Position: {TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss")}";
        }
    }

    private void Forward10_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            double newPosition = Math.Min(VideoDurationSeconds, 
                                         CurrentPositionSeconds + 10);
            CurrentPositionSeconds = newPosition;
            UpdateTimeline();
            StatusText.Text = $"Fast forward 10 seconds - Position: {TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss")}";
        }
    }

    private void PrevFrame_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            // Assume 30 FPS (can be adjusted based on actual video FPS)
            double frameTime = 1.0 / 30.0;
            double newPosition = Math.Max(0, CurrentPositionSeconds - frameTime);
            CurrentPositionSeconds = newPosition;
            UpdateTimeline();
            UpdateFrameCounter();
            StatusText.Text = $"Previous frame - {TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.fff")}";
        }
    }

    private void NextFrame_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            // Assume 30 FPS (can be adjusted based on actual video FPS)
            double frameTime = 1.0 / 30.0;
            double newPosition = Math.Min(VideoDurationSeconds, CurrentPositionSeconds + frameTime);
            CurrentPositionSeconds = newPosition;
            UpdateTimeline();
            UpdateFrameCounter();
            StatusText.Text = $"Next frame - {TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.fff")}";
        }
    }

    private void UpdateFrameCounter()
    {
        if (HasVideo)
        {
            double fps = 30.0; // Default, could be detected from video
            int frameNumber = (int)(CurrentPositionSeconds * fps);
            FrameCounter.Text = $"Frame: {frameNumber}";
        }
    }

    private void StartElement_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            // Get element from library combobox
            string elementName = "";
            if (ElementLibraryComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                elementName = selectedItem.Content?.ToString() ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(ElementLibraryComboBox.Text))
            {
                elementName = ElementLibraryComboBox.Text;
            }
            
            var entry = new TimeStudyEntry
            {
                Timestamp = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss"),
                TimeInSeconds = 0,
                ElementName = elementName,
                Description = "START",
                Category = "",
                ThumbnailImage = CaptureVideoFrame(CurrentPositionSeconds)
            };
            
            // Find insertion point for sorted order
            int insertIndex = timeStudyData.Count;
            var entryTimeSpan = TimeSpan.Parse(entry.Timestamp);
            for (int i = timeStudyData.Count - 1; i >= 0; i--)
            {
                if (TimeSpan.Parse(timeStudyData[i].Timestamp) <= entryTimeSpan)
                {
                    insertIndex = i + 1;
                    break;
                }
                if (i == 0) insertIndex = 0;
            }
            
            timeStudyData.Insert(insertIndex, entry);
            RecalculateDurationsIncremental(insertIndex);
            UpdateTimelineAndMarkers();
            StatusText.Text = $"Started {elementName} at {entry.Timestamp}";
        }
    }

    private void EndElement_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            // Get element from library combobox
            string elementName = "";
            if (ElementLibraryComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                elementName = selectedItem.Content?.ToString() ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(ElementLibraryComboBox.Text))
            {
                elementName = ElementLibraryComboBox.Text;
            }
            
            var entry = new TimeStudyEntry
            {
                Timestamp = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss"),
                TimeInSeconds = 0,
                ElementName = elementName,
                Description = "END",
                Category = "",
                ThumbnailImage = CaptureVideoFrame(CurrentPositionSeconds)
            };
            
            // Find insertion point for sorted order
            int insertIndex = timeStudyData.Count;
            var entryTimeSpan = TimeSpan.Parse(entry.Timestamp);
            for (int i = timeStudyData.Count - 1; i >= 0; i--)
            {
                if (TimeSpan.Parse(timeStudyData[i].Timestamp) <= entryTimeSpan)
                {
                    insertIndex = i + 1;
                    break;
                }
                if (i == 0) insertIndex = 0;
            }
            
            timeStudyData.Insert(insertIndex, entry);
            RecalculateDurationsIncremental(insertIndex);
            UpdateTimelineAndMarkers();
            StatusText.Text = $"Ended {elementName} at {entry.Timestamp}";
        }
    }

    private void SetSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag != null)
        {
            currentSpeedRatio = double.Parse(menuItem.Tag.ToString()!);
            if (HasVideo)
            {
                VideoPlayer.SpeedRatio = currentSpeedRatio;
                StatusText.Text = $"Playback speed set to {currentSpeedRatio}x";
                
                // Update combobox to match
                for (int i = 0; i < SpeedComboBox.Items.Count; i++)
                {
                    if (((ComboBoxItem)SpeedComboBox.Items[i]).Tag.ToString() == currentSpeedRatio.ToString())
                    {
                        SpeedComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
    }

    private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            currentSpeedRatio = double.Parse(item.Tag.ToString()!);
            if (HasVideo)
            {
                VideoPlayer.SpeedRatio = currentSpeedRatio;
                
                // Update speed display
                SpeedRatioText.Text = $"Speed: {currentSpeedRatio}x";
                
                // Adjust quality based on speed when playing
                if (timer.IsEnabled)
                {
                    SetLowQualityMode(currentSpeedRatio > 1.5);
                }
                
                StatusText.Text = $"Playback speed: {currentSpeedRatio}x";
            }
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = Math.Min(currentZoom + 0.25, 4.0);
        ApplyZoom();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = Math.Max(currentZoom - 0.25, 0.1);
        ApplyZoom();
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        // Ensure zoom is within bounds
        currentZoom = Math.Max(0.1, Math.Min(4.0, currentZoom));
        
        // Use WPF LayoutTransform for zoom (allows panning)
        VideoScaleTransform.ScaleX = currentZoom;
        VideoScaleTransform.ScaleY = currentZoom;
        
        ZoomLabel.Text = $"{currentZoom * 100:F0}%";
        StatusText.Text = $"Zoom: {currentZoom * 100:F0}%";
        
        Debug.WriteLine($"ApplyZoom: zoom={currentZoom:F2}, ScaleTransform={VideoScaleTransform.ScaleX}");
    }

    
    private void AddTimestamp_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            double currentTime = CurrentPositionSeconds;
            
            // Get description text (skip if it's the placeholder)
            string description = AnnotationText.Text == "Enter description for timestamp..." ? "" : AnnotationText.Text;
            
            // Get element from library combobox
            string elementName = "";
            if (ElementLibraryComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                elementName = selectedItem.Content?.ToString() ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(ElementLibraryComboBox.Text))
            {
                // User typed custom element
                elementName = ElementLibraryComboBox.Text;
            }
            
            var entry = new TimeStudyEntry
            {
                Timestamp = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss"),
                TimeInSeconds = 0, // Will be recalculated
                ElementName = elementName,
                Description = description,
                Category = "",
                ThumbnailImage = CaptureVideoFrame(currentTime) // Capture thumbnail
            };
            
            // Find insertion point for sorted order (binary search would be faster for large lists)
            int insertIndex = timeStudyData.Count;
            var entryTimeSpan = TimeSpan.Parse(entry.Timestamp);
            for (int i = timeStudyData.Count - 1; i >= 0; i--)
            {
                if (TimeSpan.Parse(timeStudyData[i].Timestamp) <= entryTimeSpan)
                {
                    insertIndex = i + 1;
                    break;
                }
                if (i == 0) insertIndex = 0;
            }
            
            timeStudyData.Insert(insertIndex, entry);
            
            // Recalculate durations only for affected entries (this entry and next in same segment)
            RecalculateDurationsIncremental(insertIndex);
            
            // Reset description field to placeholder
            AnnotationText.Text = "Enter description for timestamp...";
            AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            StatusText.Text = $"Added entry at {entry.Timestamp}";
            
            // Batch updates
            UpdateTimelineAndMarkers();
        }
        else
        {
            MessageBox.Show("Please load a video first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddTimestampWithSegment(int segmentNumber)
    {
        if (HasVideo)
        {
            double currentTime = CurrentPositionSeconds;
            
            // Get description text (skip if it's the placeholder)
            string description = AnnotationText.Text == "Enter description for timestamp..." ? "" : AnnotationText.Text;
            
            // Get element from library combobox
            string elementName = "";
            if (ElementLibraryComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                elementName = selectedItem.Content?.ToString() ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(ElementLibraryComboBox.Text))
            {
                // User typed custom element
                elementName = ElementLibraryComboBox.Text;
            }
            
            var entry = new TimeStudyEntry
            {
                Timestamp = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss"),
                TimeInSeconds = 0, // Will be recalculated
                ElementName = elementName,
                Description = description,
                Category = "",
                Segment = segmentNumber,
                ThumbnailImage = CaptureVideoFrame(currentTime) // Capture thumbnail
            };
            
            // Find insertion point for sorted order
            int insertIndex = timeStudyData.Count;
            var entryTimeSpan = TimeSpan.Parse(entry.Timestamp);
            for (int i = timeStudyData.Count - 1; i >= 0; i--)
            {
                if (TimeSpan.Parse(timeStudyData[i].Timestamp) <= entryTimeSpan)
                {
                    insertIndex = i + 1;
                    break;
                }
                if (i == 0) insertIndex = 0;
            }
            
            timeStudyData.Insert(insertIndex, entry);
            
            // Recalculate durations incrementally
            RecalculateDurationsIncremental(insertIndex);
            
            // Reset description field to placeholder
            AnnotationText.Text = "Enter description for timestamp...";
            AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            StatusText.Text = $"Added entry at {entry.Timestamp} (Segment {segmentNumber})";
            
            UpdateTimelineAndMarkers();
        }
        else
        {
            MessageBox.Show("Please load a video first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddAwayWaitingTimestamp(int segmentNumber)
    {
        if (HasVideo)
        {
            double currentTime = CurrentPositionSeconds;
            
            var entry = new TimeStudyEntry
            {
                Timestamp = TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss"),
                TimeInSeconds = 0,
                ElementName = "Away/Waiting",
                Description = "",
                Category = "",
                Segment = segmentNumber,
                ThumbnailImage = CaptureVideoFrame(currentTime) // Capture thumbnail
            };
            
            // Find insertion point for sorted order
            int insertIndex = timeStudyData.Count;
            var entryTimeSpan = TimeSpan.Parse(entry.Timestamp);
            for (int i = timeStudyData.Count - 1; i >= 0; i--)
            {
                if (TimeSpan.Parse(timeStudyData[i].Timestamp) <= entryTimeSpan)
                {
                    insertIndex = i + 1;
                    break;
                }
                if (i == 0) insertIndex = 0;
            }
            
            timeStudyData.Insert(insertIndex, entry);
            
            // Recalculate durations incrementally
            RecalculateDurationsIncremental(insertIndex);
            
            StatusText.Text = $"Away/Waiting timestamp added at {entry.Timestamp} (Segment {segmentNumber})";
            
            UpdateTimelineAndMarkers();
        }
    }

    private void UpdateVideoMarkers()
    {
        // Keep markers only on timeline, not on video
        MarkerCanvas.Children.Clear();
    }

    private void ClearData_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Are you sure you want to clear all data?", "Confirm Clear", 
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            timeStudyData.Clear();
            StatusText.Text = "Data cleared";
            UpdateTimeline();
            UpdateVideoMarkers();
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (TimeStudyGrid.SelectedItem != null)
        {
            var selectedEntry = (TimeStudyEntry)TimeStudyGrid.SelectedItem;
            timeStudyData.Remove(selectedEntry);
            
            // Recalculate durations after deletion
            RecalculateDurations();
            
            StatusText.Text = "Entry deleted";
            UpdateTimeline();
            UpdateVideoMarkers();
        }
    }

    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
        saveFileDialog.DefaultExt = "csv";
        
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                {
                    // Write video file name as first line
                    writer.WriteLine($"Video File: {currentVideoFileName}");
                    
                    // Write header
                    writer.WriteLine("Timestamp,Segment,Duration (sec),Element,Description,Observations,People,Category");
                    
                    // Write data
                    foreach (var entry in timeStudyData)
                    {
                        writer.WriteLine($"{entry.Timestamp},{entry.Segment},{entry.TimeInSeconds},\"{entry.ElementName}\",\"{entry.Description}\",\"{entry.Observations}\",{entry.People},\"{entry.Category}\"");
                    }
                }
                
                StatusText.Text = $"Data exported to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                MessageBox.Show("Data exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ImportData_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
        openFileDialog.DefaultExt = "csv";
        
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                timeStudyData.Clear();
                
                using (StreamReader reader = new StreamReader(openFileDialog.FileName))
                {
                    // Read first line - check if it's the video file name
                    string? firstLine = reader.ReadLine();
                    if (firstLine != null && firstLine.StartsWith("Video File: "))
                    {
                        // Extract video file name
                        string videoFileName = firstLine.Substring(12);
                        // Optional: could validate or load the video here
                        // For now, just skip to header line
                        firstLine = reader.ReadLine();
                    }
                    // firstLine should now be the header line, skip it
                    
                    while (!reader.EndOfStream)
                    {
                        string? line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        // Parse CSV line (handling quoted fields)
                        var fields = ParseCsvLine(line);
                        
                        if (fields.Count >= 8)
                        {
                            var entry = new TimeStudyEntry
                            {
                                Timestamp = fields[0],
                                Segment = int.TryParse(fields[1], out int segment) ? segment : 0,
                                TimeInSeconds = double.TryParse(fields[2], out double duration) ? duration : 0,
                                ElementName = fields[3],
                                Description = fields[4],
                                Observations = fields[5],
                                People = fields[6],
                                Category = fields.Count > 7 ? fields[7] : ""
                            };
                            timeStudyData.Add(entry);
                        }
                    }
                }
                
                UpdateTimeline();
                UpdateVideoMarkers();
                UpdateSegmentSummary();
                StatusText.Text = $"Imported {timeStudyData.Count} entries from {System.IO.Path.GetFileName(openFileDialog.FileName)}";
                MessageBox.Show($"Successfully imported {timeStudyData.Count} entries!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing data: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var currentField = new StringBuilder();
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        
        fields.Add(currentField.ToString());
        return fields;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Video Time Study Project (*.vtsp)|*.vtsp|All files (*.*)|*.*";
        saveFileDialog.DefaultExt = "vtsp";
        
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                var projectData = new ProjectData
                {
                    VideoSegments = videoSegments,
                    TimeStudyEntries = timeStudyData.Select(e => new TimeStudyEntryData
                    {
                        Timestamp = e.Timestamp,
                        TimeInSeconds = e.TimeInSeconds,
                        ElementName = e.ElementName,
                        Description = e.Description,
                        Observations = e.Observations,
                        People = e.People,
                        Category = e.Category,
                        Segment = e.Segment,
                        ThumbnailBase64 = e.ThumbnailImage != null ? BitmapToBase64(e.ThumbnailImage) : null
                    }).ToList(),
                    SegmentNames = segmentNames,
                    ElementLibrary = elementLibrary,
                    Zones = workZones.Select(z => new ZoneSaveData
                    {
                        Name = z.Name,
                        X = z.X,
                        Y = z.Y,
                        Width = z.Width,
                        Height = z.Height,
                        Color = z.Color,
                        MotionThreshold = z.MotionThreshold,
                        MinMotionPixels = z.MinMotionPixels
                    }).ToList(),
                    ZoneEvents = workZones.SelectMany(z => z.Events.Select(evt => new ZoneEventData
                    {
                        ZoneName = z.Name,
                        VideoTimeInSeconds = evt.VideoTimeInSeconds,
                        EventType = evt.EventType,
                        Timestamp = evt.Timestamp
                    })).ToList()
                };
                
                string json = JsonSerializer.Serialize(projectData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(saveFileDialog.FileName, json);
                
                StatusText.Text = $"Project saved: {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                MessageBox.Show("Project saved successfully!", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving project: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Video Time Study Project (*.vtsp)|*.vtsp|All files (*.*)|*.*";
        
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(openFileDialog.FileName);
                var projectData = JsonSerializer.Deserialize<ProjectData>(json);
                
                if (projectData == null)
                {
                    MessageBox.Show("Failed to load project data.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Restore video segments
                videoSegments = projectData.VideoSegments ?? new List<VideoSegment>();
                cumulativeVideoTime = videoSegments.Sum(v => v.Duration);
                
                // Load first video if available
                if (videoSegments.Count > 0)
                {
                    currentVideoFileName = videoSegments[0].FilePath;
                    if (File.Exists(currentVideoFileName))
                    {
                        VideoPlayer.Source = new Uri(currentVideoFileName);
                        VideoPlayer.Play();
                        VideoPlayer.MediaOpened += (s, args) => VideoPlayer.Pause();
                    }
                    else
                    {
                        MessageBox.Show($"Video file not found: {currentVideoFileName}\nPlease ensure video files are in their original locations.", 
                            "Video Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                
                // Restore time study data
                timeStudyData.Clear();
                int missingThumbnails = 0;
                foreach (var entryData in projectData.TimeStudyEntries ?? new List<TimeStudyEntryData>())
                {
                    var entry = new TimeStudyEntry
                    {
                        Timestamp = entryData.Timestamp,
                        TimeInSeconds = entryData.TimeInSeconds,
                        ElementName = entryData.ElementName,
                        Description = entryData.Description,
                        Observations = entryData.Observations,
                        People = entryData.People,
                        Category = entryData.Category,
                        Segment = entryData.Segment,
                        ThumbnailImage = !string.IsNullOrEmpty(entryData.ThumbnailBase64) ? Base64ToBitmap(entryData.ThumbnailBase64) : null
                    };
                    
                    // Count missing thumbnails
                    if (entry.ThumbnailImage == null)
                    {
                        missingThumbnails++;
                    }
                    
                    timeStudyData.Add(entry);
                }
                
                // Regenerate missing thumbnails if video is available
                if (missingThumbnails > 0 && HasVideo)
                {
                    RegenerateMissingThumbnails(missingThumbnails);
                }
                
                // Restore segment names
                if (projectData.SegmentNames != null)
                {
                    segmentNames = projectData.SegmentNames;
                    UpdateSegmentLabels();
                }
                
                // Restore element library
                if (projectData.ElementLibrary != null)
                {
                    elementLibrary = projectData.ElementLibrary;
                    PopulateElementLibrary();
                }
                
                // Restore zones
                workZones.Clear();
                if (projectData.Zones != null && projectData.Zones.Count > 0)
                {
                    foreach (var zoneData in projectData.Zones)
                    {
                        var zone = new WorkZone
                        {
                            Name = zoneData.Name,
                            X = zoneData.X,
                            Y = zoneData.Y,
                            Width = zoneData.Width,
                            Height = zoneData.Height,
                            Color = zoneData.Color,
                            MotionThreshold = zoneData.MotionThreshold,
                            MinMotionPixels = zoneData.MinMotionPixels,
                            Events = new List<ZoneEvent>()
                        };
                        workZones.Add(zone);
                    }
                    
                    // Restore zone events
                    if (projectData.ZoneEvents != null)
                    {
                        foreach (var eventData in projectData.ZoneEvents)
                        {
                            var zone = workZones.FirstOrDefault(z => z.Name == eventData.ZoneName);
                            if (zone != null)
                            {
                                zone.Events.Add(new ZoneEvent
                                {
                                    ZoneName = eventData.ZoneName,
                                    VideoTimeInSeconds = eventData.VideoTimeInSeconds,
                                    EventType = eventData.EventType,
                                    Timestamp = eventData.Timestamp
                                });
                                
                                if (eventData.EventType == "Entry")
                                {
                                    zone.EntryCount++;
                                }
                            }
                        }
                    }
                    
                    DrawAllZones();
                }
                
                // Update UI
                UpdateTimeline();
                UpdateVideoMarkers();
                UpdateSegmentSummary();
                
                StatusText.Text = $"Project loaded: {System.IO.Path.GetFileName(openFileDialog.FileName)}";
                MessageBox.Show("Project loaded successfully!", "Load Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading project: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private string BitmapToBase64(BitmapSource bitmap)
    {
        using (var ms = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
    }

    private BitmapSource? Base64ToBitmap(string base64)
    {
        try
        {
            byte[] imageBytes = Convert.FromBase64String(base64);
            using (var ms = new MemoryStream(imageBytes))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
    }

    private void RegenerateMissingThumbnails(int missingCount)
    {
        try
        {
            StatusText.Text = $"Regenerating {missingCount} missing thumbnails...";
            
            int regenerated = 0;
            int failed = 0;
            
            // Store original position to restore later
            double originalPosition = CurrentPositionSeconds;
            
            foreach (var entry in timeStudyData)
            {
                if (entry.ThumbnailImage == null)
                {
                    // Parse timestamp to get absolute time in seconds
                    if (TimeSpan.TryParse(entry.Timestamp, out var timestamp))
                    {
                        double absoluteSeconds = timestamp.TotalSeconds;
                        var thumbnail = CaptureVideoFrame(absoluteSeconds);
                        
                        if (thumbnail != null)
                        {
                            entry.ThumbnailImage = thumbnail;
                            regenerated++;
                        StatusText.Text = $"Regenerating thumbnails: {regenerated}/{missingCount}";
                        
                            // Force UI update
                            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    else
                    {
                        failed++; // Failed to parse timestamp
                    }
                }
            }
            
            // Restore original video position
            CurrentPositionSeconds = originalPosition;
            
            if (regenerated > 0)
            {
                StatusText.Text = $"Regenerated {regenerated} thumbnail(s). {(failed > 0 ? $"Failed: {failed}" : "")}";
                MessageBox.Show($"Successfully regenerated {regenerated} thumbnail(s).{(failed > 0 ? $"\n{failed} thumbnail(s) could not be generated." : "")}", 
                    "Thumbnails Regenerated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error regenerating thumbnails: {ex.Message}", "Thumbnail Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #region Zone Tracking

    private void DefineZone_Click(object sender, RoutedEventArgs e)
    {
        if (!HasVideo)
        {
            MessageBox.Show("Please load a video first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Pause video
        VideoPlayer.Pause();
        
        // Enable zone definition mode
        isDefiningZone = true;
        VideoContainer.Cursor = Cursors.Cross;
        VideoScrollViewer.PanningMode = PanningMode.None; // Disable panning
        StatusText.Text = "Click and drag on the video to define a work zone. Press ESC to cancel.";
    }

    private void ToggleZones_Click(object sender, RoutedEventArgs e)
    {
        zonesVisible = !zonesVisible;
        if (zonesVisible)
        {
            DrawAllZones();
            StatusText.Text = "Zones visible";
        }
        else
        {
            ZoneCanvas.Children.Clear();
            StatusText.Text = "Zones hidden";
        }
    }

    private void ClearZones_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0)
        {
            MessageBox.Show("No zones to clear.", "Clear Zones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Clear all {workZones.Count} zone(s)?", "Clear Zones", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            workZones.Clear();
            ZoneCanvas.Children.Clear();
            StatusText.Text = "All zones cleared";
        }
    }

    private void ZoneCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Check if clicking on a zone rectangle (not defining zones)
        if (!isDefiningZone && e.OriginalSource is Rectangle rect && rect.Tag is WorkZone)
        {
            // Let the rectangle's handler deal with it - don't interfere
            return;
        }
        
        // Also don't interfere if clicking on other zone UI elements
        if (!isDefiningZone && (e.OriginalSource is Border || e.OriginalSource is TextBlock))
        {
            return;
        }
        
        if (!isDefiningZone) return;

        // Get position relative to ZoneCanvas directly - this is where we're drawing!
        zoneStartPoint = e.GetPosition(ZoneCanvas);
        
        System.Diagnostics.Debug.WriteLine($"=== CLICK ===");
        System.Diagnostics.Debug.WriteLine($"ZoneCanvas position: {zoneStartPoint.X:F1}, {zoneStartPoint.Y:F1}");
        System.Diagnostics.Debug.WriteLine($"ZoneCanvas ActualSize: {ZoneCanvas.ActualWidth:F1} x {ZoneCanvas.ActualHeight:F1}");
        
        // Create temporary rectangle
        currentZoneRect = new Rectangle
        {
            Stroke = Brushes.Yellow,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0)),
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        
        Canvas.SetLeft(currentZoneRect, zoneStartPoint.X);
        Canvas.SetTop(currentZoneRect, zoneStartPoint.Y);
        ZoneCanvas.Children.Add(currentZoneRect);
        
        // Add a small dot at click position for visual feedback
        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Red
        };
        Canvas.SetLeft(dot, zoneStartPoint.X - 5);
        Canvas.SetTop(dot, zoneStartPoint.Y - 5);
        ZoneCanvas.Children.Add(dot);
        
        e.Handled = true;
    }

    private void ZoneCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDefiningZone || currentZoneRect == null) return;

        // Get position relative to ZoneCanvas - simple!
        Point currentPoint = e.GetPosition(ZoneCanvas);
        
        double x = Math.Min(zoneStartPoint.X, currentPoint.X);
        double y = Math.Min(zoneStartPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - zoneStartPoint.X);
        double height = Math.Abs(currentPoint.Y - zoneStartPoint.Y);
        
        Canvas.SetLeft(currentZoneRect, x);
        Canvas.SetTop(currentZoneRect, y);
        currentZoneRect.Width = width;
        currentZoneRect.Height = height;
        
        e.Handled = true;
    }
        
    private void ZoneCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDefiningZone || currentZoneRect == null) return;

        // Get position relative to ZoneCanvas - simple!
        Point endPoint = e.GetPosition(ZoneCanvas);

        double width = Math.Abs(endPoint.X - zoneStartPoint.X);
        double height = Math.Abs(endPoint.Y - zoneStartPoint.Y);
        
        double x = Math.Min(zoneStartPoint.X, endPoint.X);
        double y = Math.Min(zoneStartPoint.Y, endPoint.Y);
        
        // Minimum zone size
        if (width < 20 || height < 20)
        {
            ZoneCanvas.Children.Remove(currentZoneRect);
            currentZoneRect = null;
            isDefiningZone = false;
            VideoContainer.Cursor = Cursors.Arrow;
            VideoScrollViewer.PanningMode = PanningMode.Both;
            StatusText.Text = "Zone too small. Try again.";
            return;
        }

        // Prompt for zone name
        string zoneName = PromptForZoneName();
        
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            ZoneCanvas.Children.Remove(currentZoneRect);
            currentZoneRect = null;
            isDefiningZone = false;
            VideoContainer.Cursor = Cursors.Arrow;
            VideoScrollViewer.PanningMode = PanningMode.Both;
            StatusText.Text = "Zone definition cancelled";
            return;
        }

        // Create work zone
        var zone = new WorkZone
        {
            Name = zoneName,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Color = GetNextZoneColor()
        };
        
        workZones.Add(zone);
        
        // Clean up temporary rectangle
        ZoneCanvas.Children.Remove(currentZoneRect);
        currentZoneRect = null;
        isDefiningZone = false;
        VideoContainer.Cursor = Cursors.Arrow;
        VideoScrollViewer.PanningMode = PanningMode.Both;
        
        // Redraw all zones
        DrawAllZones();
        
        StatusText.Text = $"Zone '{zoneName}' created ({workZones.Count} total)";
        
        e.Handled = true;
    }

    private string PromptForZoneName()
    {
        var dialog = new Window
        {
            Title = "Zone Name",
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };
        
        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
        var label = new TextBlock 
        { 
            Text = "Enter zone name:", 
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        
        var textBox = new TextBox 
        { 
            Text = $"Zone {workZones.Count + 1}",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(5)
        };
        textBox.SelectAll();
        textBox.Focus();
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);
        
        var buttonPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right 
        };
        Grid.SetRow(buttonPanel, 3);
        
        var okButton = new Button 
        { 
            Content = "OK", 
            Width = 75, 
            Height = 25,
            Margin = new Thickness(0, 0, 5, 0), 
            IsDefault = true 
        };
        okButton.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
        buttonPanel.Children.Add(okButton);
        
        var cancelButton = new Button 
        { 
            Content = "Cancel", 
            Width = 75,
            Height = 25,
            IsCancel = true 
        };
        cancelButton.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };
        buttonPanel.Children.Add(cancelButton);
        
        grid.Children.Add(buttonPanel);
        dialog.Content = grid;
        
        textBox.KeyDown += (s, args) =>
        {
            if (args.Key == Key.Enter)
            {
                dialog.DialogResult = true;
                dialog.Close();
            }
        };
        
        return dialog.ShowDialog() == true ? textBox.Text.Trim() : "";
    }

    private string GetNextZoneColor()
    {
        string[] colors = { "#4CAF50", "#2196F3", "#FF9800", "#9C27B0", "#F44336", "#00BCD4", "#FFEB3B", "#E91E63" };
        return colors[workZones.Count % colors.Length];
    }

    private void ZoneRect_MouseDown(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"ZoneRect_MouseDown called. isDefiningZone={isDefiningZone}");
        
        if (isDefiningZone) return; // Don't handle clicks while defining zones
        
        if (sender is Rectangle rect && rect.Tag is WorkZone zone)
        {
            System.Diagnostics.Debug.WriteLine($"Clicking zone: {zone.Name}, PeopleCount={zone.PeopleCount}");
            
            // Track last selected zone for keyboard shortcuts
            lastSelectedZone = zone;
            
            // Show zone info in status
            StatusText.Text = $"Selected: {zone.Name} - {zone.PeopleCount} people, ∑{zone.EntryCount} total entries";
            
            e.Handled = true;
        }
    }

    private void DrawAllZones()
    {
        ZoneCanvas.Children.Clear();
        
        if (!zonesVisible) return;

        foreach (var zone in workZones)
        {
            DrawZone(zone);
        }
    }

    private void DrawZone(WorkZone zone)
    {
        var color = (Color)ColorConverter.ConvertFromString(zone.Color);
        
        // Change visual appearance based on people count
        var strokeThickness = zone.PeopleCount > 0 ? 5 : 3;
        var fillOpacity = zone.PeopleCount > 0 ? 80 : 30; // More visible when occupied
        
        // Draw rectangle
        var rect = new Rectangle
        {
            Width = zone.Width,
            Height = zone.Height,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = strokeThickness,
            Fill = new SolidColorBrush(Color.FromArgb((byte)fillOpacity, color.R, color.G, color.B)),
            Tag = zone, // Store reference to zone
            Cursor = Cursors.Hand,
            IsHitTestVisible = true // Ensure it can receive mouse events
        };
        
        // Add click handler to rectangle
        rect.MouseDown += ZoneRect_MouseDown;
        
        // Add context menu for zone configuration
        var contextMenu = new ContextMenu();
        
        var configItem = new MenuItem { Header = "⚙️ Configure Settings" };
        configItem.Click += (s, e) => ShowZoneConfigDialog(zone);
        contextMenu.Items.Add(configItem);
        
        var renameItem = new MenuItem { Header = "✏️ Rename Zone" };
        renameItem.Click += (s, e) => RenameZone(zone);
        contextMenu.Items.Add(renameItem);
        
        contextMenu.Items.Add(new Separator());
        
        var deleteItem = new MenuItem { Header = "🗑️ Delete Zone" };
        deleteItem.Click += (s, e) => DeleteZone(zone);
        contextMenu.Items.Add(deleteItem);
        
        rect.ContextMenu = contextMenu;
        
        Canvas.SetLeft(rect, zone.X);
        Canvas.SetTop(rect, zone.Y);
        Panel.SetZIndex(rect, 100); // Bring zones to front
        ZoneCanvas.Children.Add(rect);
        
        // Draw label background
        var labelBg = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4, 8, 4)
        };
        
        // Update label to show people count
        var labelText = new TextBlock
        {
            Text = zone.PeopleCount > 0 ? $"{zone.Name} [{zone.PeopleCount}] (Σ{zone.EntryCount})" : $"{zone.Name} (Σ{zone.EntryCount})",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14
        };
        
        labelBg.Child = labelText;
        Canvas.SetLeft(labelBg, zone.X);
        Canvas.SetTop(labelBg, zone.Y - 30);
        ZoneCanvas.Children.Add(labelBg);
        
        // Show person number overlays if enabled and people are detected
        if (showPersonNumbers && zone.People.Count > 0)
        {
            foreach (var person in zone.People)
            {
                // Calculate screen position from zone-relative position
                double screenX = zone.X + person.X;
                double screenY = zone.Y + person.Y;

                // Create person marker
                var personMarker = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 215, 0)), // Yellow
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(20),
                    Width = 40,
                    Height = 40,
                    Child = new TextBlock
                    {
                        Text = person.Id.ToString(),
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.Bold,
                        FontSize = 18,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };

                Canvas.SetLeft(personMarker, screenX - 20); // Center the marker
                Canvas.SetTop(personMarker, screenY - 20);
                Panel.SetZIndex(personMarker, 300); // Above zones
                ZoneCanvas.Children.Add(personMarker);
            }
        }
    }

    #endregion

    #region Keyboard Shortcuts

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't process shortcuts if typing in a text box
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox)
            return;

        switch (e.Key)
        {
            case Key.Space:
                // Play/Pause - check if timer is running as proxy for playing state
                if (VideoPlayer.Source != null)
                {
                    if (timer.IsEnabled)
                        Pause_Click(this, new RoutedEventArgs());
                    else
                        Play_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            case Key.M:
                // Mark timestamp
                if (VideoPlayer.Source != null)
                {
                    AddTimestamp_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            case Key.Left:
                // Rewind 5 seconds
                if (VideoPlayer.Source != null)
                {
                    var newPos = CurrentPositionSeconds - 5;
                    if (newPos < 0) newPos = 0;
                    CurrentPositionSeconds = newPos;
                }
                e.Handled = true;
                break;

            case Key.Right:
                // Forward 5 seconds
                if (VideoPlayer.Source != null)
                {
                    var newPos = CurrentPositionSeconds + 5;
                    if (newPos > cumulativeVideoTime) newPos = cumulativeVideoTime;
                    CurrentPositionSeconds = newPos;
                }
                e.Handled = true;
                break;





            case Key.Delete:
                // Delete selected timestamp
                if (TimeStudyGrid.SelectedItem is TimeStudyEntry selectedEntry)
                {
                    timeStudyData.Remove(selectedEntry);
                    UpdateTimeline();
                    UpdateVideoMarkers();
                    StatusText.Text = "Entry deleted";
                }
                e.Handled = true;
                break;

        }
    }

    private void ShowKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new Window
        {
            Title = "Keyboard Shortcuts",
            Width = 600,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(20)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "⌨️ Keyboard Shortcuts",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var shortcuts = new[]
        {
            ("PLAYBACK", ""),
            ("Spacebar", "Play / Pause video"),
            ("", ""),
            ("FRAME CONTROL", ""),
            (",   (comma)", "Previous frame (1/30 second)"),
            (".   (period)", "Next frame (1/30 second)"),
            ("Left Arrow", "Rewind 1 second"),
            ("Right Arrow", "Forward 1 second"),
            ("A", "Rewind 3 seconds (Shift+A: 10s)"),
            ("D", "Forward 3 seconds (Shift+D: 10s)"),
            ("", ""),
            ("SPEED CONTROL", ""),
            ("Up Arrow / +", "Increase playback speed"),
            ("Down Arrow / -", "Decrease playback speed"),
            ("", ""),
            ("MARKING & ELEMENTS", ""),
            ("M  or  Ctrl+M", "Mark timestamp at current time"),
            ("S", "Start element"),
            ("E", "End element"),
            ("1, 2, 3, 4, 5", "Add timestamp with segment number"),
            ("Shift+1 to 5", "Add 'Away/Waiting' marker"),
            ("", ""),
            ("EDITING", ""),
            ("Delete", "Remove selected timestamp")
        };

        foreach (var (key, description) in shortcuts)
        {
            if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(key))
            {
                stack.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10), Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)) });
                continue;
            }

            if (string.IsNullOrEmpty(description))
            {
                // Section header
                stack.Children.Add(new TextBlock
                {
                    Text = key,
                    Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Margin = new Thickness(0, 10, 0, 5)
                });
                continue;
            }
            
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var keyText = new TextBlock
            {
                Text = key,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            Grid.SetColumn(keyText, 0);
            grid.Children.Add(keyText);

            var descText = new TextBlock
            {
                Text = description,
                Foreground = Brushes.LightGray,
                FontSize = 12
            };
            Grid.SetColumn(descText, 1);
            grid.Children.Add(descText);

            stack.Children.Add(grid);
        }

        var closeButton = new Button
        {
            Content = "Close",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (s, args) => helpWindow.Close();
        stack.Children.Add(closeButton);

        scrollViewer.Content = stack;
        helpWindow.Content = scrollViewer;
        helpWindow.ShowDialog();
    }

    private void MotionDetectionSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new Window
        {
            Title = "Motion Detection Settings",
            Width = 500,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "⚙️ Global Motion Detection Settings",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 20)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var stack = new StackPanel();
        Grid.SetRow(stack, 1);

        // Detection Frequency
        stack.Children.Add(new TextBlock
        {
            Text = "Detection Frequency:",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 10, 0, 5),
            FontSize = 12
        });

        var frequencySlider = new System.Windows.Controls.Slider
        {
            Minimum = 100,
            Maximum = 2000,
            Value = motionDetectionIntervalMs,
            TickFrequency = 100,
            IsSnapToTickEnabled = true
        };
        var frequencyLabel = new TextBlock
        {
            Text = $"Check every {motionDetectionIntervalMs}ms ({1000.0 / motionDetectionIntervalMs:F1} times/sec)",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        frequencySlider.ValueChanged += (s, ev) =>
        {
            frequencyLabel.Text = $"Check every {ev.NewValue:F0}ms ({1000.0 / ev.NewValue:F1} times/sec)";
        };
        stack.Children.Add(frequencySlider);
        stack.Children.Add(frequencyLabel);

        // Frame Skip
        stack.Children.Add(new TextBlock
        {
            Text = "Frame Skip (Performance):",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 20, 0, 5),
            FontSize = 12
        });

        var frameSkipSlider = new System.Windows.Controls.Slider
        {
            Minimum = 1,
            Maximum = 10,
            Value = frameSkipCount,
            TickFrequency = 1,
            IsSnapToTickEnabled = true
        };
        var frameSkipLabel = new TextBlock
        {
            Text = $"Process every {frameSkipCount} frame(s)",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        frameSkipSlider.ValueChanged += (s, ev) =>
        {
            frameSkipLabel.Text = $"Process every {ev.NewValue:F0} frame(s)";
        };
        stack.Children.Add(frameSkipSlider);
        stack.Children.Add(frameSkipLabel);

        stack.Children.Add(new TextBlock
        {
            Text = "Note: Higher values = better performance, lower accuracy",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontSize = 10,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 5, 0, 0)
        });

        // Presets
        stack.Children.Add(new TextBlock
        {
            Text = "Quick Presets:",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 20, 0, 10),
            FontSize = 12,
            FontWeight = FontWeights.Bold
        });

        var presetStack = new StackPanel { Orientation = Orientation.Horizontal };
        
        var highSensBtn = new Button { Content = "High Sensitivity", Width = 120, Height = 25, Margin = new Thickness(0, 0, 5, 0) };
        highSensBtn.Click += (s, ev) => { frequencySlider.Value = 200; frameSkipSlider.Value = 1; };
        presetStack.Children.Add(highSensBtn);

        var balancedBtn = new Button { Content = "Balanced", Width = 100, Height = 25, Margin = new Thickness(0, 0, 5, 0) };
        balancedBtn.Click += (s, ev) => { frequencySlider.Value = 500; frameSkipSlider.Value = 2; };
        presetStack.Children.Add(balancedBtn);

        var perfBtn = new Button { Content = "Performance", Width = 100, Height = 25 };
        perfBtn.Click += (s, ev) => { frequencySlider.Value = 1000; frameSkipSlider.Value = 5; };
        presetStack.Children.Add(perfBtn);

        stack.Children.Add(presetStack);

        grid.Children.Add(stack);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);

        var saveButton = new Button
        {
            Content = "Save",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
            Foreground = Brushes.White
        };
        saveButton.Click += (s, args) =>
        {
            motionDetectionIntervalMs = (int)frequencySlider.Value;
            frameSkipCount = (int)frameSkipSlider.Value;
            
            // Restart timer if active
            if (isMotionTrackingActive && motionDetectionTimer != null)
            {
                motionDetectionTimer.Stop();
                motionDetectionTimer.Interval = TimeSpan.FromMilliseconds(motionDetectionIntervalMs);
                motionDetectionTimer.Start();
            }
            
            settingsWindow.Close();
            StatusText.Text = $"Motion detection settings updated: {motionDetectionIntervalMs}ms, skip {frameSkipCount}";
        };
        buttonPanel.Children.Add(saveButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 30
        };
        cancelButton.Click += (s, args) => settingsWindow.Close();
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(buttonPanel);
        settingsWindow.Content = grid;
        settingsWindow.ShowDialog();
    }

    private void IncreasePeopleCount(WorkZone zone)
    {
        if (zone.PeopleCount < 99) // Max 99 people
        {
            zone.PeopleCount++;
            zone.EntryCount++;
            
            var zoneEvent = new ZoneEvent
            {
                Timestamp = DateTime.Now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Entry ({zone.PeopleCount})",
                ZoneName = zone.Name
            };
            zone.Events.Add(zoneEvent);
            
            DrawAllZones();
            if (ZoneLogPanel.Visibility == Visibility.Visible)
                UpdateZoneEventLog();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                UpdateZoneTimeline();
        }
    }

    private void DecreasePeopleCount(WorkZone zone)
    {
        if (zone.PeopleCount > 0)
        {
            zone.PeopleCount--;
            
            var zoneEvent = new ZoneEvent
            {
                Timestamp = DateTime.Now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Exit ({zone.PeopleCount})",
                ZoneName = zone.Name
            };
            zone.Events.Add(zoneEvent);
            
            DrawAllZones();
            if (ZoneLogPanel.Visibility == Visibility.Visible)
                UpdateZoneEventLog();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                UpdateZoneTimeline();
        }
    }

    private void SetPeopleCount(WorkZone zone, int count)
    {
        if (count >= 0 && count <= 99)
        {
            int oldCount = zone.PeopleCount;
            zone.PeopleCount = count;
            
            // Track entry count increase
            if (count > oldCount)
                zone.EntryCount += (count - oldCount);
            
            var zoneEvent = new ZoneEvent
            {
                Timestamp = DateTime.Now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Set to {count}",
                ZoneName = zone.Name
            };
            zone.Events.Add(zoneEvent);
            
            DrawAllZones();
            if (ZoneLogPanel.Visibility == Visibility.Visible)
                UpdateZoneEventLog();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                UpdateZoneTimeline();
                
            StatusText.Text = $"⌨️ Set count: {zone.Name} [{count}]";
        }
    }

    private void TogglePersonNumbers_Click(object sender, RoutedEventArgs e)
    {
        showPersonNumbers = !showPersonNumbers;
        DrawAllZones();
        StatusText.Text = showPersonNumbers ? "Person numbers: ON" : "Person numbers: OFF";
    }

    private void ToggleMotionTrail_Click(object sender, RoutedEventArgs e)
    {
        motionTrailEnabled = !motionTrailEnabled;
        
        if (motionTrailEnabled)
        {
            // Start motion trail capture
            if (motionTrailTimer == null)
            {
                motionTrailTimer = new System.Windows.Threading.DispatcherTimer();
                motionTrailTimer.Interval = TimeSpan.FromMilliseconds(TRAIL_CAPTURE_INTERVAL_MS);
                motionTrailTimer.Tick += MotionTrailTimer_Tick;
            }
            motionTrailTimer.Start();
            StatusText.Text = "Motion trail overlay: ON (capturing every 1s for 5 seconds)";
        }
        else
        {
            // Stop motion trail capture
            motionTrailTimer?.Stop();
            trailFrames.Clear();
            MotionTrailCanvas.Children.Clear();
            StatusText.Text = "Motion trail overlay: OFF";
        }
    }

    private void MotionTrailTimer_Tick(object? sender, EventArgs e)
    {
        if (!HasVideo || VideoPlayer.Source == null)
            return;
            
        try
        {
            // Capture current frame
            var frame = CaptureVideoFrameBitmap(CurrentPositionSeconds);
            if (frame != null)
            {
                // Add to trail queue
                trailFrames.Enqueue(frame);
                
                // Remove oldest if we have too many
                while (trailFrames.Count > TRAIL_FRAME_COUNT)
                {
                    trailFrames.Dequeue();
                }
                
                // Redraw motion trail
                DrawMotionTrail();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Motion trail capture error: {ex.Message}");
        }
    }

    private void DrawMotionTrail()
    {
        MotionTrailCanvas.Children.Clear();
        
        if (trailFrames.Count == 0)
            return;
            
        int frameIndex = 0;
        double opacityStep = 1.0 / (trailFrames.Count + 1);
        
        foreach (var frame in trailFrames)
        {
            var image = new System.Windows.Controls.Image
            {
                Source = frame,
                Width = MotionTrailCanvas.Width,
                Height = MotionTrailCanvas.Height,
                Stretch = Stretch.Uniform,
                Opacity = opacityStep * (frameIndex + 1) // Older frames are more transparent
            };
            
            MotionTrailCanvas.Children.Add(image);
            frameIndex++;
        }
    }

    private BitmapSource? CaptureVideoFrameBitmap(double timeInSeconds)
    {
        try
        {
            var renderBitmap = new RenderTargetBitmap(
                (int)VideoPlayer.ActualWidth,
                (int)VideoPlayer.ActualHeight,
                96, 96,
                PixelFormats.Pbgra32);
            
            renderBitmap.Render(VideoPlayer);
            renderBitmap.Freeze(); // Make it thread-safe
            return renderBitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Frame capture error: {ex.Message}");
            return null;
        }
    }

    private void InitializeYoloModel()
    {
        try
        {
            // Get directories to search
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? projectDir = null;
            try
            {
                projectDir = Directory.GetParent(Directory.GetParent(Directory.GetParent(baseDir)!.FullName)!.FullName)!.FullName;
            }
            catch
            {
                projectDir = baseDir;
            }
            
            System.Diagnostics.Debug.WriteLine($"Base directory: {baseDir}");
            System.Diagnostics.Debug.WriteLine($"Project directory: {projectDir}");
            
            // Try to load YOLO11 or YOLOv8 pose model first (preferred), then regular detection model
            string[] posePaths = new[]
            {
                System.IO.Path.Combine(projectDir, "yolo11n-pose.onnx"),
                System.IO.Path.Combine(baseDir, "yolo11n-pose.onnx"),
                "yolo11n-pose.onnx",
                System.IO.Path.Combine(projectDir, "yolov8n-pose.onnx"),
                System.IO.Path.Combine(baseDir, "yolov8n-pose.onnx"),
                "yolov8n-pose.onnx"
            };
            
            string[] detectionPaths = new[]
            {
                System.IO.Path.Combine(projectDir, "yolo11n.onnx"),
                System.IO.Path.Combine(baseDir, "yolo11n.onnx"),
                "yolo11n.onnx",
                System.IO.Path.Combine(projectDir, "yolov8n.onnx"),
                System.IO.Path.Combine(baseDir, "yolov8n.onnx"),
                "yolov8n.onnx"
            };

            System.Diagnostics.Debug.WriteLine("Searching for pose model:");
            foreach (var path in posePaths)
            {
                System.Diagnostics.Debug.WriteLine($"  {path} - {(File.Exists(path) ? "EXISTS" : "not found")}");
            }

            string? poseModelPath = posePaths.FirstOrDefault(File.Exists);
            string? detectionModelPath = detectionPaths.FirstOrDefault(File.Exists);

            if (poseModelPath != null)
            {
                try 
                {
                    System.Diagnostics.Debug.WriteLine($"Attempting to load pose model: {poseModelPath}");
                    var sessionOptions = new SessionOptions();
                    
                    // Try GPU acceleration first (DirectML for Windows), fallback to CPU
                    try
                    {
                        sessionOptions.AppendExecutionProvider_DML(0); // Use default GPU (device 0)
                        System.Diagnostics.Debug.WriteLine("Using GPU acceleration (DirectML)");
                    }
                    catch
                    {
                        sessionOptions.AppendExecutionProvider_CPU(0);
                        System.Diagnostics.Debug.WriteLine("GPU acceleration not available, using CPU");
                    }
                    
                    yoloSession = new InferenceSession(poseModelPath, sessionOptions);
                    usePoseModel = true;
                    var modelName = System.IO.Path.GetFileName(poseModelPath);
                    StatusText.Text = $"✓ AI person detection with pose estimation enabled ({modelName})";
                    System.Diagnostics.Debug.WriteLine($"SUCCESS: Loaded pose model!");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FAILED to load pose model: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine("Falling back to detection-only model");
                    poseModelPath = null; // Force fallback to detection model
                }
            }
            
            if (poseModelPath == null && detectionModelPath != null)
            {
                var sessionOptions = new SessionOptions();
                
                // Try GPU acceleration first
                try
                {
                    sessionOptions.AppendExecutionProvider_DML(0);
                    System.Diagnostics.Debug.WriteLine("Using GPU acceleration (DirectML)");
                }
                catch
                {
                    sessionOptions.AppendExecutionProvider_CPU(0);
                    System.Diagnostics.Debug.WriteLine("GPU acceleration not available, using CPU");
                }
                
                yoloSession = new InferenceSession(detectionModelPath, sessionOptions);
                usePoseModel = false;
                var modelName = System.IO.Path.GetFileName(detectionModelPath);
                StatusText.Text = $"⚠ Detection only ({modelName}) - pose model incompatible with ONNX Runtime";
                System.Diagnostics.Debug.WriteLine($"Loaded detection model: {detectionModelPath}");
            }
            else if (poseModelPath == null && detectionModelPath == null)
            {
                StatusText.Text = "⚠ AI model not found - using basic motion detection. Download yolo11n.onnx or yolov8n.onnx.";
                System.Diagnostics.Debug.WriteLine("No YOLO model found");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"⚠ Could not load AI model: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"YOLO initialization error: {ex}");
        }
    }

    private List<PersonDetection> DetectPersonsWithYolo(byte[] framePixels, int width, int height)
    {
        var detections = new List<PersonDetection>();

        if (yoloSession == null)
            return detections;

        try
        {
            // Prepare input tensor
            var inputTensor = PreprocessFrame(framePixels, width, height);
            
            // Run inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            using var results = yoloSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Parse YOLO output [1, 84, 8400] format
            // 84 = 4 box coords + 80 class scores
            detections = ParseYoloOutput(output, width, height);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"YOLO detection error: {ex.Message}");
        }

        return detections;
    }

    private DenseTensor<float> PreprocessFrame(byte[] pixels, int width, int height)
    {
        // Create tensor [1, 3, 640, 640] for YOLOv8
        var tensor = new DenseTensor<float>(new[] { 1, 3, YOLO_INPUT_SIZE, YOLO_INPUT_SIZE });

        // Calculate letterbox scaling (preserve aspect ratio)
        float scale = Math.Min((float)YOLO_INPUT_SIZE / width, (float)YOLO_INPUT_SIZE / height);
        int scaledWidth = (int)(width * scale);
        int scaledHeight = (int)(height * scale);
        int offsetX = (YOLO_INPUT_SIZE - scaledWidth) / 2;
        int offsetY = (YOLO_INPUT_SIZE - scaledHeight) / 2;

        int stride = width * 4; // BGRA

        // Fill with gray padding (114, 114, 114) - YOLO standard
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < YOLO_INPUT_SIZE; y++)
            {
                for (int x = 0; x < YOLO_INPUT_SIZE; x++)
                {
                    tensor[0, c, y, x] = 114f / 255f;
                }
            }
        }

        // Copy resized image to center with bilinear interpolation
        for (int y = 0; y < scaledHeight; y++)
        {
            for (int x = 0; x < scaledWidth; x++)
            {
                float srcX = x / scale;
                float srcY = y / scale;
                int x0 = (int)srcX;
                int y0 = (int)srcY;
                int x1 = Math.Min(x0 + 1, width - 1);
                int y1 = Math.Min(y0 + 1, height - 1);
                float dx = srcX - x0;
                float dy = srcY - y0;

                // Bilinear interpolation
                for (int c = 0; c < 3; c++)
                {
                    int channelOffset = (c == 0) ? 2 : (c == 1) ? 1 : 0; // BGR to RGB
                    
                    float p00 = pixels[y0 * stride + x0 * 4 + channelOffset];
                    float p10 = pixels[y0 * stride + x1 * 4 + channelOffset];
                    float p01 = pixels[y1 * stride + x0 * 4 + channelOffset];
                    float p11 = pixels[y1 * stride + x1 * 4 + channelOffset];
                    
                    float value = p00 * (1 - dx) * (1 - dy) + 
                                  p10 * dx * (1 - dy) + 
                                  p01 * (1 - dx) * dy + 
                                  p11 * dx * dy;
                    
                    tensor[0, c, y + offsetY, x + offsetX] = value / 255f;
                }
            }
        }

        return tensor;
    }

    private List<PersonDetection> ParseYoloOutput(Tensor<float> output, int originalWidth, int originalHeight)
    {
        var rawDetections = new List<PersonDetection>();
        
        // YOLOv8 output shape: [1, 84, 8400] for detection or [1, 56, 8400] for pose
        // YOLO11 might use transposed format: [1, 8400, 56]
        // Detection: 84 = 4 box coords + 80 class scores
        // Pose: 56 = 4 box coords + 1 person score + 51 keypoint coords (17 keypoints * 3)
        int dim0 = output.Dimensions[0]; // Should be 1
        int dim1 = output.Dimensions[1]; // Could be 56 or 8400
        int dim2 = output.Dimensions[2]; // Could be 8400 or 56
        
        System.Diagnostics.Debug.WriteLine($"YOLO output shape: [{dim0}, {dim1}, {dim2}]");
        
        // Detect if output is transposed
        bool isTransposed = dim1 > dim2; // [1, 8400, 56] vs [1, 56, 8400]
        int numDetections = isTransposed ? dim1 : dim2;
        int outputDim = isTransposed ? dim2 : dim1;
        
        bool isPoseModel = outputDim == 56;
        
        System.Diagnostics.Debug.WriteLine($"Transposed: {isTransposed}, numDetections: {numDetections}, outputDim: {outputDim}, isPoseModel: {isPoseModel}");
        
        // Sample first detection to find where confidence is stored
        if (!isTransposed)
        {
            System.Diagnostics.Debug.WriteLine("Sample values from first detection:");
            System.Diagnostics.Debug.WriteLine($"  Indices [0,0-4,0]: {output[0,0,0]:F3}, {output[0,1,0]:F3}, {output[0,2,0]:F3}, {output[0,3,0]:F3}, {output[0,4,0]:F3}");
            
            // Check if confidence is computed from keypoints (indices 5-55 are keypoint data)
            float maxVal = 0;
            int maxIdx = -1;
            for (int idx = 0; idx < outputDim; idx++)
            {
                float val = output[0, idx, 0];
                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = idx;
                }
            }
            System.Diagnostics.Debug.WriteLine($"  Max value: {maxVal:F3} at index {maxIdx}");
            System.Diagnostics.Debug.WriteLine($"  Indices [0,5-9,0] (first keypoint): {output[0,5,0]:F3}, {output[0,6,0]:F3}, {output[0,7,0]:F3}, {output[0,8,0]:F3}, {output[0,9,0]:F3}");
            
            // Check keypoint confidence indices (should be at 7, 10, 13, 16, etc.)
            System.Diagnostics.Debug.WriteLine($"  Keypoint confidences [0,7/10/13/16,0]: {output[0,7,0]:F3}, {output[0,10,0]:F3}, {output[0,13,0]:F3}, {output[0,16,0]:F3}");
        }
        
        // Calculate letterbox scaling (same as preprocessing)
        float scale = Math.Min((float)YOLO_INPUT_SIZE / originalWidth, (float)YOLO_INPUT_SIZE / originalHeight);
        int scaledWidth = (int)(originalWidth * scale);
        int scaledHeight = (int)(originalHeight * scale);
        int offsetX = (YOLO_INPUT_SIZE - scaledWidth) / 2;
        int offsetY = (YOLO_INPUT_SIZE - scaledHeight) / 2;

        int highConfidenceCount = 0;
        for (int i = 0; i < numDetections; i++)
        {
            // Get bounding box and person score based on format
            float personScore;
            float cx, cy, w, h;
            
            if (isTransposed)
            {
                // [1, 8400, 56] format
                cx = output[0, i, 0];
                cy = output[0, i, 1];
                w = output[0, i, 2];
                h = output[0, i, 3];
                
                // YOLO11 pose models don't have confidence at index 4
                // Compute from keypoint confidences (every 3rd value starting at index 7)
                float sumConf = 0;
                int countConf = 0;
                for (int k = 0; k < 17; k++)
                {
                    float kpConf = output[0, i, 5 + k * 3 + 2];
                    if (kpConf > 0)
                    {
                        sumConf += kpConf;
                        countConf++;
                    }
                }
                personScore = countConf > 0 ? sumConf / countConf : 0;
            }
            else
            {
                // [1, 56, 8400] format
                cx = output[0, 0, i];
                cy = output[0, 1, i];
                w = output[0, 2, i];
                h = output[0, 3, i];
                
                // YOLO11 pose models don't have confidence at index 4
                // Compute from keypoint confidences (every 3rd value starting at index 7)
                float sumConf = 0;
                int countConf = 0;
                for (int k = 0; k < 17; k++)
                {
                    float kpConf = output[0, 5 + k * 3 + 2, i];
                    if (kpConf > 0)
                    {
                        sumConf += kpConf;
                        countConf++;
                    }
                }
                personScore = countConf > 0 ? sumConf / countConf : 0;
            }
            
            if (personScore > 0.5f) highConfidenceCount++;
            
            if (personScore < CONFIDENCE_THRESHOLD)
                continue;

            // Get bounding box (xywh format in 640x640 space)
            // Already extracted above based on format

            // Remove letterbox padding
            cx = (cx - offsetX) / scale;
            cy = (cy - offsetY) / scale;
            w = w / scale;
            h = h / scale;

            // Skip detections outside image bounds
            if (cx < 0 || cy < 0 || cx >= originalWidth || cy >= originalHeight)
                continue;

            var detection = new PersonDetection
            {
                CenterX = cx,
                CenterY = cy,
                Width = w,
                Height = h,
                Confidence = personScore,
                X = cx - w / 2,
                Y = cy - h / 2
            };

            // Parse keypoints if using pose model
            if (isPoseModel && usePoseModel)
            {
                detection.Keypoints = ParseKeypoints(output, i, offsetX, offsetY, scale, isTransposed);
                
                // Additional validation: require minimum visible keypoints
                int visibleKeypoints = detection.Keypoints.Count(kp => kp.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD);
                if (visibleKeypoints < 5) // Require at least 5 visible keypoints to be a valid person (stricter)
                    continue;
                
                // Validate keypoint distribution - should have keypoints spread across body
                bool hasUpperBody = detection.Keypoints.Take(11).Any(kp => kp.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD);
                bool hasLowerBody = detection.Keypoints.Skip(11).Any(kp => kp.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD);
                if (!hasUpperBody || !hasLowerBody)
                    continue;
            }
            else
            {
                // For detection-only models, validate by size - humans should be reasonable sized
                float minWidth = originalWidth * 0.02f; // At least 2% of frame width
                float maxWidth = originalWidth * 0.8f;  // At most 80% of frame width
                float minHeight = originalHeight * 0.05f; // At least 5% of frame height
                float aspectRatio = h / w;
                
                // Skip if too small, too large, or wrong aspect ratio for a person
                if (w < minWidth || w > maxWidth || h < minHeight || aspectRatio < 1.2f || aspectRatio > 4.0f)
                    continue;
            }

            rawDetections.Add(detection);
        }

        System.Diagnostics.Debug.WriteLine($"Found {highConfidenceCount} detections > 50% confidence, {rawDetections.Count} passed all filters (threshold: {CONFIDENCE_THRESHOLD})");
        
        // Apply Non-Maximum Suppression to remove duplicate detections
        return ApplyNMS(rawDetections, 0.45f); // IOU threshold of 0.45
    }

    private List<Keypoint> ParseKeypoints(Tensor<float> output, int detectionIndex, int offsetX, int offsetY, float scale, bool isTransposed)
    {
        // COCO 17 keypoints in order
        string[] keypointNames = new[]
        {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };

        var keypoints = new List<Keypoint>();
        
        // Keypoints start at index 5, each keypoint has 3 values (x, y, confidence)
        for (int k = 0; k < 17; k++)
        {
            int baseIdx = 5 + (k * 3);
            float kpX, kpY, kpConf;
            
            if (isTransposed)
            {
                // [1, 8400, 56] format
                kpX = output[0, detectionIndex, baseIdx];
                kpY = output[0, detectionIndex, baseIdx + 1];
                kpConf = output[0, detectionIndex, baseIdx + 2];
            }
            else
            {
                // [1, 56, 8400] format
                kpX = output[0, baseIdx, detectionIndex];
                kpY = output[0, baseIdx + 1, detectionIndex];
                kpConf = output[0, baseIdx + 2, detectionIndex];
            }

            // Remove letterbox padding
            kpX = (kpX - offsetX) / scale;
            kpY = (kpY - offsetY) / scale;

            keypoints.Add(new Keypoint
            {
                X = kpX,
                Y = kpY,
                Confidence = kpConf,
                Name = keypointNames[k]
            });
        }

        return keypoints;
    }

    private List<PersonDetection> ApplyNMS(List<PersonDetection> detections, float iouThreshold)
    {
        // Sort by confidence (highest first)
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var results = new List<PersonDetection>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            results.Add(best);
            sorted.RemoveAt(0);

            // Remove overlapping detections
            sorted = sorted.Where(d => CalculateIOU(best, d) < iouThreshold).ToList();
        }

        return results;
    }

    private float CalculateIOU(PersonDetection a, PersonDetection b)
    {
        // Calculate intersection
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        float intersectionArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        
        // Calculate union
        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float unionArea = areaA + areaB - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0;
    }

    #endregion

    #region Motion Detection (Phase 2)

    private void StartMotionTracking_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0)
        {
            MessageBox.Show("Please define at least one zone before starting motion tracking.", 
                "No Zones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        isMotionTrackingActive = !isMotionTrackingActive;

        if (isMotionTrackingActive)
        {
            // Mark all zones as tracking enabled
            foreach (var zone in workZones)
            {
                zone.IsTracking = true;
                zone.PreviousFrame = null; // Reset frame buffer
            }

            // Start timer to check for motion every 500ms
            motionDetectionTimer = new System.Windows.Threading.DispatcherTimer();
            motionDetectionTimer.Interval = TimeSpan.FromMilliseconds(motionDetectionIntervalMs);
            motionDetectionTimer.Tick += MotionDetectionTimer_Tick;
            motionDetectionTimer.Start();

            StatusText.Text = "Motion tracking started for all zones";
            ((MenuItem)sender).Header = "⏸ Stop Motion Tracking";
        }
        else
        {
            StopMotionTracking();
            StatusText.Text = "Motion tracking stopped";
            ((MenuItem)sender).Header = "▶ Start Motion Tracking";
        }
    }

    private void StopMotionTracking()
    {
        isMotionTrackingActive = false;
        motionDetectionTimer?.Stop();
        
        foreach (var zone in workZones)
        {
            zone.IsTracking = false;
            zone.PreviousFrame = null;
        }
    }

    private void MotionDetectionTimer_Tick(object? sender, EventArgs e)
    {
        if (VideoPlayer.Source == null || VideoPlayer.NaturalVideoWidth == 0)
            return;

        try
        {
            // Capture current video frame
            var currentFrame = CaptureVideoFrame();
            if (currentFrame == null) return;

            // Detect all people if using pose model (for skeleton overlay)
            List<PersonDetection>? allDetections = null;
            if (showSkeletonOverlay && usePoseModel && yoloSession != null)
            {
                int videoWidth = VideoPlayer.NaturalVideoWidth;
                int videoHeight = VideoPlayer.NaturalVideoHeight;
                allDetections = DetectPersonsWithYolo(currentFrame, videoWidth, videoHeight);
                
                System.Diagnostics.Debug.WriteLine($"Detected {allDetections.Count} people, {allDetections.Count(d => d.Keypoints != null)} with keypoints");
                
                // Draw skeletons on UI thread
                Dispatcher.Invoke(() => DrawSkeletonOverlays(allDetections, videoWidth, videoHeight));
            }

            // Check motion in each tracking zone
            foreach (var zone in workZones.Where(z => z.IsTracking))
            {
                DetectMotionInZone(zone, currentFrame);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Motion detection error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private byte[]? CaptureVideoFrame()
    {
        try
        {
            // Create bitmap from video element
            int width = VideoPlayer.NaturalVideoWidth;
            int height = VideoPlayer.NaturalVideoHeight;

            if (width == 0 || height == 0) return null;

            var renderBitmap = new RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);

            renderBitmap.Render(VideoPlayer);

            // Convert to byte array
            int stride = width * 4; // 4 bytes per pixel (BGRA)
            byte[] pixels = new byte[height * stride];
            renderBitmap.CopyPixels(pixels, stride, 0);

            return pixels;
        }
        catch
        {
            return null;
        }
    }

    private void DetectMotionInZone(WorkZone zone, byte[] currentFrame)
    {
        int videoWidth = VideoPlayer.NaturalVideoWidth;
        int videoHeight = VideoPlayer.NaturalVideoHeight;

        // Use YOLO AI detection if available
        if (yoloSession != null)
        {
            DetectWithYolo(zone, currentFrame, videoWidth, videoHeight);
        }
        else
        {
            // Fallback to motion blob detection
            DetectWithMotionBlobs(zone, currentFrame, videoWidth, videoHeight);
        }
    }

    private void DetectWithYolo(WorkZone zone, byte[] currentFrame, int videoWidth, int videoHeight)
    {
        // Calculate zone bounds in video coordinates
        int zoneX = (int)(zone.X * videoWidth / VideoContainer.Width);
        int zoneY = (int)(zone.Y * videoHeight / VideoContainer.Height);
        int zoneWidth = (int)(zone.Width * videoWidth / VideoContainer.Width);
        int zoneHeight = (int)(zone.Height * videoHeight / VideoContainer.Height);

        // Clamp to video bounds
        zoneX = Math.Max(0, Math.Min(zoneX, videoWidth - 1));
        zoneY = Math.Max(0, Math.Min(zoneY, videoHeight - 1));
        zoneWidth = Math.Min(zoneWidth, videoWidth - zoneX);
        zoneHeight = Math.Min(zoneHeight, videoHeight - zoneY);

        // Detect persons in entire frame
        var allPersons = DetectPersonsWithYolo(currentFrame, videoWidth, videoHeight);

        // Filter to persons within this zone
        var personsInZone = new List<PersonDetection>();
        foreach (var person in allPersons)
        {
            // Check if person center is within zone
            if (person.CenterX >= zoneX && person.CenterX < zoneX + zoneWidth &&
                person.CenterY >= zoneY && person.CenterY < zoneY + zoneHeight)
            {
                // Convert to zone-relative coordinates
                person.CenterX -= zoneX;
                person.CenterY -= zoneY;
                personsInZone.Add(person);
            }
        }

        // Update person tracking with AI detections
        UpdatePersonTrackingYolo(zone, personsInZone);
        
        zone.CurrentMotionLevel = personsInZone.Count * 10.0; // Rough motion level indicator
        zone.MotionDetected = personsInZone.Count > 0;
    }

    private void DetectWithMotionBlobs(WorkZone zone, byte[] currentFrame, int videoWidth, int videoHeight)
    {
        if (zone.PreviousFrame == null)
        {
            zone.PreviousFrame = currentFrame;
            return;
        }

        // Calculate zone bounds in video coordinates
        int zoneX = (int)(zone.X * videoWidth / VideoContainer.Width);
        int zoneY = (int)(zone.Y * videoHeight / VideoContainer.Height);
        int zoneWidth = (int)(zone.Width * videoWidth / VideoContainer.Width);
        int zoneHeight = (int)(zone.Height * videoHeight / VideoContainer.Height);

        // Clamp to video bounds
        zoneX = Math.Max(0, Math.Min(zoneX, videoWidth - 1));
        zoneY = Math.Max(0, Math.Min(zoneY, videoHeight - 1));
        zoneWidth = Math.Min(zoneWidth, videoWidth - zoneX);
        zoneHeight = Math.Min(zoneHeight, videoHeight - zoneY);

        int stride = videoWidth * 4;

        // Create motion map
        bool[,] motionMap = new bool[zoneWidth, zoneHeight];
        int changedPixels = 0;

        for (int y = 0; y < zoneHeight; y++)
        {
            for (int x = 0; x < zoneWidth; x++)
            {
                int videoX = zoneX + x;
                int videoY = zoneY + y;
                int idx = videoY * stride + videoX * 4;
                
                if (idx + 3 >= currentFrame.Length) continue;

                int diff = Math.Abs(currentFrame[idx] - zone.PreviousFrame[idx]) +
                          Math.Abs(currentFrame[idx + 1] - zone.PreviousFrame[idx + 1]) +
                          Math.Abs(currentFrame[idx + 2] - zone.PreviousFrame[idx + 2]);

                if (diff / 3 > zone.MotionThreshold)
                {
                    motionMap[x, y] = true;
                    changedPixels++;
                }
            }
        }

        // Detect motion blobs (people)
        var motionBlobs = DetectMotionBlobs(motionMap, zoneWidth, zoneHeight, zone.MinMotionPixels);
        
        // Update zone's person count and tracking
        UpdatePersonTracking(zone, motionBlobs, zoneX, zoneY);

        // Update motion level
        int totalPixels = zoneWidth * zoneHeight;
        zone.CurrentMotionLevel = totalPixels > 0 ? (double)changedPixels / totalPixels * 100 : 0;
        zone.MotionDetected = changedPixels > zone.MinMotionPixels;

        zone.PreviousFrame = currentFrame;
    }

    private List<MotionBlob> DetectMotionBlobs(bool[,] motionMap, int width, int height, int minSize)
    {
        var blobs = new List<MotionBlob>();
        bool[,] visited = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (motionMap[x, y] && !visited[x, y])
                {
                    var blob = FloodFillBlob(motionMap, visited, x, y, width, height);
                    if (blob.PixelCount >= minSize)
                    {
                        blobs.Add(blob);
                    }
                }
            }
        }

        return blobs;
    }

    private MotionBlob FloodFillBlob(bool[,] motionMap, bool[,] visited, int startX, int startY, int width, int height)
    {
        var blob = new MotionBlob();
        var stack = new Stack<(int x, int y)>();
        stack.Push((startX, startY));

        int minX = startX, maxX = startX, minY = startY, maxY = startY;
        int sumX = 0, sumY = 0, count = 0;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            
            if (x < 0 || x >= width || y < 0 || y >= height || visited[x, y] || !motionMap[x, y])
                continue;

            visited[x, y] = true;
            count++;
            sumX += x;
            sumY += y;

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);

            // Check 8 neighbors
            stack.Push((x - 1, y));
            stack.Push((x + 1, y));
            stack.Push((x, y - 1));
            stack.Push((x, y + 1));
            stack.Push((x - 1, y - 1));
            stack.Push((x + 1, y - 1));
            stack.Push((x - 1, y + 1));
            stack.Push((x + 1, y + 1));
        }

        blob.CenterX = count > 0 ? sumX / count : startX;
        blob.CenterY = count > 0 ? sumY / count : startY;
        blob.MinX = minX;
        blob.MaxX = maxX;
        blob.MinY = minY;
        blob.MaxY = maxY;
        blob.PixelCount = count;

        return blob;
    }

    private void UpdatePersonTracking(WorkZone zone, List<MotionBlob> blobs, int zoneX, int zoneY)
    {
        var now = DateTime.Now;
        const double MAX_MOVEMENT_DISTANCE = 100; // pixels
        const double PERSON_TIMEOUT_SECONDS = 3.0;

        // Remove people who haven't been seen recently
        zone.People.RemoveAll(p => (now - p.LastSeen).TotalSeconds > PERSON_TIMEOUT_SECONDS);

        // Match blobs to existing people or create new ones
        var unmatchedBlobs = new List<MotionBlob>(blobs);
        
        foreach (var person in zone.People.ToList())
        {
            MotionBlob? closestBlob = null;
            double minDistance = MAX_MOVEMENT_DISTANCE;

            foreach (var blob in unmatchedBlobs)
            {
                double dist = Math.Sqrt(
                    Math.Pow(blob.CenterX - person.X, 2) +
                    Math.Pow(blob.CenterY - person.Y, 2));

                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestBlob = blob;
                }
            }

            if (closestBlob != null)
            {
                // Update existing person
                person.X = closestBlob.CenterX;
                person.Y = closestBlob.CenterY;
                person.LastSeen = now;
                person.MotionCenters.Add(new System.Windows.Point(closestBlob.CenterX, closestBlob.CenterY));
                if (person.MotionCenters.Count > 30) // Keep last 30 positions
                    person.MotionCenters.RemoveAt(0);
                
                unmatchedBlobs.Remove(closestBlob);
            }
        }

        // Create new people for unmatched blobs
        foreach (var blob in unmatchedBlobs)
        {
            var newPerson = new Person
            {
                Id = nextPersonId++,
                X = blob.CenterX,
                Y = blob.CenterY,
                FirstSeen = now,
                LastSeen = now
            };
            newPerson.MotionCenters.Add(new System.Windows.Point(blob.CenterX, blob.CenterY));
            zone.People.Add(newPerson);
            zone.EntryCount++;

            // Log entry event
            var zoneEvent = new ZoneEvent
            {
                Timestamp = now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Person {newPerson.Id} Entry",
                ZoneName = zone.Name
            };
            zone.Events.Add(zoneEvent);
        }

        // Update people count
        int previousCount = zone.PeopleCount;
        zone.PeopleCount = zone.People.Count;

        // Trigger UI update if count changed
        if (previousCount != zone.PeopleCount)
        {
            DrawAllZones();
            if (ZoneLogPanel.Visibility == Visibility.Visible)
                UpdateZoneEventLog();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                UpdateZoneTimeline();

            if (zone.PeopleCount > previousCount)
                StatusText.Text = $"🔍 Person detected entering {zone.Name} (now {zone.PeopleCount} people)";
            else
                StatusText.Text = $"🔍 Person left {zone.Name} (now {zone.PeopleCount} people)";
        }
    }

    private void UpdatePersonTrackingYolo(WorkZone zone, List<PersonDetection> detections)
    {
        var now = DateTime.Now;
        const double PERSON_TIMEOUT_SECONDS = 1.5; // Shorter timeout for AI detection

        // Remove people who haven't been seen recently
        var exitedPeople = zone.People.Where(p => (now - p.LastSeen).TotalSeconds > PERSON_TIMEOUT_SECONDS).ToList();
        foreach (var exitedPerson in exitedPeople)
        {
            zone.People.Remove(exitedPerson);
            
            // Log exit event
            var exitEvent = new ZoneEvent
            {
                Timestamp = now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Person {exitedPerson.Id} Exit",
                ZoneName = zone.Name
            };
            zone.Events.Add(exitEvent);
        }

        // Match detections to existing people using IOU + distance
        var unmatchedDetections = new List<PersonDetection>(detections);
        var matchedPeople = new HashSet<Person>();
        
        foreach (var person in zone.People.ToList())
        {
            PersonDetection? bestMatch = null;
            double bestScore = 0;

            // Predict next position based on velocity
            var predictedX = person.X;
            var predictedY = person.Y;
            if (person.MotionCenters.Count >= 2)
            {
                var lastPos = person.MotionCenters[person.MotionCenters.Count - 1];
                var prevPos = person.MotionCenters[person.MotionCenters.Count - 2];
                var velX = lastPos.X - prevPos.X;
                var velY = lastPos.Y - prevPos.Y;
                predictedX = lastPos.X + velX;
                predictedY = lastPos.Y + velY;
            }

            foreach (var detection in unmatchedDetections)
            {
                // Calculate distance to predicted position
                double dist = Math.Sqrt(
                    Math.Pow(detection.CenterX - predictedX, 2) +
                    Math.Pow(detection.CenterY - predictedY, 2));
                
                // Normalize distance (0 = same position, 1 = far away)
                double normalizedDist = Math.Min(dist / 200.0, 1.0);
                
                // Calculate IOU with last known bbox (if we store it)
                double iou = 0.5; // Default if no bbox history
                
                // Combined score: higher is better
                double score = (1.0 - normalizedDist) * 0.7 + iou * 0.3 + detection.Confidence * 0.2;

                if (score > bestScore && score > 0.3) // Minimum threshold
                {
                    bestScore = score;
                    bestMatch = detection;
                }
            }

            if (bestMatch != null)
            {
                // Update existing person with smoothing
                const double SMOOTHING = 0.7; // 0 = use only new position, 1 = don't move
                person.X = person.X * SMOOTHING + bestMatch.CenterX * (1 - SMOOTHING);
                person.Y = person.Y * SMOOTHING + bestMatch.CenterY * (1 - SMOOTHING);
                person.LastSeen = now;
                person.MotionCenters.Add(new System.Windows.Point(bestMatch.CenterX, bestMatch.CenterY));
                if (person.MotionCenters.Count > 30)
                    person.MotionCenters.RemoveAt(0);
                
                matchedPeople.Add(person);
                unmatchedDetections.Remove(bestMatch);
            }
        }

        // Create new people for unmatched detections (with confidence filter)
        foreach (var detection in unmatchedDetections)
        {
            // Only create new person if confidence is high enough
            if (detection.Confidence < 0.6f)
                continue;

            var newPerson = new Person
            {
                Id = nextPersonId++,
                X = detection.CenterX,
                Y = detection.CenterY,
                FirstSeen = now,
                LastSeen = now
            };
            newPerson.MotionCenters.Add(new System.Windows.Point(detection.CenterX, detection.CenterY));
            zone.People.Add(newPerson);
            zone.EntryCount++;

            // Log entry event with confidence
            var zoneEvent = new ZoneEvent
            {
                Timestamp = now,
                VideoTimeInSeconds = VideoPlayer.Position.TotalSeconds,
                EventType = $"Person {newPerson.Id} Entry (AI: {detection.Confidence:P0})",
                ZoneName = zone.Name
            };
            zone.Events.Add(zoneEvent);
        }

        // Update people count
        int previousCount = zone.PeopleCount;
        zone.PeopleCount = zone.People.Count;

        // Trigger UI update if count changed
        if (previousCount != zone.PeopleCount)
        {
            DrawAllZones();
            if (ZoneLogPanel.Visibility == Visibility.Visible)
                UpdateZoneEventLog();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                UpdateZoneTimeline();

            if (zone.PeopleCount > previousCount)
                StatusText.Text = $"🤖 AI detected person entering {zone.Name} (now {zone.PeopleCount} people)";
            else
                StatusText.Text = $"🤖 Person left {zone.Name} (now {zone.PeopleCount} people)";
        }
    }

    private void DrawSkeletonOverlays(List<PersonDetection> detections, int videoWidth, int videoHeight)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"DrawSkeletonOverlays called with {detections.Count} detections");
            
            // Clear existing skeleton overlay
            if (skeletonOverlay != null)
            {
                VideoContainer.Children.Remove(skeletonOverlay);
                skeletonOverlay = null;
            }

            // Create new overlay canvas
            skeletonOverlay = new Canvas
            {
                Width = VideoContainer.ActualWidth,
                Height = VideoContainer.ActualHeight,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            if (videoWidth == 0 || videoHeight == 0)
            {
                System.Diagnostics.Debug.WriteLine("Video dimensions are zero");
                return;
            }

            // Get scale factors between video and display
            double scaleX = VideoContainer.ActualWidth / videoWidth;
            double scaleY = VideoContainer.ActualHeight / videoHeight;
            
            System.Diagnostics.Debug.WriteLine($"Video: {videoWidth}x{videoHeight}, Container: {VideoContainer.ActualWidth}x{VideoContainer.ActualHeight}, Scale: {scaleX}x{scaleY}");

            int drawnCount = 0;
            // Draw skeleton for each detected person
            foreach (var detection in detections)
            {
                if (detection.Keypoints == null || detection.Keypoints.Count != 17)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping detection - keypoints: {detection.Keypoints?.Count ?? 0}");
                    continue;
                }

                DrawSkeleton(detection.Keypoints, 0, 0, scaleX, scaleY, detection.Confidence);
                drawnCount++;
            }

            // Add overlay to container
            VideoContainer.Children.Add(skeletonOverlay);
            Canvas.SetZIndex(skeletonOverlay, 100); // On top of video but below zones
            
            System.Diagnostics.Debug.WriteLine($"Drew {drawnCount} skeletons, overlay added with {skeletonOverlay.Children.Count} children");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in DrawSkeletonOverlays: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void DrawSkeleton(List<Keypoint> keypoints, int offsetX, int offsetY, double scaleX, double scaleY, float confidence)
    {
        if (skeletonOverlay == null) return;

        // Define skeleton connections (COCO format)
        var connections = new[]
        {
            (0, 1), (0, 2), // nose to eyes
            (1, 3), (2, 4), // eyes to ears
            (0, 5), (0, 6), // nose to shoulders
            (5, 6), // shoulders
            (5, 7), (7, 9), // left arm
            (6, 8), (8, 10), // right arm
            (5, 11), (6, 12), // shoulders to hips
            (11, 12), // hips
            (11, 13), (13, 15), // left leg
            (12, 14), (14, 16) // right leg
        };

        // Draw connections (bones)
        foreach (var (start, end) in connections)
        {
            var kp1 = keypoints[start];
            var kp2 = keypoints[end];

            // Only draw if both keypoints are visible
            if (kp1.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD && 
                kp2.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD)
            {
                double x1 = (kp1.X + offsetX) * scaleX;
                double y1 = (kp1.Y + offsetY) * scaleY;
                double x2 = (kp2.X + offsetX) * scaleX;
                double y2 = (kp2.Y + offsetY) * scaleY;

                var line = new Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 255, 0)), // Semi-transparent green
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                skeletonOverlay.Children.Add(line);
            }
        }

        // Draw keypoints (joints)
        for (int i = 0; i < keypoints.Count; i++)
        {
            var kp = keypoints[i];
            if (kp.Confidence > KEYPOINT_CONFIDENCE_THRESHOLD)
            {
                double x = (kp.X + offsetX) * scaleX;
                double y = (kp.Y + offsetY) * scaleY;

                var circle = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)), // Red
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(circle, x - 4);
                Canvas.SetTop(circle, y - 4);
                skeletonOverlay.Children.Add(circle);
            }
        }

        // Draw confidence text
        if (keypoints.Count > 0 && keypoints[0].Confidence > KEYPOINT_CONFIDENCE_THRESHOLD)
        {
            var nose = keypoints[0];
            double textX = (nose.X + offsetX) * scaleX;
            double textY = (nose.Y + offsetY) * scaleY - 20;

            var text = new TextBlock
            {
                Text = $"{confidence:P0}",
                Foreground = Brushes.Yellow,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
            };

            Canvas.SetLeft(text, textX);
            Canvas.SetTop(text, textY);
            skeletonOverlay.Children.Add(text);
        }
    }

    private void ExportZoneData_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0 || !workZones.Any(z => z.Events.Count > 0))
        {
            MessageBox.Show("No zone data to export. Please track some zone entries/exits first.",
                "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"ZoneData_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                ExportZoneDataToCSV(dialog.FileName);

                StatusText.Text = $"Zone data exported to {System.IO.Path.GetFileName(dialog.FileName)}";
                MessageBox.Show($"Zone data exported successfully to:\n{dialog.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting zone data: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportZoneDataToCSV(string filePath)
    {
        using (var writer = new System.IO.StreamWriter(filePath))
        {
            // Write header
            writer.WriteLine("Zone Name,Event Type,Video Time,Timestamp,Entry Count");

            // Write all events sorted by video time
            var allEvents = new List<(string zoneName, ZoneEvent evt, int entryCount)>();
            foreach (var zone in workZones)
            {
                foreach (var evt in zone.Events)
                {
                    allEvents.Add((zone.Name, evt, zone.EntryCount));
                }
            }

            foreach (var item in allEvents.OrderBy(e => e.evt.VideoTimeInSeconds))
            {
                var videoTime = TimeSpan.FromSeconds(item.evt.VideoTimeInSeconds).ToString(@"hh\:mm\:ss\.ff");
                var timestamp = item.evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                writer.WriteLine($"{item.zoneName},{item.evt.EventType},{videoTime},{timestamp},{item.entryCount}");
            }

            // Write summary statistics
            writer.WriteLine();
            writer.WriteLine("Zone Summary Statistics");
            writer.WriteLine("Zone Name,Total Entries,Total Events,Avg Time in Zone (seconds)");

            foreach (var zone in workZones)
            {
                var avgTimeInZone = CalculateAverageTimeInZone(zone);
                writer.WriteLine($"{zone.Name},{zone.EntryCount},{zone.Events.Count},{avgTimeInZone:F2}");
            }
        }
    }

    private double CalculateAverageTimeInZone(WorkZone zone)
    {
        var entries = new List<double>();
        var exits = new List<double>();

        foreach (var evt in zone.Events.OrderBy(e => e.VideoTimeInSeconds))
        {
            if (evt.EventType == "Entry")
                entries.Add(evt.VideoTimeInSeconds);
            else
                exits.Add(evt.VideoTimeInSeconds);
        }

        // Calculate time spent for each complete entry-exit pair
        double totalTime = 0;
        int pairCount = Math.Min(entries.Count, exits.Count);
        
        for (int i = 0; i < pairCount; i++)
        {
            totalTime += exits[i] - entries[i];
        }

        return pairCount > 0 ? totalTime / pairCount : 0;
    }

    private void ShowZoneStatistics_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0)
        {
            MessageBox.Show("No zones defined. Please create zones first.",
                "No Zones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var statsWindow = new Window
        {
            Title = "Zone Statistics",
            Width = 600,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var title = new TextBlock
        {
            Text = "📊 Zone Activity Statistics",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 15)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        // Statistics data grid
        var dataGrid = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(40, 40, 40))
        };

        // Style for cells to ensure white text
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.White));
        cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30))));
        dataGrid.CellStyle = cellStyle;

        // Style for headers
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(14, 99, 156))));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.ForegroundProperty, Brushes.White));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.PaddingProperty, new Thickness(5)));
        dataGrid.ColumnHeaderStyle = headerStyle;

        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Zone Name",
            Binding = new System.Windows.Data.Binding("Name"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Total Entries",
            Binding = new System.Windows.Data.Binding("EntryCount"),
            Width = 100
        });
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Total Events",
            Binding = new System.Windows.Data.Binding("EventCount"),
            Width = 100
        });
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Avg Time in Zone",
            Binding = new System.Windows.Data.Binding("AvgTime"),
            Width = 120
        });

        var statsData = workZones.Select(z => new
        {
            Name = z.Name,
            EntryCount = z.EntryCount,
            EventCount = z.Events.Count,
            AvgTime = TimeSpan.FromSeconds(CalculateAverageTimeInZone(z)).ToString(@"mm\:ss")
        }).ToList();

        dataGrid.ItemsSource = statsData;
        Grid.SetRow(dataGrid, 1);
        grid.Children.Add(dataGrid);

        // Close button
        var closeButton = new Button
        {
            Content = "Close",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 15, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (s, args) => statsWindow.Close();
        Grid.SetRow(closeButton, 2);
        grid.Children.Add(closeButton);

        statsWindow.Content = grid;
        statsWindow.ShowDialog();
    }

    #endregion

    #region Zone Timeline Visualization (Phase 3)

    private void UpdateZoneTimeline()
    {
        if (VideoPlayer.Source == null || cumulativeVideoTime == 0)
            return;

        ZoneTimelineCanvas.Children.Clear();

        double canvasWidth = ZoneTimelineCanvas.ActualWidth;
        if (canvasWidth == 0) canvasWidth = 800; // Default width

        int yOffset = 0;
        const int trackHeight = 20;
        const int trackSpacing = 5;

        foreach (var zone in workZones.Where(z => z.Events.Count > 0))
        {
            // Draw zone label background
            var labelBg = new Rectangle
            {
                Width = canvasWidth,
                Height = trackHeight,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
            };
            Canvas.SetLeft(labelBg, 0);
            Canvas.SetTop(labelBg, yOffset);
            ZoneTimelineCanvas.Children.Add(labelBg);

            // Draw zone name
            var label = new TextBlock
            {
                Text = zone.Name,
                Foreground = Brushes.White,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(label, 5);
            Canvas.SetTop(label, yOffset + 3);
            ZoneTimelineCanvas.Children.Add(label);

            // Draw occupancy bars
            var entries = zone.Events.Where(e => e.EventType == "Entry").OrderBy(e => e.VideoTimeInSeconds).ToList();
            var exits = zone.Events.Where(e => e.EventType == "Exit").OrderBy(e => e.VideoTimeInSeconds).ToList();

            int exitIndex = 0;
            foreach (var entry in entries)
            {
                // Find matching exit
                double exitTime = entry.VideoTimeInSeconds + 10; // Default 10 sec if no exit
                while (exitIndex < exits.Count && exits[exitIndex].VideoTimeInSeconds < entry.VideoTimeInSeconds)
                {
                    exitIndex++;
                }
                if (exitIndex < exits.Count)
                {
                    exitTime = exits[exitIndex].VideoTimeInSeconds;
                    exitIndex++;
                }

                // Calculate bar position and width
                double startX = (entry.VideoTimeInSeconds / cumulativeVideoTime) * canvasWidth;
                double endX = (exitTime / cumulativeVideoTime) * canvasWidth;
                double width = endX - startX;

                if (width > 1)
                {
                    var occupancyBar = new Rectangle
                    {
                        Width = width,
                        Height = trackHeight - 4,
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(zone.Color)),
                        Opacity = 0.7,
                        ToolTip = $"{zone.Name}: {TimeSpan.FromSeconds(entry.VideoTimeInSeconds):hh\\:mm\\:ss} - {TimeSpan.FromSeconds(exitTime):hh\\:mm\\:ss}"
                    };
                    Canvas.SetLeft(occupancyBar, startX);
                    Canvas.SetTop(occupancyBar, yOffset + 2);
                    ZoneTimelineCanvas.Children.Add(occupancyBar);
                }
            }

            yOffset += trackHeight + trackSpacing;
        }

        // Update canvas height
        ZoneTimelineCanvas.Height = yOffset > 0 ? yOffset : 100;
        
        // Show timeline panel if there's data
        if (workZones.Any(z => z.Events.Count > 0))
        {
            ZoneTimelinePanel.Visibility = Visibility.Visible;
        }
    }

    private void ToggleZoneTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (ZoneTimelinePanel.Visibility == Visibility.Visible)
        {
            ZoneTimelinePanel.Visibility = Visibility.Collapsed;
            ((MenuItem)sender).Header = "📊 Show Zone Timeline";
        }
        else
        {
            UpdateZoneTimeline();
            ZoneTimelinePanel.Visibility = Visibility.Visible;
            ((MenuItem)sender).Header = "📊 Hide Zone Timeline";
        }
    }

    #endregion

    #region Zone Configuration (Phase 4)

    private void ConfigureZoneSettings_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0)
        {
            MessageBox.Show("No zones defined. Please create zones first.",
                "No Zones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Show zone selector dialog
        var selectorWindow = new Window
        {
            Title = "Select Zone to Configure",
            Width = 400,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "⚙️ Select Zone to Configure",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 15)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var listBox = new System.Windows.Controls.ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            ItemsSource = workZones,
            DisplayMemberPath = "Name"
        };
        Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };

        var configButton = new Button
        {
            Content = "Configure",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0)
        };
        configButton.Click += (s, args) =>
        {
            if (listBox.SelectedItem is WorkZone selectedZone)
            {
                selectorWindow.Close();
                ShowZoneConfigDialog(selectedZone);
            }
        };
        buttonPanel.Children.Add(configButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 30
        };
        cancelButton.Click += (s, args) => selectorWindow.Close();
        buttonPanel.Children.Add(cancelButton);

        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        selectorWindow.Content = grid;
        selectorWindow.ShowDialog();
    }

    private void ShowZoneConfigDialog(WorkZone zone)
    {
        var configWindow = new Window
        {
            Title = $"Configure Zone: {zone.Name}",
            Width = 450,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(15)
        };

        var stack = new StackPanel();

        // Title
        var title = new TextBlock
        {
            Text = $"⚙️ Zone Configuration: {zone.Name}",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 20)
        };
        stack.Children.Add(title);

        // Motion Threshold
        stack.Children.Add(new TextBlock
        {
            Text = "Motion Sensitivity:",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 10, 0, 5),
            FontSize = 12
        });
        
        var thresholdSlider = new System.Windows.Controls.Slider
        {
            Minimum = 5,
            Maximum = 100,
            Value = zone.MotionThreshold,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft
        };
        var thresholdLabel = new TextBlock
        {
            Text = $"Current: {zone.MotionThreshold:F0} (Lower = More Sensitive)",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        thresholdSlider.ValueChanged += (s, e) =>
        {
            thresholdLabel.Text = $"Current: {e.NewValue:F0} (Lower = More Sensitive)";
        };
        stack.Children.Add(thresholdSlider);
        stack.Children.Add(thresholdLabel);

        // Minimum Motion Pixels
        stack.Children.Add(new TextBlock
        {
            Text = "Minimum Motion Pixels:",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 15, 0, 5),
            FontSize = 12
        });
        
        var minPixelsSlider = new System.Windows.Controls.Slider
        {
            Minimum = 10,
            Maximum = 500,
            Value = zone.MinMotionPixels,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft
        };
        var minPixelsLabel = new TextBlock
        {
            Text = $"Current: {zone.MinMotionPixels} pixels",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        minPixelsSlider.ValueChanged += (s, e) =>
        {
            minPixelsLabel.Text = $"Current: {e.NewValue:F0} pixels";
        };
        stack.Children.Add(minPixelsSlider);
        stack.Children.Add(minPixelsLabel);

        // Zone Info
        stack.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 20) });
        
        stack.Children.Add(new TextBlock
        {
            Text = "Zone Information:",
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12
        });

        var infoStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"Position: ({zone.X:F0}, {zone.Y:F0})",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"Size: {zone.Width:F0} × {zone.Height:F0}",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"Total Entries: {zone.EntryCount}",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"Total Events: {zone.Events.Count}",
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });
        stack.Children.Add(infoStack);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var saveButton = new Button
        {
            Content = "Save",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
            Foreground = Brushes.White
        };
        saveButton.Click += (s, args) =>
        {
            zone.MotionThreshold = thresholdSlider.Value;
            zone.MinMotionPixels = (int)minPixelsSlider.Value;
            configWindow.Close();
            StatusText.Text = $"Settings saved for {zone.Name}";
        };
        buttonPanel.Children.Add(saveButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 30
        };
        cancelButton.Click += (s, args) => configWindow.Close();
        buttonPanel.Children.Add(cancelButton);

        stack.Children.Add(buttonPanel);
        scrollViewer.Content = stack;
        configWindow.Content = scrollViewer;
        configWindow.ShowDialog();
    }

    private void RenameZone(WorkZone zone)
    {
        var dialog = new Window
        {
            Title = "Rename Zone",
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "New zone name:",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Text = zone.Name,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Padding = new Thickness(5)
        };
        textBox.SelectAll();
        textBox.Focus();
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(buttonPanel, 3);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0)
        };
        okButton.Click += (s, args) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                zone.Name = textBox.Text.Trim();
                DrawAllZones();
                if (ZoneTimelinePanel.Visibility == Visibility.Visible)
                {
                    UpdateZoneTimeline();
                }
                dialog.Close();
                StatusText.Text = $"Zone renamed to '{zone.Name}'";
            }
        };
        buttonPanel.Children.Add(okButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 30
        };
        cancelButton.Click += (s, args) => dialog.Close();
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(buttonPanel);
        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private void DeleteZone(WorkZone zone)
    {
        var result = MessageBox.Show(
            $"Are you sure you want to delete zone '{zone.Name}'?\nThis will remove {zone.Events.Count} recorded events.",
            "Delete Zone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            workZones.Remove(zone);
            DrawAllZones();
            if (ZoneTimelinePanel.Visibility == Visibility.Visible)
            {
                UpdateZoneTimeline();
            }
            if (ZoneLogPanel.Visibility == Visibility.Visible)
            {
                UpdateZoneEventLog();
            }
            StatusText.Text = $"Zone '{zone.Name}' deleted";
        }
    }

    #endregion

    #region Zone Persistence & Integration (Phase 5)

    private void SaveZones_Click(object sender, RoutedEventArgs e)
    {
        if (workZones.Count == 0)
        {
            MessageBox.Show("No zones to save. Please create zones first.",
                "No Zones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Zone Template (*.zones)|*.zones|All files (*.*)|*.*",
            DefaultExt = ".zones",
            FileName = $"ZoneTemplate_{DateTime.Now:yyyyMMdd}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var zoneData = new List<ZoneSaveData>();
                foreach (var zone in workZones)
                {
                    zoneData.Add(new ZoneSaveData
                    {
                        Name = zone.Name,
                        X = zone.X,
                        Y = zone.Y,
                        Width = zone.Width,
                        Height = zone.Height,
                        Color = zone.Color,
                        MotionThreshold = zone.MotionThreshold,
                        MinMotionPixels = zone.MinMotionPixels
                    });
                }

                var json = System.Text.Json.JsonSerializer.Serialize(zoneData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(dialog.FileName, json);

                StatusText.Text = $"Zones saved to {System.IO.Path.GetFileName(dialog.FileName)}";
                MessageBox.Show($"Zone template saved successfully to:\n{dialog.FileName}\n\nYou can load this template for other videos.",
                    "Zones Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving zones: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadZones_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Zone Template (*.zones)|*.zones|All files (*.*)|*.*",
            DefaultExt = ".zones"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = System.IO.File.ReadAllText(dialog.FileName);
                var zoneData = System.Text.Json.JsonSerializer.Deserialize<List<ZoneSaveData>>(json);

                if (zoneData == null || zoneData.Count == 0)
                {
                    MessageBox.Show("No zones found in file.",
                        "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Ask if user wants to replace or append
                var result = MessageBoxResult.Yes;
                if (workZones.Count > 0)
                {
                    result = MessageBox.Show(
                        $"You have {workZones.Count} existing zone(s).\n\nReplace existing zones with template?\n(No will add template zones to existing ones)",
                        "Load Zones",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel)
                        return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    workZones.Clear();
                }

                foreach (var data in zoneData)
                {
                    workZones.Add(new WorkZone
                    {
                        Name = data.Name,
                        X = data.X,
                        Y = data.Y,
                        Width = data.Width,
                        Height = data.Height,
                        Color = data.Color,
                        MotionThreshold = data.MotionThreshold,
                        MinMotionPixels = data.MinMotionPixels,
                        IsTracking = false,
                        PeopleCount = 0,
                        EntryCount = 0,
                        Events = new List<ZoneEvent>()
                    });
                }

                DrawAllZones();
                StatusText.Text = $"Loaded {zoneData.Count} zone(s) from template";
                MessageBox.Show($"Successfully loaded {zoneData.Count} zone(s) from template.",
                    "Zones Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading zones: {ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    private void EditElements_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ElementEditorWindow(new List<string>(elementLibrary));
        editor.Owner = this;
        
        if (editor.ShowDialog() == true)
        {
            elementLibrary = editor.GetElements();
            
            // Update the element library dropdown
            ElementLibraryComboBox.Items.Clear();
            foreach (var element in elementLibrary)
            {
                ElementLibraryComboBox.Items.Add(new ComboBoxItem { Content = element });
            }
            
            StatusText.Text = $"Element library updated: {elementLibrary.Count} elements";
        }
    }

    private void RenameSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string tagStr)
        {
            int segmentNumber = int.Parse(tagStr);
            string currentName = segmentNames.ContainsKey(segmentNumber) ? segmentNames[segmentNumber] : $"Seg {segmentNumber}";
            
            var dialog = new Window
            {
                Title = "Rename Segment",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                ResizeMode = ResizeMode.NoResize
            };
            
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var label = new TextBlock
            {
                Text = "Enter new name:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);
            
            var textBox = new TextBox
            {
                Text = currentName,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Padding = new Thickness(5),
                FontSize = 14
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 3);
            
            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okButton.Click += (s, args) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    segmentNames[segmentNumber] = textBox.Text.Trim();
                    UpdateSegmentLabels();
                    dialog.DialogResult = true;
                }
            };
            
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            cancelButton.Click += (s, args) => dialog.DialogResult = false;
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);
            
            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();
            
            dialog.ShowDialog();
        }
    }

    private void UpdateSegmentLabels()
    {
        if (segmentNames.ContainsKey(0)) Segment0Text.Text = segmentNames[0];
        if (segmentNames.ContainsKey(1)) Segment1Text.Text = segmentNames[1];
        if (segmentNames.ContainsKey(2)) Segment2Text.Text = segmentNames[2];
        if (segmentNames.ContainsKey(3)) Segment3Text.Text = segmentNames[3];
        if (segmentNames.ContainsKey(4)) Segment4Text.Text = segmentNames[4];
        if (segmentNames.ContainsKey(5)) Segment5Text.Text = segmentNames[5];
    }

    // Export to Excel (XML format, no external dependencies)
    private void ExportToExcel_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Excel files (*.xml)|*.xml|All files (*.*)|*.*";
        saveFileDialog.DefaultExt = "xml";
        saveFileDialog.FileName = "TimeStudy_Export.xml";
        
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                {
                    // Write Excel XML header
                    writer.WriteLine("<?xml version=\"1.0\"?>");
                    writer.WriteLine("<?mso-application progid=\"Excel.Sheet\"?>");
                    writer.WriteLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
                    writer.WriteLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
                    writer.WriteLine("<Worksheet ss:Name=\"Time Study\">");
                    writer.WriteLine("<Table>");
                    
                    // Video info row
                    writer.WriteLine("<Row>");
                    writer.WriteLine($"<Cell><Data ss:Type=\"String\">Video File:</Data></Cell>");
                    writer.WriteLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(currentVideoFileName)}</Data></Cell>");
                    writer.WriteLine("</Row>");
                    writer.WriteLine("<Row/>"); // Empty row
                    
                    // Header row with bold styling
                    writer.WriteLine("<Row ss:StyleID=\"Header\">");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Timestamp</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Segment</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Duration (sec)</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Element</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Description</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Observations</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">People</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Category</Data></Cell>");
                    writer.WriteLine("</Row>");
                    
                    // Data rows
                    foreach (var entry in timeStudyData)
                    {
                        writer.WriteLine("<Row>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{entry.Timestamp}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"Number\">{entry.Segment}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"Number\">{entry.TimeInSeconds}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(entry.ElementName)}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(entry.Description)}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(entry.Observations)}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{entry.People}</Data></Cell>");
                        writer.WriteLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(entry.Category)}</Data></Cell>");
                        writer.WriteLine("</Row>");
                    }
                    
                    // Summary section
                    writer.WriteLine("<Row/>"); // Empty row
                    writer.WriteLine("<Row>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Total Entries:</Data></Cell>");
                    writer.WriteLine($"<Cell><Data ss:Type=\"Number\">{timeStudyData.Count}</Data></Cell>");
                    writer.WriteLine("</Row>");
                    
                    var totalTime = timeStudyData.Sum(e => e.TimeInSeconds);
                    writer.WriteLine("<Row>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">Total Time:</Data></Cell>");
                    writer.WriteLine($"<Cell><Data ss:Type=\"Number\">{totalTime}</Data></Cell>");
                    writer.WriteLine("<Cell><Data ss:Type=\"String\">seconds</Data></Cell>");
                    writer.WriteLine("</Row>");
                    
                    writer.WriteLine("</Table>");
                    writer.WriteLine("</Worksheet>");
                    
                    // Add style definitions
                    writer.WriteLine("<Styles>");
                    writer.WriteLine("<Style ss:ID=\"Header\">");
                    writer.WriteLine("<Font ss:Bold=\"1\"/>");
                    writer.WriteLine("</Style>");
                    writer.WriteLine("</Styles>");
                    
                    writer.WriteLine("</Workbook>");
                }
                
                StatusText.Text = $"Data exported to Excel: {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                MessageBox.Show("Data exported to Excel successfully!\n\nNote: This file can be opened in Excel.", 
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Generate HTML Report
    private void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*";
        saveFileDialog.DefaultExt = "html";
        saveFileDialog.FileName = "TimeStudy_Report.html";
        
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                {
                    // Group data by segment
                    var segmentGroups = timeStudyData
                        .GroupBy(entry => entry.Segment)
                        .OrderBy(g => g.Key)
                        .ToList();
                    
                    // Start HTML document
                    WriteHtmlHeader(writer);
                    
                    // Generate section for each segment
                    foreach (var segmentGroup in segmentGroups)
                    {
                        int segmentNumber = segmentGroup.Key;
                        var segmentData = segmentGroup.ToList();
                        
                        // Calculate statistics for this segment
                        var durations = segmentData.Select(entry => entry.TimeInSeconds).Where(t => t > 0).ToList();
                        double totalTime = durations.Sum();
                        double avgTime = durations.Any() ? durations.Average() : 0;
                        double minTime = durations.Any() ? durations.Min() : 0;
                        double maxTime = durations.Any() ? durations.Max() : 0;
                        double stdDev = durations.Any() ? Math.Sqrt(durations.Average(v => Math.Pow(v - avgTime, 2))) : 0;
                        
                        var elementStats = segmentData
                            .Where(entry => !string.IsNullOrWhiteSpace(entry.ElementName))
                            .GroupBy(entry => entry.ElementName)
                            .Select(g => new
                            {
                                Element = g.Key,
                                Count = g.Count(),
                                TotalTime = g.Sum(entry => entry.TimeInSeconds),
                                AvgTime = g.Average(entry => entry.TimeInSeconds),
                                MinTime = g.Min(entry => entry.TimeInSeconds),
                                MaxTime = g.Max(entry => entry.TimeInSeconds),
                                AvgPeople = g.Average(entry => double.TryParse(entry.People, out var r) ? r : 1),
                                StdDev = Math.Sqrt(g.Average(entry => Math.Pow(entry.TimeInSeconds - g.Average(x => x.TimeInSeconds), 2)))
                            })
                            .OrderByDescending(x => x.TotalTime)
                            .ToList();
                        
                        double totalStandardTime = segmentData.Sum(entry => {
                            double people = double.TryParse(entry.People, out var p) ? p : 1;
                            return entry.TimeInSeconds * people;
                        });
                        
                        var elementColors = AssignElementColors(elementStats.Select(stat => stat.Element).ToList());
                        
                        // Write segment report
                        WriteSegmentReport(writer, segmentNumber, segmentData, elementStats.Cast<dynamic>().ToList(), 
                            elementColors, totalTime, avgTime, minTime, maxTime, stdDev, totalStandardTime);
                    }
                    
                    // Close HTML document
                    WriteHtmlFooter(writer);
                }
                
                StatusText.Text = $"Report generated: {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                
                var result = MessageBox.Show("Report generated successfully!\n\nWould you like to open it in your browser?", 
                    "Report Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Report Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void WriteEnhancedHtmlReport(StreamWriter writer, List<dynamic> elementStats, Dictionary<string, string> elementColors, 
        double totalTime, double avgTime, double minTime, double maxTime, double stdDev, double totalStandardTime)
    {
        // Declare chart data variables
        string chartLabels;
        string chartData;
        string chartColors;
        string chartCounts;
        string histogramLabels;
        string histogramData;
        
        // Debug: Log element stats
        System.Diagnostics.Debug.WriteLine($"Element Stats Count: {elementStats.Count}");
        foreach (var stat in elementStats)
        {
            System.Diagnostics.Debug.WriteLine($"Element: {stat.Element}, Count: {stat.Count}, TotalTime: {stat.TotalTime}");
        }
        
        // Generate chart data - convert to lists first to avoid dynamic/lambda issues
        var labels = new List<string>();
        var data = new List<string>();
        var colors = new List<string>();
        var counts = new List<string>();
        
        if (elementStats.Any())
        {
            foreach (var stat in elementStats)
            {
                labels.Add($"'{System.Security.SecurityElement.Escape(stat.Element)}'");
                data.Add(stat.TotalTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                colors.Add($"'{elementColors[stat.Element]}'");
                counts.Add(stat.Count.ToString());
            }
        }
        else
        {
            // Add placeholder data if no elements
            labels.Add("'No Data'");
            data.Add("0");
            colors.Add("'#cccccc'");
            counts.Add("0");
        }
        
        chartLabels = string.Join(",", labels);
        chartData = string.Join(",", data);
        chartColors = string.Join(",", colors);
        chartCounts = string.Join(",", counts);
        
        // Debug output
        System.Diagnostics.Debug.WriteLine($"Chart Labels: {chartLabels}");
        System.Diagnostics.Debug.WriteLine($"Chart Data: {chartData}");
        System.Diagnostics.Debug.WriteLine($"Chart Counts: {chartCounts}");
        
        // Calculate histogram data (duration buckets)
        var durations = timeStudyData.Select(e => e.TimeInSeconds).Where(d => d > 0).OrderBy(d => d).ToList();
        var histogramBuckets = new List<string>();
        var histogramCounts = new List<int>();
        
        if (durations.Any())
        {
            double minDur = durations.Min();
            double maxDur = durations.Max();
            double bucketSize = (maxDur - minDur) / 10; // 10 buckets
            if (bucketSize < 0.1) bucketSize = 0.1; // Minimum bucket size
            
            for (int i = 0; i < 10; i++)
            {
                double bucketStart = minDur + (i * bucketSize);
                double bucketEnd = bucketStart + bucketSize;
                int count = durations.Count(d => d >= bucketStart && d < bucketEnd);
                if (i == 9) count = durations.Count(d => d >= bucketStart); // Include last value
                histogramBuckets.Add($"'{bucketStart:F1}-{bucketEnd:F1}s'");
                histogramCounts.Add(count);
            }
        }
        
        histogramLabels = string.Join(",", histogramBuckets);
        histogramData = string.Join(",", histogramCounts);
        
        // Calculate cycle time data (for repeating elements)
        var cycleAnalysis = new Dictionary<string, List<double>>();
        foreach (var stat in elementStats)
        {
            var elementDurations = timeStudyData
                .Where(e => e.ElementName == stat.Element && e.TimeInSeconds > 0)
                .Select(e => e.TimeInSeconds)
                .ToList();
            if (elementDurations.Count > 1)
            {
                cycleAnalysis[stat.Element] = elementDurations;
            }
        }
        
        writer.WriteLine(@"<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<title>Time Study Report - Enhanced</title>
<script src='https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js'></script>
<script src='https://cdnjs.cloudflare.com/ajax/libs/html2pdf.js/0.10.1/html2pdf.bundle.min.js'></script>
<style>
* { box-sizing: border-box; }
body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; }
.container { max-width: 1400px; margin: 0 auto; background: white; border-radius: 15px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }
.header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; border-radius: 15px 15px 0 0; }
.header h1 { margin: 0; font-size: 32px; }
.header .subtitle { opacity: 0.9; margin-top: 10px; }
.content { padding: 30px; }
.toolbar { background: #f8f9fa; padding: 15px; border-radius: 8px; margin-bottom: 20px; display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
.btn { padding: 10px 20px; border: none; border-radius: 6px; cursor: pointer; font-weight: 600; transition: all 0.3s; }
.btn-primary { background: #667eea; color: white; }
.btn-primary:hover { background: #5568d3; transform: translateY(-2px); box-shadow: 0 4px 12px rgba(102,126,234,0.4); }
.btn-secondary { background: #6c757d; color: white; }
.btn-success { background: #28a745; color: white; }
.btn-success:hover { background: #218838; }
.filter-section { display: flex; gap: 10px; align-items: center; margin-left: auto; }
.filter-label { font-weight: 600; color: #333; }
#elementFilter { padding: 8px 12px; border: 2px solid #e9ecef; border-radius: 6px; min-width: 200px; }
.hidden-row { display: none; }
.info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 15px; margin: 20px 0; }
.info-card { background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%); padding: 15px; border-radius: 10px; border-left: 4px solid #667eea; }
.stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 20px; margin: 30px 0; }
.stat-card { background: white; padding: 25px; border-radius: 12px; text-align: center; box-shadow: 0 4px 15px rgba(0,0,0,0.1); transition: transform 0.3s; }
.stat-card:hover { transform: translateY(-5px); box-shadow: 0 6px 20px rgba(0,0,0,0.15); }
.stat-value { font-size: 36px; font-weight: bold; color: #667eea; margin: 10px 0; }
.stat-label { color: #6c757d; font-size: 14px; text-transform: uppercase; letter-spacing: 1px; }
.section { margin: 40px 0; }
.section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
.section-header h2 { color: #333; margin: 0; }
.toggle-btn { background: none; border: 2px solid #667eea; color: #667eea; padding: 8px 16px; border-radius: 6px; cursor: pointer; font-weight: 600; }
.toggle-btn:hover { background: #667eea; color: white; }
.collapsible-content { max-height: 2000px; overflow: hidden; transition: max-height 0.3s ease-in-out; }
.collapsible-content.collapsed { max-height: 0; }
.chart-container { display: grid; grid-template-columns: 1fr 1fr; gap: 30px; margin: 30px 0; }
.chart-box { background: white; padding: 20px; border-radius: 12px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
.chart-box h3 { margin-top: 0; color: #333; text-align: center; }
canvas { max-height: 400px; width: 100% !important; height: auto !important; }
.thumbnail-img { width: 80px; height: 60px; object-fit: cover; border-radius: 4px; border: 2px solid #ddd; cursor: pointer; transition: transform 0.3s, box-shadow 0.3s; }
.thumbnail-img:hover { transform: scale(1.5); box-shadow: 0 8px 20px rgba(0,0,0,0.3); z-index: 100; position: relative; }
.modal { display: none; position: fixed; z-index: 1000; left: 0; top: 0; width: 100%; height: 100%; background-color: rgba(0,0,0,0.9); }
.modal-content { margin: auto; display: block; max-width: 90%; max-height: 90%; position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); animation: zoom 0.3s; }
@keyframes zoom { from {transform: translate(-50%, -50%) scale(0);} to {transform: translate(-50%, -50%) scale(1);} }
.modal-close { position: absolute; top: 30px; right: 45px; color: #f1f1f1; font-size: 50px; font-weight: bold; cursor: pointer; transition: 0.3s; }
.modal-close:hover { color: #bbb; }
.element-bars { margin: 20px 0; }
.element-bar-item { margin: 15px 0; }
.element-bar-label { display: flex; justify-content: space-between; margin-bottom: 5px; font-weight: 600; }
.element-bar-bg { background: #e9ecef; height: 30px; border-radius: 15px; overflow: hidden; position: relative; }
.element-bar-fill { height: 100%; border-radius: 15px; display: flex; align-items: center; padding: 0 15px; color: white; font-weight: bold; transition: width 0.5s ease-out; }
table { width: 100%; border-collapse: collapse; margin: 20px 0; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
th { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 15px; text-align: left; font-weight: 600; position: sticky; top: 0; cursor: pointer; user-select: none; }
th:hover { background: linear-gradient(135deg, #5568d3 0%, #653a8c 100%); }
td { padding: 12px 15px; border-bottom: 1px solid #e9ecef; }
tr:hover { background: #f8f9fa; }
.rating-good { color: #28a745; font-weight: bold; }
.rating-fair { color: #ffc107; font-weight: bold; }
.rating-poor { color: #dc3545; font-weight: bold; }
.element-tag { display: inline-block; padding: 4px 12px; border-radius: 12px; font-size: 12px; font-weight: 600; color: white; }
.search-box { padding: 10px 15px; border: 2px solid #e9ecef; border-radius: 8px; width: 300px; font-size: 14px; }
.search-box:focus { outline: none; border-color: #667eea; }
.metrics-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; margin: 20px 0; }
.metric-card { background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
.metric-card h4 { margin-top: 0; color: #667eea; }
.metric-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #e9ecef; }
.timeline-viz { margin: 30px 0; }
.timeline-bar { background: #f8f9fa; height: 60px; border-radius: 8px; position: relative; overflow: hidden; box-shadow: inset 0 2px 4px rgba(0,0,0,0.1); }
.timeline-segment { position: absolute; height: 100%; display: flex; align-items: center; justify-content: center; color: white; font-size: 11px; font-weight: bold; border-right: 2px solid white; overflow: hidden; transition: all 0.3s; }
.timeline-segment:hover { opacity: 0.8; z-index: 10; }
.footer { margin-top: 50px; padding: 20px; text-align: center; color: #6c757d; border-top: 2px solid #e9ecef; }
.export-json { display: none; }
@media print {
  .toolbar, .toggle-btn, .chart-container, .modal { display: none; }
  .collapsible-content { max-height: none !important; }
  body { background: white; }
  .container { box-shadow: none; }
  .thumbnail-img:hover { transform: none; }
}
</style>
</head>
<body>");
        
        writer.WriteLine($@"<div class='container'>
<div class='header'>
<h1>📊 Video Time Study Report</h1>
<div class='subtitle'>Comprehensive Analysis &amp; Insights</div>
</div>
<div class='content'>

<div class='toolbar'>
<button class='btn btn-primary' onclick='window.print()'>🖨️ Print Report</button>
<button class='btn btn-secondary' onclick='exportToCSV()'>📥 Download CSV</button>
<button class='btn btn-secondary' onclick='exportJSON()'>📦 Export JSON</button>
<button class='btn btn-success' onclick='exportToPDF()'>📄 Export PDF</button>
<div class='filter-section'>
<label class='filter-label'>Filter Elements:</label>
<select id='elementFilter' onchange='filterElements()'>
<option value='all'>Show All Elements</option>");
        
        // Add element filter options
        foreach (var stat in elementStats)
        {
            writer.WriteLine($"<option value='{System.Security.SecurityElement.Escape(stat.Element)}'>Hide: {System.Security.SecurityElement.Escape(stat.Element)}</option>");
        }
        
        writer.WriteLine($@"</select>
</div>
</div>

<div class='info-grid'>
<div class='info-card'>
<strong>📹 Video File:</strong><br>{System.Security.SecurityElement.Escape(System.IO.Path.GetFileName(currentVideoFileName))}
</div>
<div class='info-card'>
<strong>📅 Generated:</strong><br>{DateTime.Now:yyyy-MM-dd HH:mm:ss}
</div>
<div class='info-card'>
<strong>⏱️ Study Duration:</strong><br>{TimeSpan.FromSeconds(totalTime).ToString(@"hh\:mm\:ss\.ff")}
</div>
<div class='info-card'>
<strong>📊 Total Entries:</strong><br>{timeStudyData.Count} observations
</div>
</div>

<div class='section'>
<h2>📈 Key Metrics</h2>
<div class='stats-grid'>
<div class='stat-card'>
<div class='stat-label'>Total Time</div>
<div class='stat-value'>{TimeSpan.FromSeconds(totalTime).ToString(@"hh\:mm\:ss")}</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Average Duration</div>
<div class='stat-value'>{avgTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Std Deviation</div>
<div class='stat-value'>{stdDev:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Min Duration</div>
<div class='stat-value'>{minTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Max Duration</div>
<div class='stat-value'>{maxTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Standard Time</div>
<div class='stat-value'>{TimeSpan.FromSeconds(totalStandardTime).ToString(@"hh\:mm\:ss")}</div>
</div>
</div>
</div>");

        // Charts section
        writer.WriteLine($@"
<!-- Chart Debug Info:
Element Stats Count: {elementStats.Count}
Labels: {chartLabels}
Data: {chartData}
Counts: {chartCounts}
Histogram Labels: {histogramLabels}
Histogram Data: {histogramData}
-->
<div class='section'>
<div class='section-header'>
<h2>📊 Visual Analysis</h2>
<button class='toggle-btn' onclick='toggleSection(this)'>Collapse</button>
</div>
<div class='collapsible-content'>
<div class='chart-container'>
<div class='chart-box'>
<h3>Time Distribution by Element</h3>
<canvas id='pieChart'></canvas>
</div>
<div class='chart-box'>
<h3>Element Frequency</h3>
<canvas id='barChart'></canvas>
</div>
<div class='chart-box'>
<h3>Duration Histogram</h3>
<canvas id='histogramChart'></canvas>
</div>
<div class='chart-box'>
<h3>Cycle Time Trends</h3>
<canvas id='cycleTimeChart'></canvas>
</div>
</div>
</div>
</div>");

        // Cycle Time Analysis section
        writer.WriteLine("<div class='section'><h2>🔄 Cycle Time Analysis</h2>");
        if (cycleAnalysis.Any())
        {
            writer.WriteLine("<div class='metrics-grid'>");
            foreach (var cycle in cycleAnalysis.Take(6)) // Show top 6 elements with cycle data
            {
                var cycleDurations = cycle.Value;
                var cycleAvg = cycleDurations.Average();
                var cycleStdDev = Math.Sqrt(cycleDurations.Average(v => Math.Pow(v - cycleAvg, 2)));
                var cycleMin = cycleDurations.Min();
                var cycleMax = cycleDurations.Max();
                var color = elementColors[cycle.Key];
                
                writer.WriteLine($@"<div class='metric-card'>
<h4 style='color:{color};'>{System.Security.SecurityElement.Escape(cycle.Key)}</h4>
<div class='metric-row'><span>Cycles:</span><span>{cycleDurations.Count}</span></div>
<div class='metric-row'><span>Average:</span><span>{cycleAvg:F2}s</span></div>
<div class='metric-row'><span>Std Dev:</span><span>{cycleStdDev:F2}s</span></div>
<div class='metric-row'><span>Range:</span><span>{cycleMin:F2}s - {cycleMax:F2}s</span></div>
<div class='metric-row'><span>CV:</span><span>{(cycleStdDev / cycleAvg * 100):F1}%</span></div>
</div>");
            }
            writer.WriteLine("</div>");
        }
        else
        {
            writer.WriteLine("<p>No cycle data available. Elements need multiple occurrences for cycle analysis.</p>");
        }
        writer.WriteLine("</div>");

        // Timeline visualization
        writer.WriteLine("<div class='section'><h2>🎯 Activity Timeline</h2><div class='timeline-viz'><div class='timeline-bar'>");
        double cumulativePercent = 0;
        foreach (var entry in timeStudyData)
        {
            if (entry.TimeInSeconds > 0 && !string.IsNullOrWhiteSpace(entry.ElementName))
            {
                double widthPercent = (entry.TimeInSeconds / totalTime) * 100;
                var color = elementColors.ContainsKey(entry.ElementName) ? elementColors[entry.ElementName] : "#999";
                writer.WriteLine($"<div class='timeline-segment' style='left:{cumulativePercent:F2}%; width:{widthPercent:F2}%; background:{color};' title='{System.Security.SecurityElement.Escape(entry.ElementName)}: {entry.TimeInSeconds:F1}s'>{System.Security.SecurityElement.Escape(entry.ElementName)}</div>");
                cumulativePercent += widthPercent;
            }
        }
        writer.WriteLine("</div></div></div>");

        // Element breakdown with bars
        writer.WriteLine("<div class='section'><h2>🔍 Element Breakdown</h2><div class='element-bars'>");
        if (elementStats.Any())
        {
            foreach (var stat in elementStats)
            {
                double percentage = (stat.TotalTime / totalTime) * 100;
                var color = elementColors[stat.Element];
                writer.WriteLine($@"<div class='element-bar-item'>
<div class='element-bar-label'>
<span><span class='element-tag' style='background:{color};'>{System.Security.SecurityElement.Escape(stat.Element)}</span> - {stat.Count}x occurrences</span>
<span>Avg: {stat.AvgTime:F2}s | σ: {stat.StdDev:F2}s</span>
<span>{stat.TotalTime:F2}s ({percentage:F1}%)</span>
</div>
<div class='element-bar-bg'>
<div class='element-bar-fill' style='width:{Math.Min(percentage, 100):F1}%; background:{color};'>&nbsp;</div>
</div>
</div>");
            }
        }
        else
        {
            writer.WriteLine("<p>No element data available.</p>");
        }
        writer.WriteLine("</div></div>");

        // Performance metrics
        writer.WriteLine($@"<div class='section'>
<h2>⚡ Performance Analysis</h2>
<div class='metrics-grid'>
<div class='metric-card'>
<h4>Efficiency Metrics</h4>
<div class='metric-row'><span>Observed Time:</span><span>{TimeSpan.FromSeconds(totalTime).ToString(@"hh\:mm\:ss")}</span></div>
<div class='metric-row'><span>Standard Time:</span><span>{TimeSpan.FromSeconds(totalStandardTime).ToString(@"hh\:mm\:ss")}</span></div>
<div class='metric-row'><span>Efficiency:</span><span>{(totalStandardTime / totalTime * 100):F1}%</span></div>
</div>
<div class='metric-card'>
<h4>Variation Analysis</h4>
<div class='metric-row'><span>Mean Duration:</span><span>{avgTime:F2}s</span></div>
<div class='metric-row'><span>Std Deviation:</span><span>{stdDev:F2}s</span></div>
<div class='metric-row'><span>Coefficient of Variation:</span><span>{(stdDev / avgTime * 100):F1}%</span></div>
</div>
<div class='metric-card'>
<h4>Time Distribution</h4>
<div class='metric-row'><span>Shortest Activity:</span><span>{minTime:F2}s</span></div>
<div class='metric-row'><span>Longest Activity:</span><span>{maxTime:F2}s</span></div>
<div class='metric-row'><span>Range:</span><span>{(maxTime - minTime):F2}s</span></div>
</div>
</div>
</div>");

        // Data table
        writer.WriteLine(@"<div class='section'>
<div class='section-header'>
<h2>📋 Detailed Observations</h2>
<button class='toggle-btn' onclick='toggleSection(this)'>Collapse</button>
</div>
<div class='collapsible-content'>
<table id='dataTable'>
<thead>
<tr>
<th onclick='sortTable(0)'>#</th>
<th>Thumbnail</th>
<th onclick='sortTable(2)'>Timestamp</th>
<th onclick='sortTable(3)'>Seg</th>
<th onclick='sortTable(4)'>Duration (s)</th>
<th onclick='sortTable(5)'>Element</th>
<th onclick='sortTable(6)'>Description</th>
<th onclick='sortTable(7)'>Observations</th>
<th onclick='sortTable(8)'>People</th>
<th onclick='sortTable(9)'>Std Time (s)</th>
</tr>
</thead>
<tbody>");

        int idx = 1;
        foreach (var entry in timeStudyData)
        {
            double people = double.TryParse(entry.People, out var p) ? p : 1;
            double stdTime = entry.TimeInSeconds * people;
            string peopleClass = people >= 2 ? "rating-good" : people >= 1.5 ? "rating-fair" : "rating-poor";
            var color = !string.IsNullOrWhiteSpace(entry.ElementName) && elementColors.ContainsKey(entry.ElementName) ? elementColors[entry.ElementName] : "#999";
            
            // Convert thumbnail to base64 if available
            string thumbnailHtml = "<td style='text-align:center; color:#888; font-size:10px;'>No thumbnail</td>";
            if (entry.ThumbnailImage != null)
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        // Use full resolution but lower quality to reduce file size
                        var encoder = new JpegBitmapEncoder();
                        encoder.QualityLevel = 40; // 40% quality for smaller file size
                        encoder.Frames.Add(BitmapFrame.Create(entry.ThumbnailImage));
                        encoder.Save(ms);
                        var base64 = Convert.ToBase64String(ms.ToArray());
                        thumbnailHtml = $"<td><img class='thumbnail-img' src='data:image/jpeg;base64,{base64}' alt='Frame at {entry.Timestamp}' onclick='openModal(this.src)' title='Click to enlarge'/></td>";
                    }
                }
                catch (Exception ex) 
                { 
                    thumbnailHtml = $"<td><span style='color:red; font-size:10px;'>Error: {ex.Message}</span></td>"; 
                }
            }
            
            writer.WriteLine($@"<tr>
<td>{idx++}</td>
{thumbnailHtml}
<td>{entry.Timestamp}</td>
<td style='text-align:center; font-weight:bold; color:#4EC9B0;'>{(entry.Segment > 0 ? entry.Segment.ToString() : "-")}</td>
<td>{entry.TimeInSeconds:F2}</td>
<td><span class='element-tag' style='background:{color};'>{System.Security.SecurityElement.Escape(entry.ElementName)}</span></td>
<td>{System.Security.SecurityElement.Escape(entry.Description)}</td>
<td>{System.Security.SecurityElement.Escape(entry.Observations)}</td>
<td class='{peopleClass}'>{entry.People}</td>
<td>{stdTime:F2}</td>
</tr>");
        }

        writer.WriteLine("</tbody></table></div></div>");

        // Export JSON data
        writer.WriteLine("<div class='export-json' id='jsonData'>");
        writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(timeStudyData.Select(e => new {
            e.Timestamp,
            e.Segment,
            e.TimeInSeconds,
            e.ElementName,
            e.Description,
            e.Observations,
            e.People
        })));
        writer.WriteLine("</div>");

        // JavaScript
        writer.WriteLine(@"
<script>
// Image Modal Functions - Define globally
function openModal(imageSrc) {
  const modal = document.getElementById('imageModal');
  const modalImg = document.getElementById('modalImage');
  modal.style.display = 'block';
  modalImg.src = imageSrc;
  document.body.style.overflow = 'hidden';
}

function closeModal() {
  const modal = document.getElementById('imageModal');
  modal.style.display = 'none';
  document.body.style.overflow = 'auto';
}

// Close modal on ESC key
document.addEventListener('keydown', function(event) {
  if (event.key === 'Escape') {
    closeModal();
  }
});");

        writer.WriteLine($@"
// Wait for DOM to be fully loaded
document.addEventListener('DOMContentLoaded', function() {{
console.log('DOM loaded, checking for Chart.js...');
if (typeof Chart === 'undefined') {{
  console.error('Chart.js library not loaded!');
  return;
}}
console.log('Chart.js version:', Chart.version);
console.log('Initializing charts...');
console.log('Labels:', [{chartLabels}]);
console.log('Data:', [{chartData}]);
console.log('Colors:', [{chartColors}]);
console.log('Counts:', [{chartCounts}]);

// Chart.js visualizations
try {{
  const pieCtx = document.getElementById('pieChart');
  if (!pieCtx) {{
    console.error('pieChart canvas not found!');
  }} else {{
    console.log('Creating pie chart...');
    const pieChart = new Chart(pieCtx, {{
    type: 'pie',
    data: {{
      labels: [{chartLabels}],
      datasets: [{{
        data: [{chartData}],
        backgroundColor: [{chartColors}]
      }}]
    }},
    options: {{
      responsive: true,
      maintainAspectRatio: true,
      plugins: {{
        legend: {{ position: 'bottom' }}
      }}
    }}
  }});
  console.log('Pie chart created successfully');
  }}
}} catch(e) {{
  console.error('Error creating pie chart:', e);
}}

try {{
  const barCtx = document.getElementById('barChart');
  if (!barCtx) {{
    console.error('barChart canvas not found!');
  }} else {{
    console.log('Creating bar chart...');
    const barChart = new Chart(barCtx, {{
    type: 'bar',
    data: {{
      labels: [{chartLabels}],
      datasets: [{{
        label: 'Occurrences',
        data: [{chartCounts}],
        backgroundColor: [{chartColors}]
      }}]
    }},
    options: {{
      responsive: true,
      maintainAspectRatio: true,
      plugins: {{
        legend: {{ display: false }}
      }},
      scales: {{
        y: {{ beginAtZero: true }}
      }}
    }}
  }});
  console.log('Bar chart created successfully');
  }}
}} catch(e) {{
  console.error('Error creating bar chart:', e);
}}

// Histogram chart
try {{
  const histCtx = document.getElementById('histogramChart');
  if (histCtx) {{
    console.log('Creating histogram chart...');
    new Chart(histCtx, {{
      type: 'bar',
      data: {{
        labels: [{histogramLabels}],
        datasets: [{{
          label: 'Frequency',
          data: [{histogramData}],
          backgroundColor: '#667eea'
        }}]
      }},
      options: {{
        responsive: true,
        maintainAspectRatio: true,
        plugins: {{ legend: {{ display: false }} }},
        scales: {{ y: {{ beginAtZero: true, title: {{ display: true, text: 'Count' }} }}, x: {{ title: {{ display: true, text: 'Duration Range' }} }} }}
      }}
    }});
    console.log('Histogram created successfully');
  }}
}} catch(e) {{
  console.error('Error creating histogram:', e);
}}

// Cycle time trend chart (line chart showing cycle variations)
try {{
  const cycleCtx = document.getElementById('cycleTimeChart');
  if (cycleCtx) {{
    console.log('Creating cycle time chart...');
    new Chart(cycleCtx, {{
      type: 'line',
      data: {{
        labels: [{chartLabels}],
        datasets: [{{
          label: 'Average Cycle Time',
          data: [{chartData}],
          borderColor: '#667eea',
          backgroundColor: 'rgba(102, 126, 234, 0.1)',
          fill: true,
          tension: 0.4
        }}]
      }},
      options: {{
        responsive: true,
        maintainAspectRatio: true,
        plugins: {{ legend: {{ display: true }} }},
        scales: {{ y: {{ beginAtZero: true, title: {{ display: true, text: 'Time (seconds)' }} }} }}
      }}
    }});
    console.log('Cycle time chart created successfully');
  }}
}} catch(e) {{
  console.error('Error creating cycle time chart:', e);
}}

}}); // End DOMContentLoaded

// Element filtering
function filterElements() {{
  const filterValue = document.getElementById('elementFilter').value;
  const table = document.getElementById('dataTable');
  if (!table || !table.tBodies || !table.tBodies[0]) return;
  
  const rows = table.tBodies[0].rows;
  let visibleCount = 0;
  
  for (let row of rows) {{
    const elementCell = row.cells[5]; // Element column
    const elementText = elementCell ? elementCell.textContent.trim() : '';
    
    if (filterValue === 'all' || elementText !== filterValue) {{
      row.classList.remove('hidden-row');
      visibleCount++;
    }} else {{
      row.classList.add('hidden-row');
    }}
  }}
  
  console.log('Filtered: showing ' + visibleCount + ' of ' + rows.length + ' rows');
}}

// PDF Export function
function exportToPDF() {{
  const element = document.querySelector('.container');
  const opt = {{
    margin: 0.5,
    filename: 'time_study_report.pdf',
    image: {{ type: 'jpeg', quality: 0.98 }},
    html2canvas: {{ scale: 2, useCORS: true }},
    jsPDF: {{ unit: 'in', format: 'letter', orientation: 'portrait' }}
  }};
  
  // Show loading message
  const btn = event.target;
  const originalText = btn.textContent;
  btn.textContent = '⏳ Generating PDF...';
  btn.disabled = true;
  
  html2pdf().set(opt).from(element).save().then(() => {{
    btn.textContent = originalText;
    btn.disabled = false;
  }}).catch(err => {{
    console.error('PDF generation error:', err);
    alert('Error generating PDF: ' + err.message);
    btn.textContent = originalText;
    btn.disabled = false;
  }});
}}

// Table functions
function sortTable(col) {{
  const table = document.getElementById('dataTable');
  const tbody = table.tBodies[0];
  const rows = Array.from(tbody.rows);
  const isAsc = table.dataset.sortCol == col && table.dataset.sortDir == 'asc';
  
  rows.sort((a, b) => {{
    const aVal = a.cells[col].textContent;
    const bVal = b.cells[col].textContent;
    return isAsc ? bVal.localeCompare(aVal, undefined, {{numeric: true}}) : aVal.localeCompare(bVal, undefined, {{numeric: true}});
  }});
  
  rows.forEach(row => tbody.appendChild(row));
  table.dataset.sortCol = col;
  table.dataset.sortDir = isAsc ? 'desc' : 'asc';
}}

function toggleSection(btn) {{
  const content = btn.parentElement.nextElementSibling;
  content.classList.toggle('collapsed');
  btn.textContent = content.classList.contains('collapsed') ? 'Expand' : 'Collapse';
}}

function exportJSON() {{
  const jsonElement = document.getElementById('jsonData');
  if (!jsonElement) {{
    alert('JSON data not found');
    return;
  }}
  
  const jsonData = jsonElement.textContent.trim();
  if (!jsonData) {{
    alert('No data to export');
    return;
  }}
  
  try {{
    const parsed = JSON.parse(jsonData);
    const formatted = JSON.stringify(parsed, null, 2);
    const blob = new Blob([formatted], {{type: 'application/json'}});
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'time_study_data.json';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }} catch(e) {{
    alert('Error exporting JSON: ' + e.message);
    console.error('JSON export error:', e);
  }}
}}

function exportToCSV() {{
  let csv = [];
  
  // Add BOM for Excel UTF-8 support
  const BOM = '\uFEFF';
  
  // Add headers
  csv.push('Timestamp,Duration (s),Element,Description,Observations,Rating,Standard Time (s)');
  
  // Add data rows from table body only (skip header)
  const table = document.getElementById('dataTable');
  if (!table || !table.tBodies || !table.tBodies[0]) {{
    alert('No data table found');
    return;
  }}
  
  const rows = table.tBodies[0].rows;
  
  for (let row of rows) {{
    let rowData = [];
    // Skip first column (#) and thumbnail column (last), get middle data columns
    const cellCount = row.cells.length;
    for (let i = 1; i < cellCount - 1; i++) {{
      let cellText = row.cells[i].textContent.trim();
      // Remove newlines and escape quotes properly
      cellText = cellText.replace(/[\n\r]+/g, ' ').trim();
      cellText = cellText.replace(/""/g, '""""');
      rowData.push('""' + cellText + '""');
    }}
    csv.push(rowData.join(','));
  }}
  
  const blob = new Blob([BOM + csv.join('\r\n')], {{type: 'text/csv;charset=utf-8;'}});
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'time_study_export.csv';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}}
</script>");

        writer.WriteLine($@"<div class='footer'>
<strong>Video Time Study Application</strong><br>
Generated on {DateTime.Now:yyyy-MM-dd} at {DateTime.Now:HH:mm:ss}<br>
Total Analysis Time: {TimeSpan.FromSeconds(totalTime).ToString(@"hh\:mm\:ss")} | {timeStudyData.Count} Observations
</div>
</div></div>

<!-- Image Modal -->
<div id='imageModal' class='modal' onclick='closeModal()'>
  <span class='modal-close' onclick='closeModal()'>&times;</span>
  <img class='modal-content' id='modalImage' onclick='event.stopPropagation();'>
</div>

</body></html>");
    }

    private void WriteHtmlHeader(StreamWriter writer)
    {
        writer.WriteLine(@"<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<title>Time Study Report - Professional Analysis</title>
<!-- Core Libraries -->
<script src='https://code.jquery.com/jquery-3.7.0.min.js'></script>
<script src='https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js'></script>
<script src='https://cdnjs.cloudflare.com/ajax/libs/html2pdf.js/0.10.1/html2pdf.bundle.min.js'></script>
<!-- DataTables for Excel-like functionality -->
<link rel='stylesheet' href='https://cdn.datatables.net/1.13.7/css/jquery.dataTables.min.css'/>
<link rel='stylesheet' href='https://cdn.datatables.net/buttons/2.4.2/css/buttons.dataTables.min.css'/>
<link rel='stylesheet' href='https://cdn.datatables.net/searchpanes/2.2.0/css/searchPanes.dataTables.min.css'/>
<link rel='stylesheet' href='https://cdn.datatables.net/select/1.7.0/css/select.dataTables.min.css'/>
<script src='https://cdn.datatables.net/1.13.7/js/jquery.dataTables.min.js'></script>
<script src='https://cdn.datatables.net/buttons/2.4.2/js/dataTables.buttons.min.js'></script>
<script src='https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js'></script>
<script src='https://cdn.datatables.net/buttons/2.4.2/js/buttons.html5.min.js'></script>
<script src='https://cdn.datatables.net/buttons/2.4.2/js/buttons.print.min.js'></script>
<script src='https://cdn.datatables.net/searchpanes/2.2.0/js/dataTables.searchPanes.min.js'></script>
<script src='https://cdn.datatables.net/select/1.7.0/js/dataTables.select.min.js'></script>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #f0f2f5; color: #333; }
.container { max-width: 1600px; margin: 20px auto; background: white; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.08); margin-bottom: 30px; overflow: hidden; }
.segment-section { margin-bottom: 40px; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }
.header { background: linear-gradient(135deg, #1e3a8a 0%, #3b82f6 100%); color: white; padding: 30px; }
.header h1 { margin: 0; font-size: 32px; font-weight: 600; }
.header .subtitle { opacity: 0.95; margin-top: 8px; font-size: 16px; }
.segment-header { background: linear-gradient(135deg, #1e3a8a 0%, #3b82f6 100%); color: white; padding: 20px 30px; border-bottom: 3px solid #2563eb; }
.segment-header h2 { margin: 0; font-size: 24px; font-weight: 600; display: flex; align-items: center; gap: 10px; }
.content { padding: 30px; background: #fafafa; }
.control-panel { background: white; padding: 20px; margin-bottom: 25px; border-radius: 8px; border: 1px solid #e0e0e0; box-shadow: 0 2px 4px rgba(0,0,0,0.05); }
.control-row { display: flex; gap: 15px; margin-bottom: 15px; align-items: center; flex-wrap: wrap; }
.control-row:last-child { margin-bottom: 0; }
.control-group { display: flex; align-items: center; gap: 8px; }
.control-label { font-weight: 600; color: #555; font-size: 14px; white-space: nowrap; }
.toolbar { background: #f8f9fa; padding: 15px; border-radius: 8px; margin-bottom: 20px; display: flex; gap: 10px; flex-wrap: wrap; align-items: center; border: 1px solid #e0e0e0; }
.btn { padding: 10px 18px; border: none; border-radius: 6px; cursor: pointer; font-weight: 600; font-size: 14px; transition: all 0.2s; display: inline-flex; align-items: center; gap: 6px; }
.btn:disabled { opacity: 0.6; cursor: not-allowed; }
.btn-primary { background: #3b82f6; color: white; }
.btn-primary:hover:not(:disabled) { background: #2563eb; box-shadow: 0 4px 12px rgba(59,130,246,0.3); }
.btn-secondary { background: #6b7280; color: white; }
.btn-secondary:hover:not(:disabled) { background: #4b5563; }
.btn-outline { background: white; border: 2px solid #3b82f6; color: #3b82f6; }
.btn-outline:hover:not(:disabled) { background: #eff6ff; }
.btn-sm { padding: 6px 12px; font-size: 13px; }
.btn-success { background: #10b981; color: white; }
.btn-success:hover:not(:disabled) { background: #059669; box-shadow: 0 4px 12px rgba(16,185,129,0.3); }
.btn-warning { background: #f59e0b; color: white; }
.btn-warning:hover:not(:disabled) { background: #d97706; }
.btn-danger { background: #ef4444; color: white; }
.btn-danger:hover:not(:disabled) { background: #dc2626; }
.btn-icon { padding: 8px; }
.info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 15px; margin: 20px 0; }
.info-card { background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%); padding: 15px; border-radius: 10px; border-left: 4px solid #667eea; }
.stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 20px; margin: 30px 0; }
.stat-card { background: white; padding: 25px; border-radius: 12px; text-align: center; box-shadow: 0 4px 15px rgba(0,0,0,0.1); transition: transform 0.3s; }
.stat-card:hover { transform: translateY(-5px); box-shadow: 0 6px 20px rgba(0,0,0,0.15); }
.stat-value { font-size: 36px; font-weight: bold; color: #667eea; margin: 10px 0; }
.stat-label { color: #6c757d; font-size: 14px; text-transform: uppercase; letter-spacing: 1px; }
.section { margin: 40px 0; }
.section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
.section-header h2 { color: #333; margin: 0; }
.toggle-btn { background: none; border: 2px solid #667eea; color: #667eea; padding: 8px 16px; border-radius: 6px; cursor: pointer; font-weight: 600; }
.toggle-btn:hover { background: #667eea; color: white; }
.collapsible-content { max-height: 2000px; overflow: hidden; transition: max-height 0.3s ease-in-out; }
.collapsible-content.collapsed { max-height: 0; }
.chart-container { display: grid; grid-template-columns: 1fr 1fr; gap: 30px; margin: 30px 0; }
.chart-box { background: white; padding: 20px; border-radius: 12px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
.chart-box h3 { margin-top: 0; color: #333; text-align: center; }
canvas { max-height: 400px; width: 100% !important; height: auto !important; }
.thumbnail-img { width: 80px; height: 60px; object-fit: cover; border-radius: 4px; border: 2px solid #ddd; cursor: pointer; transition: transform 0.3s, box-shadow 0.3s; }
.thumbnail-img:hover { transform: scale(1.5); box-shadow: 0 8px 20px rgba(0,0,0,0.3); z-index: 100; position: relative; }
.modal { display: none; position: fixed; z-index: 1000; left: 0; top: 0; width: 100%; height: 100%; background-color: rgba(0,0,0,0.9); }
.modal-content { margin: auto; display: block; max-width: 90%; max-height: 90%; position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); animation: zoom 0.3s; }
@keyframes zoom { from {transform: translate(-50%, -50%) scale(0);} to {transform: translate(-50%, -50%) scale(1);} }
.modal-close { position: absolute; top: 30px; right: 45px; color: #f1f1f1; font-size: 50px; font-weight: bold; cursor: pointer; transition: 0.3s; }
.modal-close:hover { color: #bbb; }
.element-bars { margin: 20px 0; }
.element-bar-item { margin: 15px 0; }
.element-bar-label { display: flex; justify-content: space-between; margin-bottom: 5px; font-weight: 600; }
.element-bar-bg { background: #e9ecef; height: 30px; border-radius: 15px; overflow: hidden; position: relative; }
.element-bar-fill { height: 100%; border-radius: 15px; display: flex; align-items: center; padding: 0 15px; color: white; font-weight: bold; transition: width 0.5s ease-out; }
table { width: 100%; border-collapse: collapse; margin: 20px 0; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
th { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 15px; text-align: left; font-weight: 600; position: sticky; top: 0; cursor: pointer; user-select: none; }
th:hover { background: linear-gradient(135deg, #5568d3 0%, #653a8c 100%); }
td { padding: 12px 15px; border-bottom: 1px solid #e9ecef; }
tr:hover { background: #f8f9fa; }
.rating-good { color: #28a745; font-weight: bold; }
.rating-fair { color: #ffc107; font-weight: bold; }
.rating-poor { color: #dc3545; font-weight: bold; }
.element-tag { display: inline-block; padding: 4px 12px; border-radius: 12px; font-size: 12px; font-weight: 600; color: white; }
.search-box { padding: 10px 15px; border: 2px solid #e9ecef; border-radius: 8px; width: 300px; font-size: 14px; }
.search-box:focus { outline: none; border-color: #667eea; }
.metrics-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; margin: 20px 0; }
.metric-card { background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
.metric-card h4 { margin-top: 0; color: #667eea; }
.metric-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #e9ecef; }
.timeline-viz { margin: 30px 0; }
.timeline-bar { background: #f8f9fa; height: 60px; border-radius: 8px; position: relative; overflow: hidden; box-shadow: inset 0 2px 4px rgba(0,0,0,0.1); }
.timeline-segment { position: absolute; height: 100%; display: flex; align-items: center; justify-content: center; color: white; font-size: 11px; font-weight: bold; border-right: 2px solid white; overflow: hidden; transition: all 0.3s; }
.timeline-segment:hover { opacity: 0.8; z-index: 10; }
.hidden-row { display: none !important; }
.filter-section { display: flex; gap: 10px; align-items: center; }
.filter-label { font-weight: 600; color: #333; }
#elementFilter { padding: 8px 12px; border: 2px solid #e9ecef; border-radius: 6px; min-width: 200px; }
/* DataTables Excel-like styling */
.dataTables_wrapper { font-size: 14px; margin-top: 15px; }
.dataTables_wrapper .dataTables_length, .dataTables_wrapper .dataTables_filter { margin-bottom: 15px; }
.dataTables_wrapper .dataTables_length select { padding: 6px 10px; border: 1px solid #d1d5db; border-radius: 6px; background: white; }
.dataTables_wrapper .dataTables_filter input { padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 6px; margin-left: 8px; width: 300px; }
.dataTables_wrapper .dataTables_filter input:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
table.dataTable { width: 100% !important; border-collapse: collapse; background: white; border: 1px solid #e0e0e0; margin-top: 0 !important; }
table.dataTable thead th { background: linear-gradient(135deg, #1e3a8a 0%, #3b82f6 100%) !important; color: white !important; padding: 12px !important; text-align: left !important; font-weight: 600 !important; font-size: 13px !important; border-bottom: 3px solid #2563eb !important; white-space: nowrap !important; cursor: pointer !important; }
table.dataTable thead th:hover { background: linear-gradient(135deg, #1e40af 0%, #2563eb 100%) !important; }
table.dataTable tbody td { padding: 10px 12px !important; border-bottom: 1px solid #f0f0f0 !important; font-size: 13px !important; }
table.dataTable tbody tr:hover { background: #f8fafc !important; }
table.dataTable tbody tr.selected { background: #dbeafe !important; }
.dataTables_wrapper .dataTables_paginate { margin-top: 15px; }
.dataTables_wrapper .dataTables_paginate .paginate_button { padding: 6px 12px; margin: 0 2px; border-radius: 4px; border: 1px solid #d1d5db; background: white; cursor: pointer; }
.dataTables_wrapper .dataTables_paginate .paginate_button:hover { background: #3b82f6; color: white !important; border-color: #3b82f6; }
.dataTables_wrapper .dataTables_paginate .paginate_button.current { background: #3b82f6; color: white !important; border-color: #3b82f6; }
.dt-buttons { display: flex; gap: 8px; margin-bottom: 15px; flex-wrap: wrap; }
.dt-button { padding: 8px 16px !important; border: 2px solid #3b82f6 !important; background: white !important; color: #3b82f6 !important; border-radius: 6px !important; font-weight: 600 !important; font-size: 13px !important; transition: all 0.2s !important; cursor: pointer !important; }
.dt-button:hover { background: #3b82f6 !important; color: white !important; }
/* Chart toggle controls */
.chart-toggle-bar { display: flex; gap: 10px; margin: 20px 0; padding: 15px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; flex-wrap: wrap; align-items: center; }
.chart-toggle { padding: 8px 16px; border: 2px solid #e0e0e0; background: white; border-radius: 6px; cursor: pointer; font-size: 14px; font-weight: 500; transition: all 0.2s; user-select: none; display: inline-flex; align-items: center; gap: 6px; }
.chart-toggle:hover { border-color: #3b82f6; background: #eff6ff; }
.chart-toggle.active { background: #3b82f6; color: white; border-color: #3b82f6; }
.chart-toggle::before { content: '☐'; font-size: 16px; }
.chart-toggle.active::before { content: '☑'; }
.chart-box.hidden { display: none !important; }
.footer { margin-top: 50px; padding: 20px; text-align: center; color: #6c757d; border-top: 2px solid #e9ecef; }
@media print {
  body { background: white; padding: 0; }
  .toolbar, .toggle-btn, .chart-container, .modal { display: none; }
  .container { box-shadow: none; page-break-after: always; }
}
</style>
</head>
<body>
<div class='header' style='text-align: center; margin-bottom: 30px; border-radius: 15px;'>
<h1>📊 Time Study Analysis Report</h1>
<div class='subtitle'>Generated on " + DateTime.Now.ToString("yyyy-MM-dd") + @" at " + DateTime.Now.ToString("HH:mm:ss") + @"</div>
</div>

<!-- Image Modal -->
<div id='imageModal' class='modal' onclick='closeModal()'>
  <span class='modal-close' onclick='closeModal()'>&times;</span>
  <img class='modal-content' id='modalImage' onclick='event.stopPropagation();'>
</div>
");
    }

    private void WriteHtmlFooter(StreamWriter writer)
    {
        writer.WriteLine("</body></html>");
    }

    private void WriteSegmentReport(StreamWriter writer, int segmentNumber, List<TimeStudyEntry> segmentData,
        List<dynamic> elementStats, Dictionary<string, string> elementColors,
        double totalTime, double avgTime, double minTime, double maxTime, double stdDev, double totalStandardTime)
    {
        // Generate unique IDs for this segment's charts
        string pieChartId = $"pieChart_seg{segmentNumber}";
        string barChartId = $"barChart_seg{segmentNumber}";
        string dataTableId = $"dataTable_seg{segmentNumber}";
        string jsonDataId = $"jsonData_seg{segmentNumber}";
        
        // Pre-format the time to avoid format string issues
        string formattedTime = TimeSpan.FromSeconds(totalTime).ToString(@"hh\:mm\:ss");
        
        writer.WriteLine($@"
<div class='container segment-section'>
<div class='segment-header'>
<h2>Segment {segmentNumber}</h2>
<div class='subtitle'>{segmentData.Count} observations | Total Time: {formattedTime}</div>
</div>
<div class='content'>");

        // Continue with rest of segment report (stats, charts, table)...
        WriteSegmentContent(writer, segmentNumber, segmentData, elementStats, elementColors,
            totalTime, avgTime, minTime, maxTime, stdDev, totalStandardTime,
            pieChartId, barChartId, dataTableId, jsonDataId);
        
        writer.WriteLine("</div></div>");
    }

    private void WriteSegmentContent(StreamWriter writer, int segmentNumber, List<TimeStudyEntry> segmentData,
        List<dynamic> elementStats, Dictionary<string, string> elementColors,
        double totalTime, double avgTime, double minTime, double maxTime, double stdDev, double totalStandardTime,
        string pieChartId, string barChartId, string dataTableId, string jsonDataId)
    {
        // Generate chart data
        var labels = new List<string>();
        var data = new List<string>();
        var colors = new List<string>();
        var counts = new List<string>();
        
        if (elementStats.Any())
        {
            foreach (var stat in elementStats)
            {
                labels.Add($"'{System.Security.SecurityElement.Escape(stat.Element)}'");
                data.Add(stat.TotalTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                colors.Add($"'{elementColors[stat.Element]}'");
                counts.Add(stat.Count.ToString());
            }
        }
        else
        {
            labels.Add("'No Data'");
            data.Add("0");
            colors.Add("'#cccccc'");
            counts.Add("0");
        }
        
        var chartLabels = string.Join(",", labels);
        var chartData = string.Join(",", data);
        var chartColors = string.Join(",", colors);
        var chartCounts = string.Join(",", counts);

        // Calculate histogram data (duration buckets)
        var durations = segmentData.Where(e => e.TimeInSeconds > 0).Select(e => e.TimeInSeconds).OrderBy(d => d).ToList();
        var histogramBuckets = new List<string>();
        var histogramCounts = new List<int>();
        
        if (durations.Any())
        {
            double minDur = durations.Min();
            double maxDur = durations.Max();
            double bucketSize = (maxDur - minDur) / 10;
            if (bucketSize < 0.1) bucketSize = 0.1;
            
            for (int i = 0; i < 10; i++)
            {
                double bucketStart = minDur + (i * bucketSize);
                double bucketEnd = bucketStart + bucketSize;
                histogramBuckets.Add($"'{bucketStart:F1}-{bucketEnd:F1}s'");
                histogramCounts.Add(durations.Count(d => d >= bucketStart && d < bucketEnd));
            }
        }
        
        var histogramLabels = string.Join(",", histogramBuckets);
        var histogramData = string.Join(",", histogramCounts);

        // Calculate cycle time data (for repeating elements)
        var cycleTimeData = new Dictionary<string, List<double>>();
        foreach (var stat in elementStats)
        {
            var elementDurations = segmentData
                .Where(e => e.ElementName == stat.Element && e.TimeInSeconds > 0)
                .Select(e => e.TimeInSeconds)
                .ToList();
            
            if (elementDurations.Count > 1)
            {
                cycleTimeData[stat.Element] = elementDurations;
            }
        }

        // Key metrics
        writer.WriteLine($@"
<div class='section'>
<h2>📈 Key Metrics</h2>
<div class='stats-grid'>
<div class='stat-card'>
<div class='stat-label'>Total Time</div>
<div class='stat-value'>{TimeSpan.FromSeconds(totalTime):hh\:mm\:ss}</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Average Duration</div>
<div class='stat-value'>{avgTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Std Deviation</div>
<div class='stat-value'>{stdDev:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Min Duration</div>
<div class='stat-value'>{minTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Max Duration</div>
<div class='stat-value'>{maxTime:F2}s</div>
</div>
<div class='stat-card'>
<div class='stat-label'>Standard Time</div>
<div class='stat-value'>{TimeSpan.FromSeconds(totalStandardTime):hh\:mm\:ss}</div>
</div>
</div>
</div>");

        // Charts
        writer.WriteLine($@"
<div class='section'>
<div class='section-header'>
<h2>📊 Visual Analysis</h2>
</div>
<div class='chart-toggle-bar'>
<span style='font-weight: 600; color: #555; margin-right: 10px;'>Show Charts:</span>
<div class='chart-toggle active' data-chart='pie_{segmentNumber}'>Pie Chart</div>
<div class='chart-toggle active' data-chart='bar_{segmentNumber}'>Bar Chart</div>
<div class='chart-toggle active' data-chart='histogram_{segmentNumber}'>Histogram</div>
<div class='chart-toggle active' data-chart='cycle_{segmentNumber}'>Cycle Analysis</div>
</div>
<div class='chart-container'>
<div class='chart-box visible' id='chart_pie_{segmentNumber}'>
<h3>📈 Time Distribution by Element</h3>
<canvas id='{pieChartId}'></canvas>
</div>
<div class='chart-box visible' id='chart_bar_{segmentNumber}'>
<h3>📊 Element Frequency</h3>
<canvas id='{barChartId}'></canvas>
</div>
<div class='chart-box visible' id='chart_histogram_{segmentNumber}'>
<h3>📉 Duration Histogram</h3>
<canvas id='histogramChart_{segmentNumber}'></canvas>
</div>
<div class='chart-box visible' id='chart_cycle_{segmentNumber}'>
<h3>🔄 Cycle Time Analysis</h3>
<div id='cycleTimeAnalysis_{segmentNumber}' style='padding: 20px;'>");

        if (cycleTimeData.Any())
        {
            writer.WriteLine("<table style='width:100%; font-size:14px; border-collapse: collapse;'><thead><tr style='background: linear-gradient(135deg, #1e3a8a 0%, #3b82f6 100%); color: white;'><th style='padding: 10px;'>Element</th><th style='padding: 10px;'>Count</th><th style='padding: 10px;'>Avg</th><th style='padding: 10px;'>Min</th><th style='padding: 10px;'>Max</th><th style='padding: 10px;'>CV%</th></tr></thead><tbody>");
            foreach (var kvp in cycleTimeData.OrderByDescending(x => x.Value.Count))
            {
                var times = kvp.Value;
                double avg = times.Average();
                double stddev = Math.Sqrt(times.Average(v => Math.Pow(v - avg, 2)));
                double cv = (stddev / avg) * 100;
                writer.WriteLine($"<tr style='border-bottom: 1px solid #e0e0e0;'><td style='padding: 8px;'>{System.Security.SecurityElement.Escape(kvp.Key)}</td><td style='padding: 8px;'>{times.Count}</td><td style='padding: 8px;'>{avg:F2}s</td><td style='padding: 8px;'>{times.Min():F2}s</td><td style='padding: 8px;'>{times.Max():F2}s</td><td style='padding: 8px; font-weight: bold; color: {(cv < 10 ? "#10b981" : cv < 20 ? "#f59e0b" : "#ef4444")};'>{cv:F1}%</td></tr>");
            }
            writer.WriteLine("</tbody></table>");
        }
        else
        {
            writer.WriteLine("<p style='text-align:center; color:#999; padding: 40px;'>No repeating elements for cycle analysis</p>");
        }

        writer.WriteLine(@"</div>
</div>
</div>
</div>");

        // Element breakdown
        writer.WriteLine("<div class='section'><h2>🔍 Element Breakdown</h2><div class='element-bars'>");
        if (elementStats.Any())
        {
            foreach (var stat in elementStats)
            {
                double percentage = (stat.TotalTime / totalTime) * 100;
                var color = elementColors[stat.Element];
                writer.WriteLine($@"<div class='element-bar-item'>
<div class='element-bar-label'>
<span><span class='element-tag' style='background:{color};'>{System.Security.SecurityElement.Escape(stat.Element)}</span> - {stat.Count}x occurrences</span>
<span>Avg: {stat.AvgTime:F2}s | σ: {stat.StdDev:F2}s</span>
<span>{stat.TotalTime:F2}s ({percentage:F1}%)</span>
</div>
<div class='element-bar-bg'>
<div class='element-bar-fill' style='width:{Math.Min(percentage, 100):F1}%; background:{color};'>&nbsp;</div>
</div>
</div>");
            }
        }
        else
        {
            writer.WriteLine("<p>No element data available.</p>");
        }
        writer.WriteLine("</div></div>");

        // Data table
        writer.WriteLine($@"
<div class='section'>
<div class='section-header'>
<h2>📋 Detailed Observations</h2>
</div>
<div style='background: white; padding: 15px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); margin-bottom: 15px;'>
<div style='font-size: 12px; color: #6b7280;'>
💡 <strong>Excel-like Features:</strong> Click column headers to sort • Use search box to filter • Export to Excel/CSV/PDF • Click thumbnails to enlarge
</div>
</div>
<table id='{dataTableId}' class='display' style='width:100%'>
<thead>
<tr>
<th>#</th>
<th>Thumbnail</th>
<th>Timestamp</th>
<th>Duration (s)</th>
<th>Element</th>
<th>Description</th>
<th>Observations</th>
<th>Rating</th>
<th>Std Time (s)</th>
</tr>
</thead>
<tbody>");

        int idx = 1;
        foreach (var entry in segmentData)
        {
            double people = double.TryParse(entry.People, out var p) ? p : 1;
            double stdTime = entry.TimeInSeconds * people;
            string peopleClass = people >= 2 ? "rating-good" : people >= 1.5 ? "rating-fair" : "rating-poor";
            var color = !string.IsNullOrWhiteSpace(entry.ElementName) && elementColors.ContainsKey(entry.ElementName) ? elementColors[entry.ElementName] : "#999";
            
            string thumbnailHtml = "<td style='text-align:center; color:#888; font-size:10px;'>No thumbnail</td>";
            if (entry.ThumbnailImage != null)
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        // Use full resolution but lower quality to reduce file size
                        var encoder = new JpegBitmapEncoder();
                        encoder.QualityLevel = 40; // 40% quality for smaller file size
                        encoder.Frames.Add(BitmapFrame.Create(entry.ThumbnailImage));
                        encoder.Save(ms);
                        var base64 = Convert.ToBase64String(ms.ToArray());
                        thumbnailHtml = $"<td><img class='thumbnail-img' src='data:image/jpeg;base64,{base64}' alt='Frame at {entry.Timestamp}' onclick='openModal(this.src)' title='Click to enlarge'/></td>";
                    }
                }
                catch (Exception ex) 
                { 
                    thumbnailHtml = $"<td><span style='color:red; font-size:10px;'>Error: {ex.Message}</span></td>"; 
                }
            }
            
            writer.WriteLine($@"<tr>
<td>{idx++}</td>
{thumbnailHtml}
<td>{entry.Timestamp}</td>
<td>{entry.TimeInSeconds:F2}</td>
<td><span class='element-tag' style='background:{color};'>{System.Security.SecurityElement.Escape(entry.ElementName)}</span></td>
<td>{System.Security.SecurityElement.Escape(entry.Description)}</td>
<td>{System.Security.SecurityElement.Escape(entry.Observations)}</td>
<td class='{peopleClass}'>{entry.People}</td>
<td>{stdTime:F2}</td>
</tr>");
        }

        writer.WriteLine("</tbody></table></div>");

        // Add chart initialization script for this segment
        writer.WriteLine($@"
<script>
document.addEventListener('DOMContentLoaded', function() {{
  const pieCtx = document.getElementById('{pieChartId}');
  if (pieCtx) {{
    new Chart(pieCtx, {{
      type: 'pie',
      data: {{
        labels: [{chartLabels}],
        datasets: [{{
          data: [{chartData}],
          backgroundColor: [{chartColors}],
          borderWidth: 2,
          borderColor: '#fff'
        }}]
      }},
      options: {{
        responsive: true,
        maintainAspectRatio: true,
        plugins: {{
          legend: {{ position: 'bottom' }},
          tooltip: {{
            callbacks: {{
              label: function(context) {{
                return context.label + ': ' + context.parsed.toFixed(2) + 's';
              }}
            }}
          }}
        }}
      }}
    }});
  }}

  const barCtx = document.getElementById('{barChartId}');
  if (barCtx) {{
    new Chart(barCtx, {{
      type: 'bar',
      data: {{
        labels: [{chartLabels}],
        datasets: [{{
          label: 'Occurrences',
          data: [{chartCounts}],
          backgroundColor: [{chartColors}],
          borderWidth: 2,
          borderColor: '#fff'
        }}]
      }},
      options: {{
        responsive: true,
        maintainAspectRatio: true,
        plugins: {{
          legend: {{ display: false }}
        }},
        scales: {{
          y: {{ beginAtZero: true, ticks: {{ stepSize: 1 }} }}
        }}
      }}
    }});
  }}

  // Histogram chart
  const histCtx = document.getElementById('histogramChart_{segmentNumber}');
  if (histCtx) {{
    new Chart(histCtx, {{
      type: 'bar',
      data: {{
        labels: [{histogramLabels}],
        datasets: [{{
          label: 'Frequency',
          data: [{histogramData}],
          backgroundColor: '#667eea',
          borderWidth: 2,
          borderColor: '#fff'
        }}]
      }},
      options: {{
        responsive: true,
        maintainAspectRatio: true,
        plugins: {{ legend: {{ display: false }} }},
        scales: {{ 
          y: {{ beginAtZero: true, ticks: {{ stepSize: 1 }} }},
          x: {{ title: {{ display: true, text: 'Duration Range' }} }}
        }}
      }}
    }});
  }}

  // Initialize DataTables with Excel-like features
  $('#{dataTableId}').DataTable({{
    dom: '<""top""Bf>rt<""bottom""lip><""clear"">',
    buttons: [
      {{
        extend: 'copy',
        className: 'btn btn-primary btn-sm',
        text: '\ud83d\udccb Copy'
      }},
      {{
        extend: 'excel',
        className: 'btn btn-success btn-sm',
        text: '\ud83d\udcca Excel',
        title: 'Time_Study_Segment_{segmentNumber}'
      }},
      {{
        extend: 'csv',
        className: 'btn btn-primary btn-sm',
        text: '\ud83d\udcc4 CSV'
      }},
      {{
        extend: 'pdf',
        className: 'btn btn-danger btn-sm',
        text: '\ud83d\udcd5 PDF',
        title: 'Time Study Segment {segmentNumber}',
        orientation: 'landscape',
        pageSize: 'LEGAL'
      }},
      {{
        extend: 'print',
        className: 'btn btn-outline btn-sm',
        text: '\ud83d\udda8\ufe0f Print'
      }}
    ],
    pageLength: 25,
    lengthMenu: [[10, 25, 50, 100, -1], [10, 25, 50, 100, 'All']],
    order: [[2, 'asc']], // Sort by timestamp
    columnDefs: [
      {{ orderable: false, targets: 1 }}, // Disable sorting on thumbnail column
      {{ className: 'dt-center', targets: [0, 1, 3, 7, 8] }}, // Center align specific columns
    ],
    language: {{
      search: '\ud83d\udd0d Search:',
      lengthMenu: 'Show _MENU_ entries',
      info: 'Showing _START_ to _END_ of _TOTAL_ observations',
      infoEmpty: 'No observations available',
      infoFiltered: '(filtered from _MAX_ total)',
      paginate: {{
        first: '\u23ee\ufe0f',
        previous: '\u25c0\ufe0f',
        next: '\u25b6\ufe0f',
        last: '\u23ed\ufe0f'
      }}
    }}
  }});

  // Chart toggle functionality
  $('.chart-toggle').click(function() {{
    $(this).toggleClass('active');
    const chartId = $(this).data('chart');
    const chartDiv = $('#chart_' + chartId + '_{segmentNumber}');
    chartDiv.toggleClass('hidden');
    
    // Save preference to localStorage
    const storageKey = 'chart_' + chartId + '_{segmentNumber}_visible';
    localStorage.setItem(storageKey, chartDiv.hasClass('hidden') ? 'false' : 'true');
  }});

  // Restore chart visibility from localStorage
  $('.chart-toggle').each(function() {{
    const chartId = $(this).data('chart');
    const storageKey = 'chart_' + chartId + '_{segmentNumber}_visible';
    const isVisible = localStorage.getItem(storageKey);
    
    if (isVisible === 'false') {{
      $(this).removeClass('active');
      $('#chart_' + chartId + '_{segmentNumber}').addClass('hidden');
    }}
  }});
}});

// Element filtering is handled by DataTables search functionality

// Image modal functions
function openModal(imageSrc) {{
  const modal = document.getElementById('imageModal');
  const modalImg = document.getElementById('modalImage');
  modal.style.display = 'block';
  modalImg.src = imageSrc;
  document.body.style.overflow = 'hidden';
}}

function closeModal() {{
  const modal = document.getElementById('imageModal');
  modal.style.display = 'none';
  document.body.style.overflow = 'auto';
}}

document.addEventListener('keydown', function(event) {{
  if (event.key === 'Escape') {{
    closeModal();
  }}
}});
</script>");
    }

    // Helper method to assign consistent colors to elements
    private Dictionary<string, string> AssignElementColors(List<string> elements)
    {
        var colors = new[] { "#FF6B6B", "#4ECDC4", "#45B7D1", "#FFA07A", "#98D8C8", "#F7DC6F", 
                            "#BB8FCE", "#85C1E2", "#F8B739", "#52B788", "#E76F51", "#2A9D8F" };
        var colorDict = new Dictionary<string, string>();
        for (int i = 0; i < elements.Count; i++)
        {
            colorDict[elements[i]] = colors[i % colors.Length];
        }
        return colorDict;
    }

    private void TimelineZoomIn_Click(object sender, RoutedEventArgs e)
    {
        timelineZoom = Math.Min(timelineZoom * 1.5, 10.0);
        ApplyTimelineZoom();
    }

    private void TimelineScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.None)
        {
            // Get mouse position relative to the timeline scrollviewer viewport
            Point mousePos = e.GetPosition(TimelineScrollViewer);
            
            // Calculate the time position at cursor BEFORE zoom
            double currentScrollOffset = TimelineScrollViewer.HorizontalOffset;
            double mouseXInCanvas = currentScrollOffset + mousePos.X;
            double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
            double timeAtCursor = mouseXInCanvas / effectivePixelsPerSecond;
            
            // Calculate zoom change
            double zoomChange = e.Delta > 0 ? 1.25 : 0.8;
            double newZoom = Math.Max(0.01, Math.Min(10.0, timelineZoom * zoomChange));
            
            if (newZoom != timelineZoom)
            {
                // Update zoom and redraw everything
                timelineZoom = newZoom;
                ApplyTimelineZoom();
                
                // Calculate new scroll position to keep time-at-cursor in same screen position
                double newEffectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
                double newMouseXInCanvas = timeAtCursor * newEffectivePixelsPerSecond;
                double newScrollOffset = newMouseXInCanvas - mousePos.X;
                
                // Clamp scroll offset to valid range
                newScrollOffset = Math.Max(0, Math.Min(newScrollOffset, TimelineScrollViewer.ScrollableWidth));
                
                // Update both scrollviewers to maintain sync
                TimelineScrollViewer.ScrollToHorizontalOffset(newScrollOffset);
                SegmentTracksScroll.ScrollToHorizontalOffset(newScrollOffset);
            }
            
            e.Handled = true;
        }
    }

    private void TimelineZoomOut_Click(object sender, RoutedEventArgs e)
    {
        // Calculate minimum zoom to fit entire video in viewport
        double minZoom = 0.01; // Absolute minimum
        
        if (HasVideo)
        {
            double totalSeconds = VideoDurationSeconds;
            // Use a reasonable minimum width (e.g., 1000 pixels for entire timeline)
            double minWidth = 800.0;
            minZoom = Math.Max(0.01, minWidth / (totalSeconds * timelinePixelsPerSecond));
        }
        
        timelineZoom = Math.Max(timelineZoom / 1.5, minZoom);
        ApplyTimelineZoom();
    }

    private void TimelineZoomReset_Click(object sender, RoutedEventArgs e)
    {
        timelineZoom = 1.0;
        ApplyTimelineZoom();
    }

    private void TimelineZoomFit_Click(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            double totalSeconds = VideoDurationSeconds;
            // Get the actual scrollviewer width from the timeline scrollviewer
            if (SegmentTracksScroll != null)
            {
                double availableWidth = SegmentTracksScroll.ViewportWidth - 100; // Account for track labels
                timelineZoom = Math.Max(0.01, availableWidth / (totalSeconds * timelinePixelsPerSecond));
                ApplyTimelineZoom();
            }
        }
    }

    private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        
        T? parent = parentObject as T;
        return parent ?? FindVisualParent<T>(parentObject);
    }

    private void ApplyTimelineZoom()
    {
        DrawTimeRuler();
        UpdateTimeline();
        TimelineZoomLabel.Text = $"{timelineZoom * 100:F0}%";
        StatusText.Text = $"Timeline Zoom: {timelineZoom * 100:F0}%";
        
        // Force layout update and sync scrolling
        TimelineScrollViewer.UpdateLayout();
        SegmentTracksScroll.UpdateLayout();
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!HasVideo)
            return;
        
        // Check for double-click
        DateTime now = DateTime.Now;
        bool isDoubleClick = (now - lastTimelineClickTime).TotalMilliseconds < DoubleClickMilliseconds;
        lastTimelineClickTime = now;
        
        if (isDoubleClick)
        {
            // Toggle play/pause on double-click
            if (timer.IsEnabled)
            {
                VideoPlayer.Pause();
                timer.Stop();
                StatusText.Text = "Paused";
            }
            else
            {
                VideoPlayer.Play();
                VideoPlayer.SpeedRatio = currentSpeedRatio;
                timer.Start();
                StatusText.Text = $"Playing at {currentSpeedRatio}x speed...";
            }
            e.Handled = true;
            return;
        }
        
        // Get position relative to the actual canvas that was clicked
        Canvas? clickedCanvas = sender as Canvas;
        if (clickedCanvas == null)
            clickedCanvas = Segment1Canvas; // Fallback
            
        Point clickPos = e.GetPosition(clickedCanvas);
        double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
        double clickedTime = clickPos.X / effectivePixelsPerSecond;
        
        // Immediately seek for responsive feedback, but don't redraw timeline yet
        CurrentPositionSeconds = clickedTime;
        
        // Start drag with performance optimization
        isTimelineDragging = true;
        hasStartedDragging = false; // Will be set to true once drag threshold is exceeded
        timelineDragStartPoint = clickPos;
        timelineDragStartPosition = TimeSpan.FromSeconds(CurrentPositionSeconds);
        clickedCanvas.CaptureMouse();
        lastDraggedCanvas = clickedCanvas;
        
        // Pause main timer to prevent timeline updates during scrub
        bool wasPlaying = timer.IsEnabled;
        wasPlayingBeforeScrub = wasPlaying;
        if (wasPlaying)
        {
            timer.Stop();
        }
        
        // Enable low quality mode and start throttle timer for smooth scrubbing
        SetLowQualityMode(true);
        scrubThrottleTimer.Start();
        
        StatusText.Text = $"Scrubbing: {TimeSpan.FromSeconds(clickedTime).ToString(@"hh\:mm\:ss")}";
    }
    
    private void TimelineCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (isTimelinePanning)
        {
            // Handle timeline panning with middle mouse button
            Canvas? canvas = sender as Canvas;
            if (canvas != null)
            {
                Point currentPos = e.GetPosition(canvas);
                double deltaX = timelinePanStartPoint.X - currentPos.X;
                
                double newOffset = timelineHorizontalOffsetStart + deltaX;
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                SegmentTracksScroll.ScrollToHorizontalOffset(newOffset);
            }
        }
        else if (isTimelineDragging && HasVideo && lastDraggedCanvas != null)
        {
            Point currentPos = e.GetPosition(lastDraggedCanvas);
            
            // Check if we've exceeded drag threshold
            if (!hasStartedDragging)
            {
                double dragDistance = (currentPos - timelineDragStartPoint).Length;
                if (dragDistance < DragThreshold)
                {
                    return; // Not dragging yet, just jitter
                }
                
                // Start dragging
                hasStartedDragging = true;
                isScrubbing = true;
                
                // Enable low quality mode and start throttle timer for smooth scrubbing
                SetLowQualityMode(true);
                scrubThrottleTimer.Start();
            }
            
            double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
            double draggedTime = currentPos.X / effectivePixelsPerSecond;
            
            // Clamp to video duration
            draggedTime = Math.Max(0, Math.Min(draggedTime, VideoDurationSeconds));
            
            // Queue the scrub position for throttled update (~60 FPS)
            pendingScrubPosition = TimeSpan.FromSeconds(draggedTime);
            hasPendingScrub = true;
            
            StatusText.Text = $"Scrubbing: {TimeSpan.FromSeconds(draggedTime).ToString(@"hh\:mm\:ss")}";
        }
    }
    
    private void TimelineCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (isTimelineDragging)
        {
            isTimelineDragging = false;
            isScrubbing = false; // End scrubbing
            
            if (lastDraggedCanvas != null)
            {
                lastDraggedCanvas.ReleaseMouseCapture();
                lastDraggedCanvas = null;
            }
            
            // Only stop throttle timer if we actually started dragging
            if (hasStartedDragging)
            {
                scrubThrottleTimer.Stop();
                
                // Apply any pending scrub position
                if (hasPendingScrub)
                {
                    CurrentPositionSeconds = pendingScrubPosition.TotalSeconds;
                    hasPendingScrub = false;
                }
                
                SetLowQualityMode(false);
            }
            
            hasStartedDragging = false;
            
            UpdatePlaybackIndicator();
            
            // Restore timer if it was running before scrub
            if (wasPlayingBeforeScrub)
            {
                timer.Start();
                wasPlayingBeforeScrub = false;
            }
            
            SetLowQualityMode(false);
            // Don't call UpdateTimeline() here - it's expensive and causes jitter
            // The timeline will update naturally on the next timer tick
            
            StatusText.Text = $"Position: {TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss")}";
        }
    }
    
    private void TimelineCanvas_MouseMiddleButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Start timeline panning with middle mouse button
        Canvas? canvas = sender as Canvas;
        if (canvas != null)
        {
            isTimelinePanning = true;
            timelinePanStartPoint = e.GetPosition(canvas);
            timelineHorizontalOffsetStart = TimelineScrollViewer.HorizontalOffset;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.ScrollAll;
            e.Handled = true;
        }
    }
    
    private void TimelineCanvas_MouseMiddleButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (isTimelinePanning)
        {
            isTimelinePanning = false;
            Canvas? canvas = sender as Canvas;
            if (canvas != null)
            {
                canvas.ReleaseMouseCapture();
                canvas.Cursor = Cursors.Hand;
            }
            e.Handled = true;
        }
    }

    private void TimeStudyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimeStudyGrid.SelectedItem is TimeStudyEntry entry)
        {
            // Load the description, or show placeholder if empty
            if (string.IsNullOrWhiteSpace(entry.Description))
            {
                AnnotationText.Text = "Enter description for timestamp...";
                AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            }
            else
            {
                AnnotationText.Text = entry.Description;
                AnnotationText.Foreground = Brushes.White;
            }
        }
        else
        {
            AnnotationText.Text = "Enter description for timestamp...";
            AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }
    }

    private void AnnotationText_GotFocus(object sender, RoutedEventArgs e)
    {
        if (AnnotationText.Text == "Enter description for timestamp...")
        {
            AnnotationText.Text = "";
            AnnotationText.Foreground = Brushes.White;
        }
    }

    private void AnnotationText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AnnotationText.Text))
        {
            AnnotationText.Text = "Enter description for timestamp...";
            AnnotationText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }
    }

    private void AnnotationText_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Don't save if it's placeholder text or no entry is selected
        if (AnnotationText.Text == "Enter description for timestamp...")
            return;
            
        if (AnnotationText.Foreground is SolidColorBrush brush && 
            brush.Color == Color.FromRgb(150, 150, 150))
            return;
        
        // Save description to selected entry
        if (TimeStudyGrid.SelectedItem is TimeStudyEntry entry)
        {
            entry.Description = AnnotationText.Text;
        }
    }

    private void TimeStudyGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TimeStudyGrid.SelectedItem is TimeStudyEntry entry && HasVideo)
        {
            TimeSpan timestamp = TimeSpan.Parse(entry.Timestamp);
            CurrentPositionSeconds = timestamp.TotalSeconds;
            UpdateTimeline();
            
            // Scroll timeline to show this position
            ScrollTimelineToPosition(timestamp.TotalSeconds);
            
            StatusText.Text = $"Jumped to: {entry.Timestamp}";
        }
    }

    private void TimeRulerCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // This is now handled by TimelineCanvas_MouseMove for scrubbing
    }

    private void TimeRulerCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // No longer needed - preview removed
    }

    private void VideoContainer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!HasVideo)
        {
            e.Handled = true;
            return;
        }
        
        Point mousePos = e.GetPosition(VideoContainer);
        double zoomChange = e.Delta > 0 ? 0.1 : -0.1;
        double newZoom = Math.Max(0.1, Math.Min(4.0, currentZoom + zoomChange));
        
        if (Math.Abs(newZoom - currentZoom) > 0.0001)
        {
            double oldZoom = currentZoom;
            double contentMouseX = (VideoScrollViewer.HorizontalOffset + mousePos.X) / currentZoom;
            double contentMouseY = (VideoScrollViewer.VerticalOffset + mousePos.Y) / currentZoom;

            currentZoom = newZoom;
            ApplyZoom();

            double newHOffset = contentMouseX * newZoom - mousePos.X;
            double newVOffset = contentMouseY * newZoom - mousePos.Y;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                VideoScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newHOffset));
                VideoScrollViewer.ScrollToVerticalOffset(Math.Max(0, newVOffset));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        e.Handled = true;
    }

    private void VideoScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // Redirect to main zoom handler
        VideoContainer_PreviewMouseWheel(sender, e);
    }
    
    private void VideoScrollViewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Don't start panning if we're defining a zone
        if (isDefiningZone) return;
        
        isPanning = true;
        panStartPoint = e.GetPosition(VideoScrollViewer);
        horizontalOffsetStart = VideoScrollViewer.HorizontalOffset;
        verticalOffsetStart = VideoScrollViewer.VerticalOffset;
        VideoScrollViewer.Cursor = Cursors.Hand;
        VideoScrollViewer.CaptureMouse();
        e.Handled = true;
    }
    
    private void VideoScrollViewer_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Don't handle if we're defining a zone
        if (isDefiningZone) return;
        
        if (isPanning)
        {
            isPanning = false;
            VideoScrollViewer.Cursor = Cursors.Arrow;
            VideoScrollViewer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    
    private void VideoScrollViewer_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Don't pan if we're defining a zone
        if (isDefiningZone) return;
        
        if (isPanning)
        {
            Point currentPoint = e.GetPosition(VideoScrollViewer);
            double deltaX = panStartPoint.X - currentPoint.X;
            double deltaY = panStartPoint.Y - currentPoint.Y;
            
            VideoScrollViewer.ScrollToHorizontalOffset(horizontalOffsetStart + deltaX);
            VideoScrollViewer.ScrollToVerticalOffset(verticalOffsetStart + deltaY);
            
            e.Handled = true;
        }
    }



    private void MarkerDot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Ellipse dot && dot.Tag is TimeStudyEntry entry && HasVideo)
        {
            TimeSpan timestamp = TimeSpan.Parse(entry.Timestamp);
            CurrentPositionSeconds = timestamp.TotalSeconds;
            UpdateTimeline();
            StatusText.Text = $"Jumped to marker: {entry.Timestamp}";
            e.Handled = true;
        }
    }

    private void MarkerLabel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is TimeStudyEntry entry && HasVideo)
        {
            TimeSpan timestamp = TimeSpan.Parse(entry.Timestamp);
            CurrentPositionSeconds = timestamp.TotalSeconds;
            UpdateTimeline();
            StatusText.Text = $"Jumped to: {entry.ElementName ?? entry.Timestamp}";
            e.Handled = true;
        }
    }

    private void SegmentBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TimeStudyEntry entry && HasVideo)
        {
            TimeSpan timestamp = TimeSpan.Parse(entry.Timestamp);
            CurrentPositionSeconds = timestamp.TotalSeconds;
            UpdateTimeline();
            StatusText.Text = $"Playing segment: {entry.ElementName ?? entry.Timestamp}";
            e.Handled = true;
        }
    }

    private void ScrollTimelineToPosition(double timeInSeconds)
    {
        // Use the segment tracks scrollviewer
        if (SegmentTracksScroll != null && HasVideo)
        {
            double effectivePixelsPerSecond = timelinePixelsPerSecond * timelineZoom;
            double targetX = timeInSeconds * effectivePixelsPerSecond;
            
            // Center the position in the viewport
            double centerOffset = targetX - (SegmentTracksScroll.ViewportWidth / 2);
            SegmentTracksScroll.ScrollToHorizontalOffset(Math.Max(0, centerOffset));
        }
    }

    // Statistics Panel
    private void ShowStatistics_Click(object sender, RoutedEventArgs e)
    {
        if (StatisticsPanel.Visibility == Visibility.Collapsed)
        {
            UpdateStatistics();
            StatisticsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            StatisticsPanel.Visibility = Visibility.Collapsed;
        }
    }

    // Zone Event Log Panel
    private void ToggleZoneLog_Click(object sender, RoutedEventArgs e)
    {
        if (ZoneLogPanel.Visibility == Visibility.Collapsed)
        {
            UpdateZoneEventLog();
            ZoneLogPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ZoneLogPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearZoneLog_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Clear all zone event logs?", "Clear Zone Logs", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            foreach (var zone in workZones)
            {
                zone.Events.Clear();
                zone.EntryCount = 0;
                zone.PeopleCount = 0;
            }
            UpdateZoneEventLog();
            DrawAllZones();
            StatusText.Text = "All zone logs cleared";
        }
    }

    private void UpdateZoneEventLog()
    {
        var allEvents = new List<ZoneEventDisplay>();
        
        foreach (var zone in workZones)
        {
            foreach (var zoneEvent in zone.Events)
            {
                allEvents.Add(new ZoneEventDisplay
                {
                    VideoTime = TimeSpan.FromSeconds(zoneEvent.VideoTimeInSeconds).ToString(@"hh\:mm\:ss"),
                    EventType = zoneEvent.EventType,
                    EventColor = zoneEvent.EventType == "Entry" ? "#4CAF50" : "#F44336",
                    ZoneName = zoneEvent.ZoneName
                });
            }
        }
        
        // Sort by video time
        ZoneEventList.ItemsSource = allEvents.OrderBy(e => e.VideoTime).ToList();
        
        // Scroll to bottom to show newest entry
        Dispatcher.BeginInvoke(new Action(() => 
        {
            ZoneLogScrollViewer?.ScrollToBottom();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateStatistics()
    {
        if (timeStudyData.Count == 0)
        {
            StatsTotal.Text = "Total Entries: 0";
            StatsTotalTime.Text = "Total Time: 0.00s";
            StatsAvgTime.Text = "Average Duration: 0.00s";
            StatsMinTime.Text = "Min Duration: 0.00s";
            StatsMaxTime.Text = "Max Duration: 0.00s";
            StatsElementList.ItemsSource = null;
            return;
        }

        var durations = timeStudyData.Select(e => e.TimeInSeconds).Where(t => t > 0).ToList();
        double totalTime = durations.Sum();
        double avgTime = durations.Any() ? durations.Average() : 0;
        double minTime = durations.Any() ? durations.Min() : 0;
        double maxTime = durations.Any() ? durations.Max() : 0;

        StatsTotal.Text = $"Total Entries: {timeStudyData.Count}";
        StatsTotalTime.Text = useDecimalTimeFormat 
            ? $"Total Time: {(totalTime / 3600):F2} hrs" 
            : $"Total Time: {TimeSpan.FromSeconds(totalTime):hh\\:mm\\:ss}";
        StatsAvgTime.Text = useDecimalTimeFormat 
            ? $"Average Duration: {(avgTime / 3600):F2} hrs" 
            : $"Average Duration: {avgTime:F2}s";
        StatsMinTime.Text = useDecimalTimeFormat 
            ? $"Min Duration: {(minTime / 3600):F4} hrs" 
            : $"Min Duration: {minTime:F2}s";
        StatsMaxTime.Text = useDecimalTimeFormat 
            ? $"Max Duration: {(maxTime / 3600):F4} hrs" 
            : $"Max Duration: {maxTime:F2}s";

        // Group by element
        var elementStats = timeStudyData
            .Where(e => !string.IsNullOrWhiteSpace(e.ElementName))
            .GroupBy(e => e.ElementName)
            .Select(g => new
            {
                Element = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => e.TimeInSeconds),
                AvgTime = g.Average(e => e.TimeInSeconds)
            })
            .OrderByDescending(x => x.TotalTime)
            .ToList();

        var statsList = new List<TextBlock>();
        foreach (var stat in elementStats)
        {
            var timeDisplay = useDecimalTimeFormat 
                ? $"{(stat.TotalTime / 3600):F2} hrs (avg: {(stat.AvgTime / 3600):F4} hrs)" 
                : $"{stat.TotalTime:F2}s (avg: {stat.AvgTime:F2}s)";
            
            statsList.Add(new TextBlock
            {
                Text = $"{stat.Element}: {stat.Count}x, {timeDisplay}",
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 1, 0, 1)
            });
        }
        StatsElementList.ItemsSource = statsList;
    }

    // Time Format Toggle
    private void ToggleTimeFormat_Click(object sender, RoutedEventArgs e)
    {
        useDecimalTimeFormat = !useDecimalTimeFormat;
        TimeFormatToggle.Content = useDecimalTimeFormat ? "⏱ HH:MM:SS" : "⏱ Decimal";
        
        // Update statistics if visible
        if (StatisticsPanel.Visibility == Visibility.Visible)
        {
            UpdateStatistics();
        }
        
        StatusText.Text = useDecimalTimeFormat 
            ? "Time format: Decimal hours" 
            : "Time format: HH:MM:SS";
    }
}  // End of MainWindow class

public class TimeStudyEntry : INotifyPropertyChanged
{
    private string _timestamp = "";
    private double _timeInSeconds;
    private string _elementName = "";
    private string _description = "";
    private string _observations = "";
    private string _people = "1";
    private string _category = "";
    private int _segment = 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Timestamp 
    { 
        get => _timestamp; 
        set { _timestamp = value; OnPropertyChanged(nameof(Timestamp)); } 
    }
    
    public double TimeInSeconds 
    { 
        get => _timeInSeconds; 
        set { _timeInSeconds = value; OnPropertyChanged(nameof(TimeInSeconds)); } 
    }
    
    public string ElementName 
    { 
        get => _elementName; 
        set { _elementName = value; OnPropertyChanged(nameof(ElementName)); } 
    }
    
    public string Description 
    { 
        get => _description; 
        set { _description = value; OnPropertyChanged(nameof(Description)); } 
    }
    
    public string Observations 
    { 
        get => _observations; 
        set { _observations = value; OnPropertyChanged(nameof(Observations)); } 
    }
    
    public string People 
    { 
        get => _people; 
        set { _people = value; OnPropertyChanged(nameof(People)); } 
    }
    
    public string Category 
    { 
        get => _category; 
        set { _category = value; OnPropertyChanged(nameof(Category)); } 
    }

    public int Segment 
    { 
        get => _segment; 
        set { _segment = value; OnPropertyChanged(nameof(Segment)); } 
    }
    
    public BitmapSource? ThumbnailImage { get; set; } // Cached thumbnail image
}

// Class to track multiple video segments
public class VideoSegment
{
    public string FilePath { get; set; } = "";
    public double StartTime { get; set; } // Cumulative start time in the virtual timeline
    public double Duration { get; set; } // Duration of this video
    public double EndTime => StartTime + Duration;
}

// Work zone for motion tracking
public class WorkZone
{
    public string Name { get; set; } = "Zone";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Color { get; set; } = "#4CAF50"; // Green default
    public bool IsTracking { get; set; } = false;
    public int PeopleCount { get; set; } = 0; // Number of people currently in zone
    public int EntryCount { get; set; } = 0;
    public List<ZoneEvent> Events { get; set; } = new List<ZoneEvent>();
    public List<Person> People { get; set; } = new List<Person>(); // Individual people in zone
    
    // Motion detection properties
    public double MotionThreshold { get; set; } = 25.0; // Sensitivity (0-255, lower = more sensitive)
    public int MinMotionPixels { get; set; } = 50; // Minimum pixels changed to trigger motion
    public byte[]? PreviousFrame { get; set; } // Store previous frame for comparison
    public double CurrentMotionLevel { get; set; } = 0; // Current motion percentage
    public bool MotionDetected { get; set; } = false; // Is motion currently detected
}

public class Person
{
    public int Id { get; set; } // Unique identifier
    public double X { get; set; } // Position within zone
    public double Y { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public List<System.Windows.Point> MotionCenters { get; set; } = new List<System.Windows.Point>(); // Track movement
}

public class MotionBlob
{
    public int CenterX { get; set; }
    public int CenterY { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
    public int PixelCount { get; set; }
}

public class PersonDetection
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Confidence { get; set; }
    public List<Keypoint>? Keypoints { get; set; } // 17 body keypoints for pose estimation
}

public class Keypoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Confidence { get; set; }
    public string Name { get; set; } = "";
}

public class ZoneEvent
{
    public DateTime Timestamp { get; set; }
    public double VideoTimeInSeconds { get; set; }
    public string EventType { get; set; } = "Entry"; // "Entry" or "Exit"
    public string ZoneName { get; set; } = "";
}

public class ZoneEventDisplay
{
    public string VideoTime { get; set; } = "";
    public string EventType { get; set; } = "";
    public string EventColor { get; set; } = "";
    public string ZoneName { get; set; } = "";
}

public class ZoneSaveData
{
    public string Name { get; set; } = "Zone";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Color { get; set; } = "#4CAF50";
    public double MotionThreshold { get; set; } = 25.0;
    public int MinMotionPixels { get; set; } = 50;
}

// Project data for save/load
public class ProjectData
{
    public List<VideoSegment>? VideoSegments { get; set; }
    public List<TimeStudyEntryData>? TimeStudyEntries { get; set; }
    public Dictionary<int, string>? SegmentNames { get; set; }
    public List<string>? ElementLibrary { get; set; }
    public List<ZoneSaveData>? Zones { get; set; }
    public List<ZoneEventData>? ZoneEvents { get; set; }
}

public class ZoneEventData
{
    public string ZoneName { get; set; } = "";
    public double VideoTimeInSeconds { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

// Serializable version of TimeStudyEntry
public class TimeStudyEntryData
{
    public string Timestamp { get; set; } = "";
    public double TimeInSeconds { get; set; }
    public string ElementName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Observations { get; set; } = "";
    public string People { get; set; } = "1";
    public string Category { get; set; } = "";
    public int Segment { get; set; }
    public string? ThumbnailBase64 { get; set; }
}

// Event handlers for segment track hover effects
partial class MainWindow
{
    private void SegmentTrack_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)); // Lighter background on hover
        }
    }

    private void SegmentTrack_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)); // Original background
        }
    }
}