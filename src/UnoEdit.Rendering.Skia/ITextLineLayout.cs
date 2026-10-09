using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;

namespace UnoEdit.Rendering.Skia;

/// <summary>A shaped, document-relative line. Implementations may project several document lines.</summary>
public interface ITextLineLayout : IDisposable
{
    int Length { get; }
    float Width { get; }
    float Height { get; }
    int HitTest(float x, float y);
    SKRect GetCaretRectangle(int offset);
    IReadOnlyList<SKRect> GetRangeRectangles(int start, int length);
    void Paint(SKCanvas canvas, float x, float y, int selectionStart = 0, int selectionLength = 0, SKColor? selectionColor = null);
}

public sealed record ProjectedSourceSegment(int Start, int Length, bool IsAtomic);
/// <summary>Source intervals represented by a projected line. Atomic intervals need only one marker hit.</summary>
public interface IProjectedTextLineLayout
{
    IReadOnlyList<ProjectedSourceSegment> SourceSegments { get; }
}

/// <summary>Document projection supplied by a visual-line generator/transformer pipeline.</summary>
public interface IDocumentLineLayoutSource
{
    TextDocument Document { get; }
    DocumentLine ResolveFirstLine(DocumentLine line);
    ITextLineLayout CreateLayout(DocumentLine firstLine, TextViewStyle style, float? wrapWidth, ILineStyleSource styles);
    DocumentLine GetLastLine(DocumentLine firstLine, ITextLineLayout layout);
    event EventHandler<LineStylesChangedEventArgs> LayoutsChanged;
}

public sealed partial class DocumentViewport
{
    private IDocumentLineLayoutSource _layoutSource;
    /// <summary>The non-owning projected-line source. Null uses ordinary shaped document text.</summary>
    public IDocumentLineLayoutSource LineLayoutSource
    {
        get => _layoutSource;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(value, _layoutSource)) return;
            if (value != null && !ReferenceEquals(value.Document, _document)) throw new ArgumentException("Layout source belongs to another document.", nameof(value));
            if (_layoutSource != null) _layoutSource.LayoutsChanged -= OnLineStylesChanged;
            _layoutSource = value;
            if (value != null) value.LayoutsChanged += OnLineStylesChanged;
            ClearCache(); ResetHeightEstimates(); Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }
    public ITextLineLayout GetLineLayout(DocumentLine line)
    {
        ThrowIfDisposed();
        if (line == null || line.IsDeleted || !_document.Lines.Contains(line)) throw new ArgumentException("Invalid document line.", nameof(line));
        return GetLayout(ResolveLine(line));
    }
    public ITextLineLayout FindCachedLineLayout(int lineNumber)
    {
        ThrowIfDisposed();
        foreach (var entry in _lru)
            if (!entry.Line.IsDeleted && !entry.LastLine.IsDeleted && entry.Line.LineNumber <= lineNumber && entry.LastLine.LineNumber >= lineNumber) return entry.Layout;
        return null;
    }
    private DocumentLine ResolveLine(DocumentLine line) => _layoutSource?.ResolveFirstLine(line) ?? line;
    private DocumentLine LastLine(DocumentLine first, ITextLineLayout layout) => _layoutSource?.GetLastLine(first, layout) ?? first;
    private void DetachLayoutSource()
    {
        if (_layoutSource != null) _layoutSource.LayoutsChanged -= OnLineStylesChanged;
        _layoutSource = null;
    }
}
