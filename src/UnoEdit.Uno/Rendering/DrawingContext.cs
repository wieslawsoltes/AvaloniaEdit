using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Utils;
using Windows.Foundation;
using Windows.UI;

namespace UnoEdit.Rendering;

/// <summary>Native geometry/brush adapter over Uno's existing Skia render pass.</summary>
public sealed class DrawingContext
{
    public DrawingContext(SKCanvas canvas) => Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    public SKCanvas Canvas { get; }
    public IDisposable PushClip(Rect rect) { var count = Canvas.Save(); Canvas.ClipRect(ToSkia(rect)); return new CallbackOnDispose(() => Canvas.RestoreToCount(count)); }
    public IDisposable PushOpacity(double opacity)
    { using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255, 0, 255)) }; var count = Canvas.SaveLayer(paint); return new CallbackOnDispose(() => Canvas.RestoreToCount(count)); }
    public IDisposable PushTransform(System.Numerics.Matrix3x2 matrix)
    { var count = Canvas.Save(); var m = new SKMatrix(matrix.M11, matrix.M21, matrix.M31, matrix.M12, matrix.M22, matrix.M32, 0, 0, 1); Canvas.Concat(in m); return new CallbackOnDispose(() => Canvas.RestoreToCount(count)); }
    public void DrawRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX = 0, double radiusY = 0)
    {
        var rect = ToSkia(rectangle);
        if (brush != null) { using var fill = CreatePaint(brush, rectangle); Canvas.DrawRoundRect(rect, (float)radiusX, (float)radiusY, fill); }
        if (pen != null) { using var stroke = CreatePen(pen, rectangle); Canvas.DrawRoundRect(rect, (float)radiusX, (float)radiusY, stroke); }
    }
    public void DrawLine(Pen pen, Point start, Point end)
    { if (pen == null) return; using var paint = CreatePen(pen, new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y))); Canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint); }
    public void DrawGeometry(Brush brush, Pen pen, Geometry geometry)
    {
        if (geometry == null) return;
        using var path = ToPath(geometry); var b = path.Bounds; var bounds = new Rect(b.Left, b.Top, b.Width, b.Height);
        if (brush != null) { using var fill = CreatePaint(brush, bounds); Canvas.DrawPath(path, fill); }
        if (pen != null) { using var stroke = CreatePen(pen, bounds); Canvas.DrawPath(path, stroke); }
    }
    internal static SKRect ToSkia(Rect r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
    private static SKColor Color(Color c, double opacity) => new(c.R, c.G, c.B, (byte)Math.Clamp(Math.Round(c.A * opacity), 0, 255));
    internal static SKPaint CreatePaint(Brush brush, Rect bounds)
    {
        var paint = new SKPaint { IsAntialias = true };
        if (brush is SolidColorBrush solid) { paint.Color = Color(solid.Color, brush.Opacity); return paint; }
        if (brush is LinearGradientBrush linear)
        {
            var stops = linear.GradientStops.OrderBy(x => x.Offset).ToArray();
            if (stops.Length == 0) { paint.Color = SKColors.Transparent; return paint; }
            if (stops.Length == 1) { paint.Color = Color(stops[0].Color, brush.Opacity); return paint; }
            SKPoint PointAt(Point point) => linear.MappingMode == BrushMappingMode.Absolute ? new((float)point.X, (float)point.Y) : new((float)(bounds.Left + point.X * bounds.Width), (float)(bounds.Top + point.Y * bounds.Height));
            paint.Shader = SKShader.CreateLinearGradient(PointAt(linear.StartPoint), PointAt(linear.EndPoint), stops.Select(x => Color(x.Color, brush.Opacity)).ToArray(), stops.Select(x => (float)x.Offset).ToArray(), linear.SpreadMethod == GradientSpreadMethod.Repeat ? SKShaderTileMode.Repeat : linear.SpreadMethod == GradientSpreadMethod.Reflect ? SKShaderTileMode.Mirror : SKShaderTileMode.Clamp);
            return paint;
        }
        paint.Dispose();
        throw new NotSupportedException($"The native Skia drawing adapter cannot render {brush.GetType().Name}. Use a supported native solid or linear-gradient brush.");
    }
    private static SKPaint CreatePen(Pen pen, Rect bounds)
    {
        var paint = CreatePaint(pen.Brush, bounds); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)pen.Thickness;
        if (pen.DashStyle != null && pen.DashStyle.Dashes.Count > 0) paint.PathEffect = SKPathEffect.CreateDash(pen.DashStyle.Dashes.Select(x => (float)(x * pen.Thickness)).ToArray(), (float)pen.DashStyle.Offset);
        return paint;
    }
    private static SKPath ToPath(Geometry geometry)
    {
        var path = new SKPath();
        switch (geometry)
        {
            case RectangleGeometry r: path.AddRect(ToSkia(r.Rect)); break;
            case EllipseGeometry e: path.AddOval(new SKRect((float)(e.Center.X - e.RadiusX), (float)(e.Center.Y - e.RadiusY), (float)(e.Center.X + e.RadiusX), (float)(e.Center.Y + e.RadiusY))); break;
            case LineGeometry l: path.MoveTo((float)l.StartPoint.X, (float)l.StartPoint.Y); path.LineTo((float)l.EndPoint.X, (float)l.EndPoint.Y); break;
            case GeometryGroup group:
                path.FillType = group.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                foreach (var child in group.Children) { using var c = ToPath(child); path.AddPath(c); } break;
            case PathGeometry g:
                path.FillType = g.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                foreach (var figure in g.Figures)
                {
                    path.MoveTo((float)figure.StartPoint.X, (float)figure.StartPoint.Y);
                    foreach (var segment in figure.Segments)
                        switch (segment)
                        {
                            case LineSegment l: path.LineTo((float)l.Point.X, (float)l.Point.Y); break;
                            case PolyLineSegment l: foreach (var point in l.Points) path.LineTo((float)point.X, (float)point.Y); break;
                            case BezierSegment c: path.CubicTo((float)c.Point1.X, (float)c.Point1.Y, (float)c.Point2.X, (float)c.Point2.Y, (float)c.Point3.X, (float)c.Point3.Y); break;
                            case QuadraticBezierSegment q: path.QuadTo((float)q.Point1.X, (float)q.Point1.Y, (float)q.Point2.X, (float)q.Point2.Y); break;
                            case ArcSegment a: path.ArcTo((float)a.Size.Width, (float)a.Size.Height, (float)a.RotationAngle, a.IsLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small, a.SweepDirection == SweepDirection.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise, (float)a.Point.X, (float)a.Point.Y); break;
                            default: path.Dispose(); throw new NotSupportedException($"Geometry segment {segment.GetType().Name} is not supported.");
                        }
                    if (figure.IsClosed) path.Close();
                }
                break;
            default: path.Dispose(); throw new NotSupportedException($"Geometry {geometry.GetType().Name} is not supported.");
        }
        if (geometry.Transform != null)
        {
            var o = geometry.Transform.TransformPoint(new Point()); var x = geometry.Transform.TransformPoint(new Point(1, 0)); var y = geometry.Transform.TransformPoint(new Point(0, 1));
            path.Transform(new SKMatrix((float)(x.X - o.X), (float)(y.X - o.X), (float)o.X, (float)(x.Y - o.Y), (float)(y.Y - o.Y), (float)o.Y, 0, 0, 1));
        }
        return path;
    }
}
public sealed class Pen
{
    public Pen(Brush brush, double thickness = 1, DashStyle dashStyle = null) { Brush = brush ?? throw new ArgumentNullException(nameof(brush)); Thickness = thickness; DashStyle = dashStyle; }
    public Brush Brush { get; set; }
    public double Thickness { get; set; }
    public DashStyle DashStyle { get; set; }
}
public sealed class DashStyle
{
    public DashStyle(IEnumerable<double> dashes, double offset = 0) { Dashes = dashes.ToArray(); Offset = offset; }
    public IReadOnlyList<double> Dashes { get; }
    public double Offset { get; }
    public static DashStyle Dot { get; } = new(new[] { 1.0, 2.0 });
}
