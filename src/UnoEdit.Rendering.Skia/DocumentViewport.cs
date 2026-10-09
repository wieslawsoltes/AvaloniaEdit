using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.Rendering.Skia;

/// <summary>
/// A reusable, UI-independent viewport over the original TextDocument and height
/// tree. Only visible lines are shaped; an LRU cap bounds retained layouts.
/// Text, caret, selection and hit testing use the same shaped line objects.
/// </summary>
public sealed partial class DocumentViewport : IDisposable
{
    private sealed class Entry
    {
        internal DocumentLine Line;
        internal TextLineLayout Layout;
        internal float? WrapWidth;
    }

    private readonly Dictionary<DocumentLine, LinkedListNode<Entry>> _cache = new();
    private readonly LinkedList<Entry> _lru = new();
    private TextDocument _document;
    private DocumentHeightIndex _heights;
    private TextViewStyle _style = new();
    private double _width;
    private double _height;
    private double _horizontalOffset;
    private double _verticalOffset;
    private double _extentWidth;
    private bool _disposed;
    private bool _showLineNumbers = true;

    public DocumentViewport(TextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _heights = new DocumentHeightIndex(_document, DefaultLineHeight);
        _document.Changing += OnChanging;
        _document.Changed += OnChanged;
    }

    public TextDocument Document
    {
        get => _document;
        set
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(value, _document)) return;
            DetachLineStyleSource();
            DetachMarkerSource();
            _document.Changing -= OnChanging;
            _document.Changed -= OnChanged;
            ClearCache();
            _heights.Dispose();
            _document = value;
            _heights = new DocumentHeightIndex(_document, DefaultLineHeight);
            _document.Changing += OnChanging;
            _document.Changed += OnChanged;
            _horizontalOffset = _verticalOffset = _extentWidth = 0;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public TextViewStyle Style
    {
        get => _style;
        set
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!float.IsFinite(value.FontSize) || value.FontSize <= 0 || value.TabSize < 1 || value.TabSize > 256)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_style == value) return;
            var geometryChanged = _style.FontFamily != value.FontFamily || _style.FontSize != value.FontSize || _style.TabSize != value.TabSize || _style.WordWrap != value.WordWrap;
            _style = value;
            ClearCache();
            if (geometryChanged) ResetHeightEstimates();
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers == value) return;
            _showLineNumbers = value;
            if (_style.WordWrap) { ClearCache(); ResetHeightEstimates(); }
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public const int CacheCapacity = 256;
    public int CachedLineCount => _cache.Count;
    public long LayoutCreationCount { get; private set; }
    public long RenderCount { get; private set; }
    public int LastVisibleLineCount { get; private set; }
    public double DefaultLineHeight => Math.Ceiling(_style.FontSize * 1.4);
    public double GutterWidth => ShowLineNumbers ? (Math.Max(2, _document.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) + 2) * _style.FontSize * 0.65 : 8;
    public double ExtentHeight => _heights.TotalHeight;
    public double ExtentWidth => Math.Max(_width, _extentWidth + GutterWidth + 16);
    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    public event EventHandler Invalidated;

    public void SetViewport(double width, double height)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (_width == width && _height == height) return;
        var widthChanged = _width != width;
        _width = width;
        _height = height;
        if (widthChanged && _style.WordWrap) { ClearCache(); ResetHeightEstimates(); }
        ClampOffsets();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ScrollTo(double horizontalOffset, double verticalOffset)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(horizontalOffset) || !double.IsFinite(verticalOffset)) throw new ArgumentOutOfRangeException(nameof(verticalOffset));
        var oldX = _horizontalOffset;
        var oldY = _verticalOffset;
        _horizontalOffset = horizontalOffset;
        _verticalOffset = verticalOffset;
        ClampOffsets();
        if (_horizontalOffset != oldX || _verticalOffset != oldY) Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ScrollToLine(int lineNumber)
    {
        ThrowIfDisposed();
        ScrollTo(_horizontalOffset, _heights.GetVisualPosition(_document.GetLineByNumber(lineNumber)));
    }

    public SKRect GetCaretRectangle(int offset)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset > _document.TextLength) throw new ArgumentOutOfRangeException(nameof(offset));
        var line = _document.GetLineByOffset(offset);
        var layout = GetLayout(line);
        var rectangle = layout.GetCaretRectangle(Math.Min(offset - line.Offset, line.Length));
        rectangle.Offset((float)(GutterWidth - _horizontalOffset), (float)(_heights.GetVisualPosition(line) - _verticalOffset));
        return rectangle;
    }

    public int HitTest(double x, double y)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var line = _heights.GetLineByVisualPosition(Math.Max(0, y + _verticalOffset));
        var layout = GetLayout(line);
        return line.Offset + layout.HitTest((float)(x + _horizontalOffset - GutterWidth), (float)(y + _verticalOffset - _heights.GetVisualPosition(line)));
    }

    public void EnsureCaretVisible(int offset)
    {
        var rectangle = GetCaretRectangle(offset);
        var horizontal = _horizontalOffset;
        var vertical = _verticalOffset;
        if (rectangle.Left < GutterWidth) horizontal += rectangle.Left - GutterWidth;
        else if (rectangle.Right > _width - 8) horizontal += rectangle.Right - _width + 8;
        if (rectangle.Top < 0) vertical += rectangle.Top;
        else if (rectangle.Bottom > _height) vertical += rectangle.Bottom - _height;
        ScrollTo(horizontal, vertical);
    }

    public void Render(SKCanvas canvas, EditorSession session = null, bool drawCaret = true)
    {
        ThrowIfDisposed();
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        if (session != null && !ReferenceEquals(session.Document, _document)) throw new ArgumentException("Session belongs to another document.", nameof(session));
        using var paint = new SKPaint { IsAntialias = true, Color = _style.Background };
        using var numberFont = new SKFont(SKTypeface.Default, _style.FontSize);
        var savedCanvasCount = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, (float)_width, (float)_height));
            canvas.DrawRect(0, 0, (float)_width, (float)_height, paint);
            RaiseRenderLayer(canvas, ViewportRenderLayer.Background);
            LastVisibleLineCount = 0;
            LastMarkerCount = 0;
            if (_height <= 0 || _width <= 0) return;
            ClampOffsets();
            var line = _heights.GetLineByVisualPosition(_verticalOffset);
            var top = _heights.GetVisualPosition(line) - _verticalOffset;
            var caretLine = session == null ? null : _document.GetLineByOffset(session.CaretOffset);
            while (line != null && top < _height)
            {
                var layout = GetLayout(line);
                var rowHeight = _heights.GetHeight(line);
                if (ReferenceEquals(line, caretLine))
                {
                    paint.Color = _style.CurrentLine;
                    canvas.DrawRect(0, (float)top, (float)_width, (float)rowHeight, paint);
                }
                var from = session == null ? 0 : Math.Clamp(session.SelectionStart - line.Offset, 0, line.Length);
                var to = session == null ? 0 : Math.Clamp(session.SelectionStart + session.SelectionLength - line.Offset, 0, line.Length);
                canvas.Save();
                canvas.ClipRect(new SKRect((float)GutterWidth, 0, (float)_width, (float)_height));
                PaintLineMarkers(canvas, line, layout, (float)(GutterWidth - _horizontalOffset), (float)top);
                layout.Paint(canvas, (float)(GutterWidth - _horizontalOffset), (float)top, from, Math.Max(0, to - from), _style.Selection);
                if (session != null && session.SelectionStart <= line.EndOffset && session.SelectionStart + session.SelectionLength > line.EndOffset && line.DelimiterLength != 0)
                {
                    var end = layout.GetCaretRectangle(line.Length);
                    paint.Color = _style.Selection;
                    canvas.DrawRect((float)(GutterWidth - _horizontalOffset) + end.Left, (float)top + end.Top, _style.FontSize * 0.65f, Math.Max(1, end.Height), paint);
                }
                canvas.Restore();
                if (ShowLineNumbers)
                {
                    paint.Color = _style.LineNumber;
                    var number = line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    canvas.DrawText(number, (float)GutterWidth - numberFont.MeasureText(number) - 8, (float)top + _style.FontSize, numberFont, paint);
                }
                LastVisibleLineCount++;
                var nextPosition = _heights.GetVisualPosition(line) + rowHeight;
                if (nextPosition >= _heights.TotalHeight) break;
                var next = _heights.GetLineByVisualPosition(nextPosition + 0.001);
                if (ReferenceEquals(next, line)) break;
                line = next;
                top = _heights.GetVisualPosition(line) - _verticalOffset;
            }
            RaiseRenderLayer(canvas, ViewportRenderLayer.Text);
            if (drawCaret && session != null)
            {
                var caret = GetCaretRectangle(session.CaretOffset);
                paint.Color = _style.Foreground;
                canvas.DrawRect(caret.Left, caret.Top, Math.Max(1, _style.FontSize / 14), Math.Max(1, caret.Height), paint);
            }
            RaiseRenderLayer(canvas, ViewportRenderLayer.Caret);
        }
        finally
        {
            canvas.RestoreToCount(savedCanvasCount);
            RenderCount++;
        }
    }

    private TextLineLayout GetLayout(DocumentLine line)
    {
        var wrapWidth = _style.WordWrap ? (float?)Math.Max(1, _width - GutterWidth - 8) : null;
        if (_cache.TryGetValue(line, out var node))
        {
            if (node.Value.WrapWidth == wrapWidth)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Layout;
            }
            Remove(node);
        }
        var layout = new TextLineLayout(_document.GetText(line.Offset, line.Length), _style, wrapWidth, GetLineStyles(line));
        LayoutCreationCount++;
        _heights.SetHeight(line, Math.Max(DefaultLineHeight, layout.Height));
        _extentWidth = Math.Max(_extentWidth, layout.Width);
        var entry = new Entry { Line = line, Layout = layout, WrapWidth = wrapWidth };
        _cache.Add(line, _lru.AddFirst(entry));
        while (_cache.Count > CacheCapacity) Remove(_lru.Last);
        return layout;
    }

    private void OnChanging(object sender, DocumentChangeEventArgs e)
    {
        // Only visit the bounded cache, not every line covered by a large edit.
        var last = e.Offset + e.RemovalLength;
        for (var node = _lru.First; node != null;)
        {
            var next = node.Next;
            var line = node.Value.Line;
            if (line.IsDeleted || line.Offset <= last && line.EndOffset >= e.Offset)
            {
                if (!line.IsDeleted) _heights.SetHeight(line, DefaultLineHeight);
                Remove(node);
            }
            node = next;
        }
    }

    private void OnChanged(object sender, DocumentChangeEventArgs e)
    {
        ClampOffsets();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ResetHeightEstimates()
    {
        _heights.Dispose();
        _heights = new DocumentHeightIndex(_document, DefaultLineHeight);
        HeightIndexReset?.Invoke(this, EventArgs.Empty);
        _extentWidth = 0;
        ClampOffsets();
    }

    private void ClampOffsets()
    {
        _horizontalOffset = _style.WordWrap ? 0 : Math.Clamp(_horizontalOffset, 0, Math.Max(0, ExtentWidth - _width));
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, ExtentHeight - _height));
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _cache.Remove(node.Value.Line);
        _lru.Remove(node);
        node.Value.Layout.Dispose();
    }

    private void ClearCache()
    {
        while (_lru.Last != null) Remove(_lru.Last);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DocumentViewport));
    }

    public void Dispose()
    {
        if (_disposed) return;
        DetachLineStyleSource();
        DetachMarkerSource();
        _document.Changing -= OnChanging;
        _document.Changed -= OnChanged;
        ClearCache();
        _heights.Dispose();
        _disposed = true;
        Invalidated = null;
        RenderingLayer = null;
        HeightIndexReset = null;
    }
}
