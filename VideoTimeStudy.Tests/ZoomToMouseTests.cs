using System;
using System.Windows;
using Xunit;

namespace VideoTimeStudy.Tests;

/// <summary>
/// Tests that verify the zoom/pan helper logic which keeps the mouse location fixed during zoom
/// and allows the inner (video) box to be panned within the outer boundary box.
/// </summary>
public class ZoomToMouseTests
{
    private const double OuterWidth = 400;
    private const double OuterHeight = 300;
    private const double ContentWidth = 1600;
    private const double ContentHeight = 1200;

    [Fact]
    public void ZoomAt_MouseLocationRemainsFixed()
    {
        var viewport = new ViewportZoomSimulator(OuterWidth, OuterHeight, ContentWidth, ContentHeight);
        viewport.OffsetX = 150;
        viewport.OffsetY = 80;

        var mousePoint = new Point(200, 120);
        var beforeContent = viewport.GetContentPoint(mousePoint);

        viewport.ZoomAt(mousePoint, deltaZoom: 0.75); // simulate scroll up
        var afterContent = viewport.GetContentPoint(mousePoint);

        Assert.Equal(beforeContent.X, afterContent.X, precision: 3);
        Assert.Equal(beforeContent.Y, afterContent.Y, precision: 3);
    }

    [Fact]
    public void Pan_AnyZoom_ChangesOffsetsWithinBounds()
    {
        var viewport = new ViewportZoomSimulator(OuterWidth, OuterHeight, ContentWidth, ContentHeight);
        viewport.ZoomAt(new Point(OuterWidth / 2, OuterHeight / 2), deltaZoom: 1.0); // zoom to 2x
        viewport.Pan(50, 75);

        Assert.InRange(viewport.OffsetX, 0, viewport.MaxHorizontalOffset);
        Assert.InRange(viewport.OffsetY, 0, viewport.MaxVerticalOffset);
    }

    [Fact]
    public void CenterMark_TracksOuterBoxCenter()
    {
        var viewport = new ViewportZoomSimulator(OuterWidth, OuterHeight, ContentWidth, ContentHeight);
        viewport.CenterContent();
        var containerCenter = viewport.GetContainerCenter();
        var contentCenter = viewport.GetContentPoint(containerCenter);

        Assert.Equal(ContentWidth / 2, contentCenter.X, precision: 3);
        Assert.Equal(ContentHeight / 2, contentCenter.Y, precision: 3);
    }
}

internal class ViewportZoomSimulator
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;

    public double OuterWidth { get; }
    public double OuterHeight { get; }
    public double ContentWidth { get; }
    public double ContentHeight { get; }

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Zoom { get; private set; } = 1.0;

    public double MaxHorizontalOffset => Math.Max(ContentWidth * Zoom - OuterWidth, 0);
    public double MaxVerticalOffset => Math.Max(ContentHeight * Zoom - OuterHeight, 0);

    public ViewportZoomSimulator(double outerWidth, double outerHeight, double contentWidth, double contentHeight)
    {
        OuterWidth = outerWidth;
        OuterHeight = outerHeight;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
    }

    public void ZoomAt(Point viewportPoint, double deltaZoom)
    {
        var newZoom = Math.Clamp(Zoom + deltaZoom, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - Zoom) < 1e-6)
        {
            return;
        }

        var contentX = (OffsetX + viewportPoint.X) / Zoom;
        var contentY = (OffsetY + viewportPoint.Y) / Zoom;
        Zoom = newZoom;
        OffsetX = contentX * Zoom - viewportPoint.X;
        OffsetY = contentY * Zoom - viewportPoint.Y;
        ClampOffsets();
    }

    public void Pan(double deltaX, double deltaY)
    {
        OffsetX = Math.Clamp(OffsetX + deltaX, 0, MaxHorizontalOffset);
        OffsetY = Math.Clamp(OffsetY + deltaY, 0, MaxVerticalOffset);
    }

    public void CenterContent()
    {
        OffsetX = Math.Max(0, (ContentWidth * Zoom - OuterWidth) / 2.0);
        OffsetY = Math.Max(0, (ContentHeight * Zoom - OuterHeight) / 2.0);
    }

    public Point GetContentPoint(Point viewportPoint)
        => new((OffsetX + viewportPoint.X) / Zoom, (OffsetY + viewportPoint.Y) / Zoom);

    public Point GetContainerCenter()
        => new(OuterWidth / 2.0, OuterHeight / 2.0);

    private void ClampOffsets()
    {
        OffsetX = Math.Clamp(OffsetX, 0, MaxHorizontalOffset);
        OffsetY = Math.Clamp(OffsetY, 0, MaxVerticalOffset);
    }
}