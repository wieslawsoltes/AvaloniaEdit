using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using Windows.Foundation;

namespace UnoEdit.Rendering;

public sealed class BackgroundGeometryBuilder
{
    private readonly List<Rect> _rectangles = new();
    public double CornerRadius { get; set; }
    public bool AlignToWholePixels { get; set; }
    public double BorderThickness { get; set; }
    public bool ExtendToFullWidthAtLineEnd { get; set; }
    public void AddSegment(TextView textView, ISegment segment)
    { foreach (var rect in GetRectsForSegment(textView, segment, ExtendToFullWidthAtLineEnd)) AddRectangle(textView, rect); }
    public void AddRectangle(TextView textView, Rect rectangle)
    {
        if (textView == null) throw new ArgumentNullException(nameof(textView));
        var scale = textView.XamlRoot?.RasterizationScale ?? 1;
        if (AlignToWholePixels) rectangle = new Rect(Math.Floor(rectangle.Left * scale) / scale, Math.Floor(rectangle.Top * scale) / scale, Math.Ceiling(rectangle.Right * scale) / scale - Math.Floor(rectangle.Left * scale) / scale, Math.Ceiling(rectangle.Bottom * scale) / scale - Math.Floor(rectangle.Top * scale) / scale);
        if (rectangle.Width > 0 && rectangle.Height > 0) _rectangles.Add(rectangle);
    }
    public static IEnumerable<Rect> GetRectsForSegment(TextView textView, ISegment segment, bool extendToFullWidthAtLineEnd = false)
    {
        if (textView == null) throw new ArgumentNullException(nameof(textView));
        if (segment == null) throw new ArgumentNullException(nameof(segment));
        foreach (var r in textView.Viewport.GetSegmentRectangles(segment.Offset, segment.Length, extendToFullWidthAtLineEnd)) yield return new Rect(r.Left, r.Top, r.Width, r.Height);
    }
    public void AddRectangle(double left, double top, double right, double bottom)
    { if (right > left && bottom > top) _rectangles.Add(new Rect(left, top, right - left, bottom - top)); }
    public void CloseFigure() { /* Each added rectangle is an independent closed contour. */ }
    public Geometry CreateGeometry()
    {
        if (_rectangles.Count == 0) return null;
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var r in _rectangles)
        {
            var radius = Math.Min(Math.Max(0, CornerRadius), Math.Min(r.Width, r.Height) / 2);
            var figure = new PathFigure { StartPoint = new Point(r.Left + radius, r.Top), IsClosed = true, IsFilled = true };
            void Line(double x, double y) => figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
            void Corner(double x, double y) => figure.Segments.Add(new ArcSegment { Point = new Point(x, y), Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise });
            Line(r.Right - radius, r.Top); if (radius > 0) Corner(r.Right, r.Top + radius);
            Line(r.Right, r.Bottom - radius); if (radius > 0) Corner(r.Right - radius, r.Bottom);
            Line(r.Left + radius, r.Bottom); if (radius > 0) Corner(r.Left, r.Bottom - radius);
            Line(r.Left, r.Top + radius); if (radius > 0) Corner(r.Left + radius, r.Top);
            geometry.Figures.Add(figure);
        }
        return geometry;
    }
}
