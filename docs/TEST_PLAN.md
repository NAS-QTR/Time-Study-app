# Video Time Study Application - Test Plan

## Date: December 5, 2025

---

## 1. VIDEO LOADING TESTS

### Test 1.1: Load Video File
- **Steps:**
  1. Open application
  2. Click File > Open Video
  3. Select a valid video file (MP4, AVI, MKV, or WMV)
- **Expected:** Video loads, displays in player, status bar shows filename and duration
- **Status:** [ ] Pass [ ] Fail

### Test 1.2: Video Player Controls
- **Steps:**
  1. Load a video
  2. Test Play button
  3. Test Pause button
  4. Test Stop button
- **Expected:** All controls work correctly, status bar updates
- **Status:** [ ] Pass [ ] Fail

### Test 1.3: Playback Speed
- **Steps:**
  1. Load a video and play
  2. Test each speed option (0.25x, 0.5x, 0.75x, 1x, 1.25x, 1.5x, 2x, 4x, 8x)
  3. Verify speed changes via menu and speed combo box
- **Expected:** Video speed changes correctly, status bar shows current speed
- **Status:** [ ] Pass [ ] Fail

---

## 2. TIMELINE SYNCHRONIZATION TESTS

### Test 2.1: Timeline Ruler Alignment
- **Steps:**
  1. Load a video
  2. Check that time markers (00:00:00, 00:00:30, 00:01:00, etc.) align properly
  3. Zoom in/out using +/- buttons
  4. Verify markers remain aligned at all zoom levels
- **Expected:** Time markers stay properly aligned and don't overlap
- **Status:** [ ] Pass [ ] Fail

### Test 2.2: Segment Bar Alignment
- **Steps:**
  1. Load a video and create several timestamps
  2. Verify segment bars start/end at correct time positions
  3. Compare ruler timestamps with segment positions
  4. Zoom in/out and verify alignment is maintained
- **Expected:** Segment bars align exactly with timeline ruler
- **Status:** [ ] Pass [ ] Fail

### Test 2.3: Thumbnail Strip Alignment
- **Steps:**
  1. Create timestamps
  2. Verify thumbnail boxes align with segment bars above
  3. Check that thumbnails line up with ruler timestamps
  4. Scroll horizontally and verify no drift
- **Expected:** Thumbnails perfectly aligned with segments and ruler
- **Status:** [ ] Pass [ ] Fail

### Test 2.4: Playhead Alignment
- **Steps:**
  1. Play video
  2. Watch playhead (blue line) move across timeline
  3. Verify it aligns with current time in ruler
  4. Pause at various points and check alignment
- **Expected:** Playhead position matches timeline ruler exactly
- **Status:** [ ] Pass [ ] Fail

---

## 3. TIMELINE INTERACTION TESTS

### Test 3.1: Single Click on Timeline
- **Steps:**
  1. Load video
  2. Single click at various positions on timeline
  3. Verify video jumps to clicked position
- **Expected:** Video seeks to exact clicked time, status shows timestamp
- **Status:** [ ] Pass [ ] Fail

### Test 3.2: Click and Drag Scrubbing
- **Steps:**
  1. Load video
  2. Click and hold on timeline
  3. Drag left and right
  4. Release mouse
- **Expected:** Video scrubs smoothly, status shows "Scrubbing: [time]"
- **Status:** [ ] Pass [ ] Fail

### Test 3.3: Double-Click Play/Pause
- **Steps:**
  1. Load video (paused state)
  2. Double-click timeline
  3. Verify video starts playing
  4. Double-click again
  5. Verify video pauses
- **Expected:** Double-click toggles play/pause, single-click still seeks
- **Status:** [ ] Pass [ ] Fail

### Test 3.4: Scroll Wheel Zoom
- **Steps:**
  1. Load video with timeline visible
  2. Hover over timeline
  3. Scroll wheel up (zoom in)
  4. Scroll wheel down (zoom out)
  5. Verify zoom percentage updates
- **Expected:** Timeline zooms smoothly, maintains scroll position
- **Status:** [ ] Pass [ ] Fail

### Test 3.5: Zoom Buttons
- **Steps:**
  1. Test + button (zoom in)
  2. Test - button (zoom out)
  3. Test ⊙ button (reset to 100%)
  4. Test ⊏⊐ button (fit to window)
- **Expected:** All zoom controls work, percentage label updates
- **Status:** [ ] Pass [ ] Fail

---

## 4. TIMESTAMP MARKING TESTS

### Test 4.1: Mark Timestamp
- **Steps:**
  1. Load video and play
  2. Click "Mark Timestamp" button at various points
  3. Verify entries appear in data grid
- **Expected:** Timestamp, Element, Method columns populated correctly
- **Status:** [ ] Pass [ ] Fail

### Test 4.2: Add Description
- **Steps:**
  1. Mark timestamp
  2. Enter description in text box
  3. Verify description appears in grid
- **Expected:** Description field updates correctly
- **Status:** [ ] Pass [ ] Fail

### Test 4.3: Edit Grid Entry
- **Steps:**
  1. Mark timestamps
  2. Double-click cells in grid to edit
  3. Modify Timestamp, Element, Method, Description
  4. Verify changes persist
- **Expected:** All fields editable and save correctly
- **Status:** [ ] Pass [ ] Fail

### Test 4.4: Delete Entry
- **Steps:**
  1. Mark timestamps
  2. Select row in grid
  3. Click "Clear All" or delete entry
  4. Verify timeline updates
- **Expected:** Entry removed, timeline segments recalculate
- **Status:** [ ] Pass [ ] Fail

### Test 4.5: Duration Calculation
- **Steps:**
  1. Mark timestamp at 00:00:05
  2. Mark timestamp at 00:00:10
  3. Verify first segment shows 5.00 seconds
  4. Verify second segment shows 5.00 seconds
- **Expected:** TimeInSeconds column shows correct durations between timestamps
- **Status:** [ ] Pass [ ] Fail

---

## 5. DATA GRID INTERACTION TESTS

### Test 5.1: Select Grid Row
- **Steps:**
  1. Create multiple timestamps
  2. Click on different rows in grid
- **Expected:** Row highlights, description updates in text box
- **Status:** [ ] Pass [ ] Fail

### Test 5.2: Double-Click Grid Row
- **Steps:**
  1. Create timestamps
  2. Double-click a row in grid
  3. Verify video seeks to that timestamp
  4. Verify timeline scrolls to show that position
- **Expected:** Video jumps to timestamp, timeline centers on position
- **Status:** [ ] Pass [ ] Fail

---

## 6. TIMELINE RESIZING TESTS

### Test 6.1: Resize Timeline Panel
- **Steps:**
  1. Load video
  2. Locate GridSplitter between main content and timeline
  3. Drag splitter up and down
  4. Verify minimum height (150px) enforced
- **Expected:** Timeline resizes smoothly, content adjusts
- **Status:** [ ] Pass [ ] Fail

### Test 6.2: Resize Right Panel
- **Steps:**
  1. Locate GridSplitter between video player and data grid
  2. Drag splitter left and right
- **Expected:** Data grid width adjusts (default 550px)
- **Status:** [ ] Pass [ ] Fail

---

## 7. SEGMENT VISUALIZATION TESTS

### Test 7.1: Segment Colors
- **Steps:**
  1. Create 6+ timestamps
  2. Verify segments use color rotation (Orange, Blue, Green, Pink, Purple, Yellow)
  3. Check color consistency between segment bars and thumbnail borders
- **Expected:** Colors cycle through palette, match on timeline and thumbnails
- **Status:** [ ] Pass [ ] Fail

### Test 7.2: Segment Labels
- **Steps:**
  1. Create timestamp with Element name "Assembly"
  2. Verify segment bar shows "Assembly"
  3. Create timestamp without Element name
  4. Verify shows "Segment #N"
- **Expected:** Labels display correctly on segment bars
- **Status:** [ ] Pass [ ] Fail

### Test 7.3: Duration Labels
- **Steps:**
  1. Create segments of various lengths
  2. Verify duration shows on bars (e.g., "5.2s")
  3. Check that short segments (<50px) don't show duration
- **Expected:** Duration labels appear on segments wide enough
- **Status:** [ ] Pass [ ] Fail

### Test 7.4: Thumbnail Boxes
- **Steps:**
  1. Create timestamps
  2. Verify thumbnail boxes appear in bottom strip
  3. Check segment number (#1, #2, etc.)
  4. Verify timestamp text shows
  5. Click thumbnail to jump to segment
- **Expected:** Thumbnails display correctly, clickable
- **Status:** [ ] Pass [ ] Fail

---

## 8. CSV IMPORT/EXPORT TESTS

### Test 8.1: Export Data
- **Steps:**
  1. Create several timestamps with descriptions
  2. Click File > Export Data
  3. Save CSV file
  4. Open in Excel/text editor
  5. Verify columns: Segment, Timestamp, Element, Method, Description, Observations, Rating, TimeInSeconds
- **Expected:** CSV file created with all data correctly formatted
- **Status:** [ ] Pass [ ] Fail

### Test 8.2: Import Data
- **Steps:**
  1. Create CSV file with timestamp data
  2. Click File > Import Data (CSV)
  3. Select file
  4. Verify data loads into grid
  5. Verify timeline shows segments
- **Expected:** Data imports correctly, timeline visualizes segments
- **Status:** [ ] Pass [ ] Fail

---

## 9. VIDEO ZOOM TESTS

### Test 9.1: Video Zoom In
- **Steps:**
  1. Load video
  2. Click View > Zoom In (or press +)
  3. Verify video enlarges
- **Expected:** Video scales up, label shows zoom percentage
- **Status:** [ ] Pass [ ] Fail

### Test 9.2: Video Zoom Out
- **Steps:**
  1. Zoom in first
  2. Click View > Zoom Out (or press -)
  3. Verify video shrinks
- **Expected:** Video scales down
- **Status:** [ ] Pass [ ] Fail

### Test 9.3: Video Zoom Reset
- **Steps:**
  1. Zoom to any level
  2. Click View > Zoom Reset
  3. Verify video returns to 100%
- **Expected:** Video at original size
- **Status:** [ ] Pass [ ] Fail

---

## 10. WINDOW STATE TESTS

### Test 10.1: Application Starts Maximized
- **Steps:**
  1. Close application if open
  2. Launch application
  3. Verify window opens maximized
- **Expected:** Window fills screen on startup
- **Status:** [ ] Pass [ ] Fail

### Test 10.2: Window Restore/Maximize
- **Steps:**
  1. Click restore/maximize button
  2. Verify layout adjusts properly
  3. Test timeline still scrolls correctly
- **Expected:** Application resizes gracefully
- **Status:** [ ] Pass [ ] Fail

---

## 11. EDGE CASE TESTS

### Test 11.1: Empty Timeline
- **Steps:**
  1. Load video
  2. Don't create any timestamps
  3. Verify timeline shows only ruler
- **Expected:** No errors, empty timeline display
- **Status:** [ ] Pass [ ] Fail

### Test 11.2: Very Long Video
- **Steps:**
  1. Load video >1 hour
  2. Verify hour format displays (hh:mm:ss)
  3. Test scrolling entire timeline
  4. Create timestamps throughout
- **Expected:** Application handles long videos smoothly
- **Status:** [ ] Pass [ ] Fail

### Test 11.3: Very Short Video
- **Steps:**
  1. Load video <10 seconds
  2. Verify timeline displays correctly
  3. Create timestamps
- **Expected:** Short videos display properly
- **Status:** [ ] Pass [ ] Fail

### Test 11.4: Maximum Zoom In
- **Steps:**
  1. Load video
  2. Zoom in to maximum (1000%)
  3. Verify no overlap or rendering issues
- **Expected:** Timeline renders correctly at max zoom
- **Status:** [ ] Pass [ ] Fail

### Test 11.5: Minimum Zoom Out
- **Steps:**
  1. Load long video
  2. Zoom out to minimum (1%)
  3. Verify timestamps still readable
- **Expected:** Timeline compresses correctly, no overlap
- **Status:** [ ] Pass [ ] Fail

### Test 11.6: Rapid Clicking
- **Steps:**
  1. Load video
  2. Rapidly click timeline in different positions
  3. Rapidly create timestamps
  4. Test for crashes or lag
- **Expected:** Application remains responsive
- **Status:** [ ] Pass [ ] Fail

---

## 12. KNOWN ISSUES TO TEST

### Issue 12.1: Timeline/Ruler Sync
- **Problem:** Timeline segments may not align with ruler timestamps after zoom
- **Test:** Zoom to various levels, verify pixel-perfect alignment
- **Status:** [ ] Pass [ ] Fail

### Issue 12.2: Thumbnail Alignment
- **Problem:** Thumbnail strip may drift from timeline
- **Test:** Scroll timeline, verify thumbnails stay aligned
- **Status:** [ ] Pass [ ] Fail

### Issue 12.3: Double-Click vs Drag
- **Problem:** Double-click might trigger both play/pause and seek
- **Test:** Double-click rapidly, ensure only play/pause happens
- **Status:** [ ] Pass [ ] Fail

### Issue 12.4: Canvas Width Consistency
- **Problem:** TimeRulerCanvas, TimelineCanvas, ThumbnailCanvas may have different widths
- **Test:** Inspect all three canvas widths at various zoom levels
- **Expected:** All three canvases should have identical widths
- **Status:** [ ] Pass [ ] Fail

---

## 13. PERFORMANCE TESTS

### Test 13.1: Many Timestamps
- **Steps:**
  1. Create 50+ timestamps
  2. Verify timeline renders without lag
  3. Test scrolling performance
- **Expected:** Smooth performance with many segments
- **Status:** [ ] Pass [ ] Fail

### Test 13.2: Timeline Redraw Performance
- **Steps:**
  1. Play video (timeline redraws every 100ms)
  2. Monitor CPU usage
  3. Verify smooth playhead movement
- **Expected:** Minimal performance impact during playback
- **Status:** [ ] Pass [ ] Fail

---

## 14. USER EXPERIENCE TESTS

### Test 14.1: Color Readability
- **Steps:**
  1. Create timestamps
  2. Verify all text is readable (white on dark background)
  3. Check timestamp labels on ruler (14px, white text, dark background with border)
- **Expected:** All text clearly visible
- **Status:** [ ] Pass [ ] Fail

### Test 14.2: Tooltip and Feedback
- **Steps:**
  1. Hover over zoom buttons
  2. Verify tooltips appear
  3. Check status bar updates during operations
- **Expected:** Clear feedback for all actions
- **Status:** [ ] Pass [ ] Fail

### Test 14.3: Intuitive Controls
- **Steps:**
  1. Give application to new user
  2. Observe if they can load video and create timestamps without help
- **Expected:** Interface is intuitive
- **Status:** [ ] Pass [ ] Fail

---

## TEST SUMMARY

**Total Tests:** 70+
**Tests Passed:** ___
**Tests Failed:** ___
**Pass Rate:** ___%

### Critical Issues Found:
1. _______________________________________________
2. _______________________________________________
3. _______________________________________________

### Recommendations:
1. _______________________________________________
2. _______________________________________________
3. _______________________________________________

### Notes:
_______________________________________________
_______________________________________________
_______________________________________________

**Tested By:** _______________
**Date:** _______________
**Version:** 1.0
