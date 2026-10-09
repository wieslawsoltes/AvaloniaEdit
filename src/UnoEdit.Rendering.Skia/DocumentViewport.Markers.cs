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

/// <summary>
/// Indexed adapter from search results to visible-line markers. Paint never
/// executes a search; invalidation immediately drops stale result coordinates.
/// </summary>
public sealed class SearchResultMarkerSource : ILineMarkerSource, IDisposable
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
    private void PaintLineMarkers(SKCanvas canvas, DocumentLine line, TextLineLayout layout, float x, float y)
    {
        if (_markerSource == null) return;
        using var paint = new SKPaint { IsAntialias = false };
        foreach (var marker in _markerSource.GetMarkers(line))
        {
            if (marker == null || marker.Start < 0 || marker.Length < 0 || marker.Start > line.Length || marker.Length > line.Length - marker.Start)
                throw new InvalidOperationException("Marker source returned invalid line-relative coordinates.");
            paint.Color = marker.Color;
            foreach (var range in layout.GetRangeRectangles(marker.Start, marker.Length))
            {
                var rectangle = range;
                rectangle.Offset(x, y);
                canvas.DrawRect(rectangle, paint);
            }
            if (marker.IncludesLineBreak)
            {
                var caret = layout.GetCaretRectangle(line.Length);
                canvas.DrawRect(x + caret.Left, y + caret.Top, _style.FontSize * 0.65f, Math.Max(1, caret.Height), paint);
            }
            LastMarkerCount++;
        }
    }
}
