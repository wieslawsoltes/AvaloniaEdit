using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Rendering.Skia;
using Windows.Foundation;
using Windows.UI.Text;

namespace UnoEdit.Rendering;

/// <summary>Shapes the original editor's text runs, including atomic drawable runs.</summary>
internal sealed class NativeTextLayout : IDisposable
{
    internal sealed record ObjectPlacement(int VisualStart, int DisplayStart, DrawableTextRun Run);
    private readonly int[] _visualToDisplay;
    private readonly int[] _displayToVisual;
    private readonly string _display;
    private readonly int[] _boundaries;
    private bool _disposed;
    internal TextLineLayout Layout { get; }
    internal IReadOnlyList<TextRun> Runs { get; }
    internal IReadOnlyList<ObjectPlacement> Objects { get; }
    internal IReadOnlyList<TextLine> Lines { get; }
    internal int Length => _visualToDisplay.Length - 1;

    internal NativeTextLayout(IReadOnlyList<TextRun> runs, TextViewStyle style, float? width, float minimumHeight)
    {
        Runs = runs ?? throw new ArgumentNullException(nameof(runs));
        var length = runs.Sum(x => x.Length);
        _visualToDisplay = new int[length + 1];
        var reverse = new List<int> { 0 };
        var spans = new List<TextStyleSpan>();
        var objects = new List<ObjectPlacement>();
        var text = new StringBuilder();
        var visual = 0;
        foreach (var run in runs)
        {
            if (run == null || run.Length <= 0) throw new ArgumentException("Text runs must have positive length.", nameof(runs));
            var properties = run.Properties ?? throw new ArgumentException("Every rendered run requires text properties.", nameof(runs));
            var start = text.Length;
            if (run is DrawableTextRun drawable)
            {
                var box = drawable.Size;
                if (!double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width < 0 || box.Height < 0)
                    throw new ArgumentException("Drawable runs require finite nonnegative geometry.", nameof(runs));
                objects.Add(new ObjectPlacement(visual, start, drawable));
                text.Append('\ufffc');
                for (var i = 0; i < run.Length; i++) _visualToDisplay[visual + i] = start;
                visual += run.Length; reverse.Add(visual);
                spans.Add(ToStyle(properties, start, 1) with { ObjectWidth = (float)box.Width, ObjectHeight = (float)box.Height, ObjectBaseline = (float)Math.Clamp(drawable.Baseline, 0, box.Height) });
            }
            else
            {
                if (run.Text.Length != run.Length) throw new ArgumentException("A text run's UTF-16 text and length must agree.", nameof(runs));
                text.Append(run.Text.Span);
                for (var i = 0; i < run.Length; i++) { _visualToDisplay[visual] = start + i; reverse.Add(++visual); }
                spans.Add(ToStyle(properties, start, run.Length));
            }
        }
        _visualToDisplay[visual] = text.Length;
        _displayToVisual = reverse.ToArray(); _display = text.ToString();
        _boundaries = StringInfo.ParseCombiningCharacters(_display);
        Objects = objects.AsReadOnly();
        Layout = new TextLineLayout(_display, style, width, spans, minimumHeight);
        Lines = Layout.Rows.Select(row => new TextLine(this, row)).ToArray();
    }

    internal static TextStyleSpan ToStyle(TextRunProperties properties, int start, int length)
    {
        return new TextStyleSpan(start, length)
        {
            FontFamily = properties.FontFamily?.Source ?? "monospace",
            FontSize = (float)properties.FontRenderingEmSize,
            FontWeight = properties.FontWeight.Weight,
            Italic = properties.FontStyle != FontStyle.Normal,
            Foreground = SolidColor(properties.ForegroundBrush), Background = SolidColor(properties.BackgroundBrush),
            Underline = properties.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false,
            Strikethrough = properties.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) ?? false
        };
    }
    internal static SKColor? SolidColor(Brush brush)
    {
        if (brush == null) return null;
        if (brush is not SolidColorBrush solid) throw new NotSupportedException("Native shaped-run colors currently require a SolidColorBrush.");
        var c = solid.Color;
        return new SKColor(c.R, c.G, c.B, (byte)Math.Clamp(Math.Round(c.A * solid.Opacity), 0, 255));
    }
    internal int ToDisplay(int visual) => _visualToDisplay[Math.Clamp(visual, 0, Length)];
    internal int ToVisual(int display) => _displayToVisual[Math.Clamp(display, 0, _displayToVisual.Length - 1)];
    internal int HitTest(float x, float y) => ToVisual(Layout.HitTest(x, y));
    internal SKRect GetCaretRectangle(int visual) => Layout.GetCaretRectangle(ToDisplay(visual));
    internal IReadOnlyList<SKRect> GetRangeRectangles(int visual, int length) => Layout.GetRangeRectangles(ToDisplay(visual), ToDisplay(visual + length) - ToDisplay(visual));
    internal int PreviousBoundary(int visual)
    {
        var display = ToDisplay(visual); var i = Array.BinarySearch(_boundaries, display); i = i >= 0 ? i - 1 : ~i - 1;
        return ToVisual(i < 0 ? 0 : _boundaries[i]);
    }
    internal int NextBoundary(int visual)
    {
        var display = ToDisplay(visual); var i = Array.BinarySearch(_boundaries, display); i = i >= 0 ? i + 1 : ~i;
        return ToVisual(i >= _boundaries.Length ? _display.Length : _boundaries[i]);
    }
    internal int CountTrailingWhitespace(TextLine line)
    {
        var end = line.Row.End;
        while (end > line.Row.Start && char.IsWhiteSpace(_display[end - 1])) end--;
        return line.Length - (ToVisual(end) - line.FirstTextSourceIndex);
    }
    internal Rect GetObjectRectangle(ObjectPlacement value)
    {
        var row = Layout.GetRowByOffset(value.DisplayStart);
        var x = Layout.GetDistanceAtOffset(value.DisplayStart, row);
        return new Rect(x, row.Top + row.Baseline - value.Run.Baseline, value.Run.Size.Width, value.Run.Size.Height);
    }
    internal void Paint(SKCanvas canvas, float x, float y, int selectedStart, int selectedLength, SKColor? selection)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeTextLayout));
        Layout.Paint(canvas, x, y, ToDisplay(selectedStart), ToDisplay(selectedStart + selectedLength) - ToDisplay(selectedStart), selection);
        var context = new DrawingContext(canvas);
        foreach (var value in Objects)
        {
            if (value.Run is InlineObjectRun) continue; // Real child control is arranged by TextView, not rasterized twice.
            var box = GetObjectRectangle(value);
            value.Run.Draw(context, new Point(x + box.X, y + box.Y));
        }
    }
    internal void PaintRow(DrawingContext context, TextLine line, Point origin)
    {
        Layout.PaintRow(context.Canvas, line.Row, (float)origin.X, (float)origin.Y);
        foreach (var value in Objects)
        {
            if (value.DisplayStart < line.Row.Start || value.DisplayStart >= line.Row.End || value.Run is InlineObjectRun) continue;
            var box = GetObjectRectangle(value);
            value.Run.Draw(context, new Point(origin.X + box.X, origin.Y + box.Y - line.Row.Top));
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Layout.Dispose();
        foreach (var element in Objects.Select(o => (o.Run as FormattedTextRun)?.Element).Where(e => e?.OwnsTextLine == true).Distinct())
            element.TextLine?.Dispose();
    }
}
