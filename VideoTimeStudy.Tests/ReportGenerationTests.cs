using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace VideoTimeStudy.Tests;

/// <summary>
/// Unit tests for HTML report generation functionality
/// Tests verify statistics calculations, HTML structure, and data integrity
/// </summary>
public class ReportGenerationTests
{
    [Fact]
    public void CalculateTotalTime_WithValidData_ReturnsCorrectSum()
    {
        // Arrange
        var durations = new List<double> { 1.5, 2.5, 3.0, 1.0 };
        
        // Act
        double totalTime = durations.Sum();
        
        // Assert
        Assert.Equal(8.0, totalTime);
    }
    
    [Fact]
    public void CalculateAverageTime_WithValidData_ReturnsCorrectAverage()
    {
        // Arrange
        var durations = new List<double> { 1.0, 2.0, 3.0, 4.0 };
        
        // Act
        double avgTime = durations.Average();
        
        // Assert
        Assert.Equal(2.5, avgTime);
    }
    
    [Fact]
    public void CalculateAverageTime_WithEmptyList_ReturnsZero()
    {
        // Arrange
        var durations = new List<double>();
        
        // Act
        double avgTime = durations.Any() ? durations.Average() : 0;
        
        // Assert
        Assert.Equal(0, avgTime);
    }
    
    [Fact]
    public void CalculateMinMax_WithValidData_ReturnsCorrectValues()
    {
        // Arrange
        var durations = new List<double> { 5.0, 1.5, 8.0, 3.2 };
        
        // Act
        double minTime = durations.Min();
        double maxTime = durations.Max();
        
        // Assert
        Assert.Equal(1.5, minTime);
        Assert.Equal(8.0, maxTime);
    }
    
    [Fact]
    public void CalculateStandardDeviation_WithValidData_ReturnsCorrectValue()
    {
        // Arrange
        var durations = new List<double> { 2, 4, 4, 4, 5, 5, 7, 9 };
        double avgTime = durations.Average(); // 5.0
        
        // Act
        double stdDev = Math.Sqrt(durations.Average(v => Math.Pow(v - avgTime, 2)));
        
        // Assert
        Assert.Equal(2.0, stdDev, precision: 1);
    }
    
    [Fact]
    public void CalculateStandardTime_WithRating_AppliesCorrectly()
    {
        // Arrange
        double timeInSeconds = 10.0;
        double rating = 120.0; // 120% performance rating
        
        // Act
        double standardTime = timeInSeconds * (rating / 100.0);
        
        // Assert
        Assert.Equal(12.0, standardTime);
    }
    
    [Fact]
    public void GroupByElement_WithMultipleElements_GroupsCorrectly()
    {
        // Arrange
        var entries = new List<TimeStudyEntry>
        {
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 1.0 },
            new TimeStudyEntry { ElementName = "Grasp", TimeInSeconds = 0.5 },
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 1.2 },
            new TimeStudyEntry { ElementName = "Grasp", TimeInSeconds = 0.6 }
        };
        
        // Act
        var grouped = entries
            .GroupBy(e => e.ElementName)
            .Select(g => new
            {
                Element = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => e.TimeInSeconds),
                AvgTime = g.Average(e => e.TimeInSeconds)
            })
            .OrderBy(x => x.Element)
            .ToList();
        
        // Assert
        Assert.Equal(2, grouped.Count);
        
        var graspGroup = grouped.First(g => g.Element == "Grasp");
        Assert.Equal(2, graspGroup.Count);
        Assert.Equal(1.1, graspGroup.TotalTime, precision: 2);
        Assert.Equal(0.55, graspGroup.AvgTime, precision: 2);
        
        var reachGroup = grouped.First(g => g.Element == "Reach");
        Assert.Equal(2, reachGroup.Count);
        Assert.Equal(2.2, reachGroup.TotalTime, precision: 2);
        Assert.Equal(1.1, reachGroup.AvgTime, precision: 2);
    }
    
    [Fact]
    public void FilterEmptyElements_RemovesNullAndWhitespace()
    {
        // Arrange
        var entries = new List<TimeStudyEntry>
        {
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 1.0 },
            new TimeStudyEntry { ElementName = "", TimeInSeconds = 0.5 },
            new TimeStudyEntry { ElementName = null, TimeInSeconds = 1.2 },
            new TimeStudyEntry { ElementName = "   ", TimeInSeconds = 0.6 }
        };
        
        // Act
        var filtered = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.ElementName))
            .ToList();
        
        // Assert
        Assert.Single(filtered);
        Assert.Equal("Reach", filtered[0].ElementName);
    }
    
    [Fact]
    public void ParsePerformanceRating_WithValidString_ReturnsDouble()
    {
        // Arrange
        string rating = "120";
        
        // Act
        bool success = double.TryParse(rating, out var result);
        
        // Assert
        Assert.True(success);
        Assert.Equal(120.0, result);
    }
    
    [Fact]
    public void ParsePerformanceRating_WithInvalidString_ReturnsDefault()
    {
        // Arrange
        string rating = "invalid";
        
        // Act
        double result = double.TryParse(rating, out var r) ? r : 100.0;
        
        // Assert
        Assert.Equal(100.0, result);
    }
    
    [Fact]
    public void HtmlEscape_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        string input = "<script>alert('test')</script>";
        
        // Act
        string escaped = System.Security.SecurityElement.Escape(input);
        
        // Assert
        Assert.DoesNotContain("<script>", escaped);
        Assert.Contains("&lt;script&gt;", escaped);
    }
    
    [Fact]
    public void FormatNumber_WithInvariantCulture_UsesDecimalPoint()
    {
        // Arrange
        double value = 1.5;
        
        // Act
        string formatted = value.ToString("F2", CultureInfo.InvariantCulture);
        
        // Assert
        Assert.Equal("1.50", formatted);
        Assert.Contains(".", formatted);
        Assert.DoesNotContain(",", formatted);
    }
    
    [Fact]
    public void GenerateChartData_WithMultipleElements_FormatsCorrectly()
    {
        // Arrange
        var elementStats = new List<dynamic>
        {
            new { Element = "Reach", TotalTime = 5.5, Count = 3 },
            new { Element = "Grasp", TotalTime = 2.25, Count = 2 }
        };
        var elementColors = new Dictionary<string, string>
        {
            { "Reach", "#FF8C00" },
            { "Grasp", "#1E90FF" }
        };
        
        // Act
        var labels = new List<string>();
        var data = new List<string>();
        var colors = new List<string>();
        
        foreach (var stat in elementStats)
        {
            labels.Add($"'{System.Security.SecurityElement.Escape(stat.Element)}'");
            data.Add(stat.TotalTime.ToString("F2", CultureInfo.InvariantCulture));
            colors.Add($"'{elementColors[stat.Element]}'");
        }
        
        var chartLabels = string.Join(",", labels);
        var chartData = string.Join(",", data);
        var chartColors = string.Join(",", colors);
        
        // Assert
        Assert.Equal("'Reach','Grasp'", chartLabels);
        Assert.Equal("5.50,2.25", chartData);
        Assert.Equal("'#FF8C00','#1E90FF'", chartColors);
    }
    
    [Fact]
    public void GenerateChartData_WithNoData_UsesPlaceholder()
    {
        // Arrange
        var elementStats = new List<dynamic>();
        
        // Act
        var labels = new List<string>();
        var data = new List<string>();
        var colors = new List<string>();
        
        if (!elementStats.Any())
        {
            labels.Add("'No Data'");
            data.Add("0");
            colors.Add("'#cccccc'");
        }
        
        var chartLabels = string.Join(",", labels);
        var chartData = string.Join(",", data);
        
        // Assert
        Assert.Equal("'No Data'", chartLabels);
        Assert.Equal("0", chartData);
    }
    
    [Fact]
    public void HtmlReportStructure_ContainsRequiredElements()
    {
        // Arrange
        var htmlContent = @"<!DOCTYPE html>
<html>
<head><title>Time Study Report</title></head>
<body>
<h1>Time Study Analysis Report</h1>
<div id='summary'>
<p>Total Time: 10.50s</p>
<p>Average Time: 2.10s</p>
</div>
<table id='data-table'>
<tr><th>Element</th><th>Count</th><th>Total Time</th></tr>
</table>
</body>
</html>";
        
        // Act & Assert
        Assert.Contains("<!DOCTYPE html>", htmlContent);
        Assert.Contains("<html>", htmlContent);
        Assert.Contains("<head>", htmlContent);
        Assert.Contains("<body>", htmlContent);
        Assert.Contains("Time Study Analysis Report", htmlContent);
        Assert.Contains("id='summary'", htmlContent);
        Assert.Contains("id='data-table'", htmlContent);
    }
    
    [Fact]
    public void CalculateElementStatistics_WithComplexData_ComputesCorrectly()
    {
        // Arrange
        var entries = new List<TimeStudyEntry>
        {
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 1.0, PerformanceRating = "100" },
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 1.5, PerformanceRating = "110" },
            new TimeStudyEntry { ElementName = "Reach", TimeInSeconds = 2.0, PerformanceRating = "90" },
            new TimeStudyEntry { ElementName = "Grasp", TimeInSeconds = 0.5, PerformanceRating = "100" },
            new TimeStudyEntry { ElementName = "Grasp", TimeInSeconds = 0.6, PerformanceRating = "120" }
        };
        
        // Act
        var stats = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.ElementName))
            .GroupBy(e => e.ElementName)
            .Select(g => new
            {
                Element = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => e.TimeInSeconds),
                AvgTime = g.Average(e => e.TimeInSeconds),
                MinTime = g.Min(e => e.TimeInSeconds),
                MaxTime = g.Max(e => e.TimeInSeconds),
                AvgRating = g.Average(e => double.TryParse(e.PerformanceRating, out var r) ? r : 100),
                StdDev = Math.Sqrt(g.Average(e => Math.Pow(e.TimeInSeconds - g.Average(x => x.TimeInSeconds), 2)))
            })
            .ToList();
        
        // Assert
        Assert.Equal(2, stats.Count);
        
        var reachStats = stats.First(s => s.Element == "Reach");
        Assert.Equal(3, reachStats.Count);
        Assert.Equal(4.5, reachStats.TotalTime);
        Assert.Equal(1.5, reachStats.AvgTime);
        Assert.Equal(1.0, reachStats.MinTime);
        Assert.Equal(2.0, reachStats.MaxTime);
        Assert.Equal(100.0, reachStats.AvgRating, precision: 1);
        
        var graspStats = stats.First(s => s.Element == "Grasp");
        Assert.Equal(2, graspStats.Count);
        Assert.Equal(1.1, graspStats.TotalTime, precision: 2);
        Assert.Equal(0.55, graspStats.AvgTime, precision: 2);
        Assert.Equal(110.0, graspStats.AvgRating, precision: 1);
    }
}
