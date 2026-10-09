using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Search;

namespace UnoEdit.Rendering.Skia;

/// <summary>A background marker using line-relative UTF-16 coordinates.</summary>
public sealed record TextRangeMarker(int Start, int Length, SKColor Color, bool IncludesLineBreak = false);

/// <summary>Supplies viewport-local markers independently of syntax and shaping.</summary>
public interface ILineMarkerSource
{
    TextDocument Document { get; }
    IReadOnlyList<TextRangeMarker> GetMarkers(DocumentLine line);
    event EventHandler MarkersChanged;
}

/// <summary>Optional indexed range lookup used by folded or otherwise projected visual lines.</summary>
public interface IDocumentRangeMarkerSource : ILineMarkerSource
{
    IReadOnlyList<TextRangeMarker> GetMarkers(int offset, int length, bool firstMatchOnly);
}

/// <summary>
/// Indexed adapter from search results to visible-line markers. Paint never
/// executes a search; invalidation immediately drops stale result coordinates.
/// </summary>
public sealed class SearchResultMarkerSource : IDocumentRangeMarkerSource, IDisposable
{
    private readonly SearchSession _search;
    private IReadOnlyList<ISearchResult> _results = Array.Empty<ISearchResult>();
    private SKColor _color = new SKColor(255, 193, 7, 110);
    private bool _disposed;

    public SearchResultMarkerSource(SearchSession search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _search.ResultsChanged += OnResultsChanged;
        _search.ResultsInvalidated += OnInvalidated;
        _results = _search.Results;
    }
    public TextDocument Document => _search.Document;
    public SKColor Color
    {
        get => _color;
        set { if (_disposed) throw new ObjectDisposedException(nameof(SearchResultMarkerSource)); _color = value; MarkersChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler MarkersChanged;
    public IReadOnlyList<TextRangeMarker> GetMarkers(DocumentLine line)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SearchResultMarkerSource));
        if (line == null) throw new ArgumentNullException(nameof(line));
        if (line.IsDeleted || !Document.Lines.Contains(line)) throw new ArgumentException("Line belongs to another document.", nameof(line));
        var low = 0;
        var high = _results.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_results[middle].EndOffset < line.Offset) low = middle + 1;
            else high = middle;
        }
        List<TextRangeMarker> markers = null;
        for (var i = low; i < _results.Count && _results[i].Offset <= line.EndOffset; i++)
        {
            var result = _results[i];
            var from = Math.Max(result.Offset, line.Offset);
            var to = Math.Min(result.EndOffset, line.EndOffset);
            var newline = line.DelimiterLength != 0 && result.Offset < line.EndOffset + line.DelimiterLength && result.EndOffset > line.EndOffset;
            if (to <= from && result.Length != 0 && !newline) continue;
            markers ??= new List<TextRangeMarker>();
            markers.Add(new TextRangeMarker(Math.Clamp(from - line.Offset, 0, line.Length), Math.Max(0, to - from), _color, newline));
        }
        return markers == null ? Array.Empty<TextRangeMarker>() : markers;
    }
    public IReadOnlyList<TextRangeMarker> GetMarkers(int offset, int length, bool firstMatchOnly)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SearchResultMarkerSource));
        if (offset < 0 || length < 0 || offset > Document.TextLength || length > Document.TextLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        var end = offset + length; var low = 0; var high = _results.Count;
        while (low < high) { var middle = low + (high - low) / 2; if (_results[middle].EndOffset < offset) low = middle + 1; else high = middle; }
        List<TextRangeMarker> markers = null;
        for (var i = low; i < _results.Count && _results[i].Offset <= end; i++)
        {
            var result = _results[i]; var from = Math.Max(offset, result.Offset); var to = Math.Min(end, result.EndOffset);
            if (to <= from && result.Length != 0) continue;
            markers ??= new(); markers.Add(new TextRangeMarker(from - offset, Math.Max(0, to - from), _color));
            if (firstMatchOnly) break;
        }
        return markers == null ? Array.Empty<TextRangeMarker>() : markers;
    }
    private void OnResultsChanged(object sender, EventArgs args) { _results = _search.Results; MarkersChanged?.Invoke(this, EventArgs.Empty); }
    private void OnInvalidated(object sender, EventArgs args) { _results = Array.Empty<ISearchResult>(); MarkersChanged?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _search.ResultsChanged -= OnResultsChanged;
        _search.ResultsInvalidated -= OnInvalidated;
        _results = Array.Empty<ISearchResult>();
        MarkersChanged = null;
    }
}

public sealed partial class DocumentViewport
{
    private ILineMarkerSource _markerSource;
    /// <summary>Non-owning marker layer. Changing markers does not discard shaped lines.</summary>
    public ILineMarkerSource MarkerSource
    {
        get => _markerSource;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(value, _markerSource)) return;
            if (value != null && !ReferenceEquals(value.Document, _document)) throw new ArgumentException("Marker source belongs to another document.", nameof(value));
            DetachMarkerSource();
            _markerSource = value;
            if (value != null) value.MarkersChanged += OnMarkersChanged;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }
    public int LastMarkerCount { get; private set; }
    private void OnMarkersChanged(object sender, EventArgs args) => Invalidated?.Invoke(this, EventArgs.Empty);
    private void DetachMarkerSource()
    {
        if (_markerSource != null) _markerSource.MarkersChanged -= OnMarkersChanged;
        _markerSource = null;
    }
    private void PaintLineMarkers(SKCanvas canvas, DocumentLine line, DocumentLine lastLine, ITextLineLayout layout, float x, float y)
    {
        if (_markerSource == null) return;
        using var paint = new SKPaint { IsAntialias = false };
        if (layout is IProjectedTextLineLayout projected && _markerSource is IDocumentRangeMarkerSource indexed)
        {
            foreach (var source in projected.SourceSegments)
                foreach (var marker in indexed.GetMarkers(line.Offset + source.Start, source.Length, source.IsAtomic))
                {
                    if (marker == null || marker.Start < 0 || marker.Length < 0 || marker.Start > source.Length || marker.Length > source.Length - marker.Start)
                        throw new InvalidOperationException("Marker source returned invalid projected coordinates.");
                    Draw(source.Start + marker.Start, marker.Length, marker.Color);
                }
            foreach (var marker in _markerSource.GetMarkers(lastLine))
                if (marker.IncludesLineBreak) DrawDelimiter(marker.Color);
            return;
        }
        // Generic line providers are asked only about document lines with a
        // visible representation. Never enumerate an entire collapsed region.
        var numbers = new SortedSet<int> { line.LineNumber, lastLine.LineNumber };
        if (layout is IProjectedTextLineLayout visible)
            foreach (var source in visible.SourceSegments)
                numbers.Add(_document.GetLineByOffset(line.Offset + source.Start).LineNumber);
        foreach (var number in numbers)
        {
            var current = _document.GetLineByNumber(number);
            foreach (var marker in _markerSource.GetMarkers(current))
            {
                if (marker == null || marker.Start < 0 || marker.Length < 0 || marker.Start > current.Length || marker.Length > current.Length - marker.Start)
                    throw new InvalidOperationException("Marker source returned invalid line-relative coordinates.");
                Draw(current.Offset - line.Offset + marker.Start, marker.Length, marker.Color);
                if (marker.IncludesLineBreak && ReferenceEquals(current, lastLine)) DrawDelimiter(marker.Color);
            }
        }
        void Draw(int start, int length, SKColor color)
        {
            paint.Color = color;
            foreach (var range in layout.GetRangeRectangles(start, length))
            { var rectangle = range; rectangle.Offset(x, y); canvas.DrawRect(rectangle, paint); }
            LastMarkerCount++;
        }
        void DrawDelimiter(SKColor color)
        {
            paint.Color = color;
            var caret = layout.GetCaretRectangle(layout.Length);
            canvas.DrawRect(x + caret.Left, y + caret.Top, _style.FontSize * 0.65f, Math.Max(1, caret.Height), paint);
        }
    }
}
