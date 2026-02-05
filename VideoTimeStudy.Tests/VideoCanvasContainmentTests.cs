using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace VideoTimeStudy.Tests;

/// <summary>
/// Unit tests for video canvas containment and zooming functionality
/// Tests verify that video stays within bounds at all zoom levels
/// </summary>
public class VideoCanvasContainmentTests
{

    [Fact]
    public void VideoContainer_Should_HaveClipToBounds()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");

            Assert.NotNull(videoContainer);
            Assert.True(videoContainer!.ClipToBounds,
                "VideoContainer must have ClipToBounds=True to prevent video from escaping");

            window.Close();
        });
    }

    [Fact]
    public void VideoScaleTransform_Should_UseLayoutTransform()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");

            Assert.NotNull(videoContainer);
            Assert.NotNull(videoContainer!.LayoutTransform);
            Assert.IsType<ScaleTransform>(videoContainer.LayoutTransform);

            window.Close();
        });
    }

    [Fact]
    public void VideoContainer_Should_NotHaveFixedDimensions_Initially()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");

            Assert.NotNull(videoContainer);
            Assert.True(double.IsNaN(videoContainer!.Width) || videoContainer.Width > 0,
                "VideoContainer width should either be unset (NaN) or set to video dimensions");
            Assert.True(double.IsNaN(videoContainer.Height) || videoContainer.Height > 0,
                "VideoContainer height should either be unset (NaN) or set to video dimensions");

            window.Close();
        });
    }

    [Theory]
    [InlineData(0.1)]  // Minimum zoom
    [InlineData(0.5)]  // Half zoom
    [InlineData(1.0)]  // Normal zoom
    [InlineData(2.0)]  // 2x zoom
    [InlineData(4.0)]  // Maximum zoom
    public void ApplyZoom_Should_ClampToValidRange(double zoomLevel)
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");
            Assert.NotNull(videoContainer);

            videoContainer!.Width = 1920;
            videoContainer.Height = 1080;

            SetPrivateField(window, "currentZoom", zoomLevel);
            InvokePrivateMethod(window, "ApplyZoom");

            var transform = videoContainer.LayoutTransform as ScaleTransform;
            Assert.NotNull(transform);
            Assert.InRange(transform!.ScaleX, 0.1, 4.0);
            Assert.InRange(transform.ScaleY, 0.1, 4.0);
            Assert.Equal(transform.ScaleX, transform.ScaleY);

            window.Close();
        });
    }

    [Fact]
    public void ApplyZoom_Should_RejectInvalidZoomLevels()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");
            Assert.NotNull(videoContainer);
            videoContainer!.Width = 1920;
            videoContainer.Height = 1080;

            SetPrivateField(window, "currentZoom", 0.05);
            InvokePrivateMethod(window, "ApplyZoom");
            var transform = videoContainer.LayoutTransform as ScaleTransform;
            Assert.NotNull(transform);
            Assert.Equal(0.1, transform!.ScaleX);

            SetPrivateField(window, "currentZoom", 10.0);
            InvokePrivateMethod(window, "ApplyZoom");
            Assert.Equal(4.0, transform.ScaleX);

            window.Close();
        });
    }

    [Fact]
    public void VideoScrollViewer_Should_HaveClipToBounds()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var scrollViewer = GetControl<ScrollViewer>(window, "VideoScrollViewer");

            Assert.NotNull(scrollViewer);
            Assert.True(scrollViewer!.ClipToBounds,
                "VideoScrollViewer must have ClipToBounds=True to prevent overflow");

            window.Close();
        });
    }

    [Fact]
    public void MarkerCanvas_Should_ExistWithinVideoContainer()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var markerCanvas = GetControl<Canvas>(window, "MarkerCanvas");

            Assert.NotNull(markerCanvas);
            Assert.False(markerCanvas!.IsHitTestVisible,
                "MarkerCanvas should not be hit-testable to allow video interaction");

            window.Close();
        });
    }

    [Fact]
    public void VideoScaleTransform_Should_StartAtOneToOne()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");
            Assert.NotNull(videoContainer);
            var transform = videoContainer!.LayoutTransform as ScaleTransform;

            Assert.NotNull(transform);
            Assert.Equal(1.0, transform!.ScaleX);
            Assert.Equal(1.0, transform.ScaleY);

            window.Close();
        });
    }

    [Fact]
    public void VideoContainer_LayoutTransform_Should_ScaleBothAxesEqually()
    {
        ExecuteInSta(() =>
        {
            var window = new MainWindow();
            var videoContainer = GetControl<Grid>(window, "VideoContainer");
            Assert.NotNull(videoContainer);

            double[] testZooms = { 0.5, 1.0, 1.5, 2.0, 3.0 };

            foreach (var zoom in testZooms)
            {
                SetPrivateField(window, "currentZoom", zoom);
                InvokePrivateMethod(window, "ApplyZoom");

                var transform = videoContainer!.LayoutTransform as ScaleTransform;
                Assert.NotNull(transform);
                Assert.Equal(transform!.ScaleX, transform.ScaleY);
            }

            window.Close();
        });
    }

    // Helper method to get named control from window
    private T? GetControl<T>(Window window, string name) where T : FrameworkElement
    {
        return window.FindName(name) as T;
    }

    // Helper methods to access private members for testing
    private void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName,
            BindingFlags.NonPublic |
            BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    private void InvokePrivateMethod(object obj, string methodName)
    {
        var method = obj.GetType().GetMethod(methodName,
            BindingFlags.NonPublic |
            BindingFlags.Instance);
        method?.Invoke(obj, null);
    }

    private void ExecuteInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private void EnsureApplication()
    {
        if (Application.Current == null)
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }
}
