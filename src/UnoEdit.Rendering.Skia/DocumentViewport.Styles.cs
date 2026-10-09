using System;
using System.Collections.Generic;
using UnoEdit.Document;

namespace UnoEdit.Rendering.Skia;

public sealed partial class DocumentViewport
{
    private ILineStyleSource _lineStyleSource;

    /// <summary>
    /// Optional document-bound source of syntax styles. Setting it does not
    /// transfer ownership. The viewport detaches it when its document changes.
    /// </summary>
    public ILineStyleSource LineStyleSource
    {
        get => _lineStyleSource;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_lineStyleSource, value)) return;
            if (value != null && !ReferenceEquals(value.Document, _document))
                throw new ArgumentException("Style source belongs to another document.", nameof(value));
            if (_lineStyleSource != null) _lineStyleSource.StylesChanged -= OnLineStylesChanged;
            _lineStyleSource = value;
            if (_lineStyleSource != null) _lineStyleSource.StylesChanged += OnLineStylesChanged;
            ClearCache();
            ResetHeightEstimates();
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void InvalidateLineStyles(int firstLine, int lastLine)
    {
        ThrowIfDisposed();
        if (firstLine < 1 || lastLine < firstLine) throw new ArgumentOutOfRangeException(nameof(firstLine));
        for (var node = _lru.First; node != null;)
        {
            var next = node.Next;
            var line = node.Value.Line;
            if (line.IsDeleted || node.Value.LastLine.LineNumber >= firstLine && line.LineNumber <= lastLine)
            {
                if (!line.IsDeleted) _heights.SetHeight(line, DefaultLineHeight);
                Remove(node);
            }
            node = next;
        }
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<TextStyleSpan> GetLineStyles(DocumentLine line) => _lineStyleSource?.GetStyles(line);
    private void OnLineStylesChanged(object sender, LineStylesChangedEventArgs e) => InvalidateLineStyles(e.FirstLine, e.LastLine);
    private void DetachLineStyleSource()
    {
        if (_lineStyleSource != null) _lineStyleSource.StylesChanged -= OnLineStylesChanged;
        _lineStyleSource = null;
    }
}
