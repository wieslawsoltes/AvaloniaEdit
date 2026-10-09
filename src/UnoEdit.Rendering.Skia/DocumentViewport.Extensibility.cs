using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;

namespace UnoEdit.Rendering.Skia;

public enum ViewportRenderLayer { Background, Text, Caret }
public sealed class ViewportRenderEventArgs : EventArgs
{
    internal ViewportRenderEventArgs(SKCanvas canvas, ViewportRenderLayer layer) { Canvas = canvas; Layer = layer; }
    /// <summary>Borrowed canvas, valid only during the synchronous callback.</summary>
    public SKCanvas Canvas { get; }
    public ViewportRenderLayer Layer { get; }
}
public sealed partial class DocumentViewport
{
    public event EventHandler<ViewportRenderEventArgs> RenderingLayer;
    public event EventHandler HeightIndexReset;
    public double ViewportWidth => _width;
    public double ViewportHeight => _height;
    public double GetVisualTop(DocumentLine line) { ThrowIfDisposed(); return _heights.GetVisualPosition(line); }
    public double GetLineHeight(DocumentLine line) { ThrowIfDisposed(); return _heights.GetHeight(line); }
    public DocumentLine GetLineByVisualPosition(double position) { ThrowIfDisposed(); return _heights.GetLineByVisualPosition(position); }
    public bool IsLineCollapsed(int number) { ThrowIfDisposed(); return _heights.GetIsCollapsed(number); }
    public CollapsedLineSection CollapseLines(DocumentLine first, DocumentLine last)
    { ThrowIfDisposed(); var section = _heights.CollapseText(first, last); Invalidated?.Invoke(this, EventArgs.Empty); return section; }
    public IReadOnlyList<DocumentLine> GetVisibleDocumentLines()
    {
        ThrowIfDisposed();
        var result = new List<DocumentLine>();
        if (_height <= 0) return result;
        var line = _heights.GetLineByVisualPosition(_verticalOffset);
        while (line != null && _heights.GetVisualPosition(line) < _verticalOffset + _height)
        {
            if (!_heights.GetIsCollapsed(line.LineNumber)) result.Add(line);
            var nextY = _heights.GetVisualPosition(line) + _heights.GetHeight(line);
            if (nextY >= _heights.TotalHeight) break;
            var next = _heights.GetLineByVisualPosition(nextY + 0.001);
            if (ReferenceEquals(next, line)) break;
            line = next;
        }
        return result;
    }
    /// <summary>Returns only on-screen shaped geometry, never scanning hidden preceding lines.</summary>
    public IReadOnlyList<SKRect> GetSegmentRectangles(int offset, int length, bool extendLineEnds = false)
    {
        ThrowIfDisposed();
        if (offset < 0 || length < 0 || offset > _document.TextLength || length > _document.TextLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = new List<SKRect>(); var end = offset + length;
        foreach (var line in GetVisibleDocumentLines())
        {
            if (line.EndOffset < offset || line.Offset > end) continue;
            var from = Math.Clamp(offset - line.Offset, 0, line.Length);
            var to = Math.Clamp(end - line.Offset, 0, line.Length);
            if (from == to && length > 0 && end <= line.Offset) continue;
            var layout = GetLayout(line);
            var x = (float)(GutterWidth - _horizontalOffset); var y = (float)(_heights.GetVisualPosition(line) - _verticalOffset);
            foreach (var value in layout.GetRangeRectangles(from, Math.Max(0, to - from)))
            { var rect = value; rect.Offset(x, y); result.Add(rect); }
            if (end > line.EndOffset && line.DelimiterLength != 0)
            {
                var caret = layout.GetCaretRectangle(line.Length); caret.Offset(x, y);
                result.Add(new SKRect(caret.Left, caret.Top, extendLineEnds ? (float)_width : caret.Left + _style.FontSize * 0.65f, caret.Bottom));
            }
        }
        return result;
    }
    public void InvalidateDrawing() { ThrowIfDisposed(); Invalidated?.Invoke(this, EventArgs.Empty); }
    private void RaiseRenderLayer(SKCanvas canvas, ViewportRenderLayer layer)
    {
        if (RenderingLayer == null) return;
        var count = canvas.Save();
        try { RenderingLayer(this, new ViewportRenderEventArgs(canvas, layer)); }
        finally { canvas.RestoreToCount(count); }
    }
}
