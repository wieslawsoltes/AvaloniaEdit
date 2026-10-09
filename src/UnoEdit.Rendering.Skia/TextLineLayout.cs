using System;
using System.Collections.Generic;
using System.Text;
using SkiaSharp;
using Topten.RichTextKit;

namespace UnoEdit.Rendering.Skia;

/// <summary>Immutable rendering settings. A change invalidates cached layouts.</summary>
public sealed record TextViewStyle
{
    public string FontFamily { get; init; } = "monospace";
    public float FontSize { get; init; } = 14;
    public int TabSize { get; init; } = 4;
    public bool WordWrap { get; init; }
    public SKColor Foreground { get; init; } = SKColors.Black;
    public SKColor Background { get; init; } = SKColors.White;
    public SKColor Selection { get; init; } = new SKColor(51, 119, 207, 100);
    public SKColor CurrentLine { get; init; } = new SKColor(128, 128, 128, 20);
    public SKColor LineNumber { get; init; } = new SKColor(110, 110, 110);
}

/// <summary>
/// Shapes a document line, including its syntax styles, through RichTextKit and
/// HarfBuzz. All public offsets are UTF-16; the backend uses scalar indexes.
/// Text, background, decorations, selections and caret geometry share one layout.
/// </summary>
public sealed class TextLineLayout : IDisposable
{
    private readonly TextBlock _block;
    private readonly int[] _utf16ToScalar;
    private readonly int[] _scalarToUtf16;
    private bool _disposed;

    public TextLineLayout(string text, TextViewStyle style, float? wrapWidth = null)
        : this(text, style, wrapWidth, null) { }

    public TextLineLayout(string text, TextViewStyle style, float? wrapWidth, IReadOnlyList<TextStyleSpan> spans)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (style == null) throw new ArgumentNullException(nameof(style));
        if (!float.IsFinite(style.FontSize) || style.FontSize <= 0) throw new ArgumentOutOfRangeException(nameof(style));
        if (style.TabSize < 1 || style.TabSize > 256) throw new ArgumentOutOfRangeException(nameof(style));
        if (wrapWidth.HasValue && (!float.IsFinite(wrapWidth.Value) || wrapWidth.Value <= 0)) throw new ArgumentOutOfRangeException(nameof(wrapWidth));
        ValidateSpans(text, spans);
        Length = text.Length;
        _utf16ToScalar = new int[text.Length + 1];
        var expandedOffsets = spans == null || spans.Count == 0 ? null : new int[text.Length + 1];
        var reverse = new List<int>(text.Length + 1) { 0 };
        var expanded = new StringBuilder(text.Length);
        var scalar = 0;
        var column = 0;
        for (var i = 0; i < text.Length;)
        {
            _utf16ToScalar[i] = scalar;
            if (expandedOffsets != null) expandedOffsets[i] = expanded.Length;
            if (text[i] == '\t')
            {
                var spaces = style.TabSize - column % style.TabSize;
                expanded.Append(' ', spaces);
                for (var s = 1; s <= spaces; s++) reverse.Add(s * 2 < spaces ? i : i + 1);
                scalar += spaces;
                column += spaces;
                i++;
            }
            else
            {
                var length = i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]) ? 2 : 1;
                if (length == 2)
                {
                    _utf16ToScalar[i + 1] = scalar;
                    if (expandedOffsets != null) expandedOffsets[i + 1] = expanded.Length;
                }
                expanded.Append(text, i, length);
                scalar++;
                column++;
                i += length;
                reverse.Add(i);
            }
        }
        _utf16ToScalar[text.Length] = scalar;
        _scalarToUtf16 = reverse.ToArray();
        if (expandedOffsets != null) expandedOffsets[text.Length] = expanded.Length;
        _block = new TextBlock { MaxWidth = wrapWidth, EllipsisEnabled = false };
        var defaultStyle = CreateStyle(style, null);
        try
        {
            if (expandedOffsets == null || text.Length == 0)
            {
                _block.AddText(expanded.Length == 0 ? "\n" : expanded.ToString(), defaultStyle);
                StyleRunCount = 1;
            }
            else
            {
                var position = 0;
                foreach (var span in spans)
                {
                    if (span.Start > position) AppendRun(position, span.Start, defaultStyle);
                    if (span.Length > 0) AppendRun(span.Start, span.Start + span.Length, CreateStyle(style, span));
                    position = span.Start + span.Length;
                }
                if (position < text.Length) AppendRun(position, text.Length, defaultStyle);
            }
            _block.Layout();
            Width = _block.MeasuredWidth;
            Height = Math.Max(style.FontSize, _block.MeasuredHeight);
        }
        catch { _block.Clear(); throw; }

        void AppendRun(int from, int to, Style runStyle)
        {
            var start = expandedOffsets[from];
            var length = expandedOffsets[to] - start;
            if (length == 0) return;
            _block.AddText(expanded.ToString(start, length), runStyle);
            StyleRunCount++;
        }
    }

    public int Length { get; }
    public float Width { get; }
    public float Height { get; }
    public int StyleRunCount { get; private set; }

    private static Style CreateStyle(TextViewStyle basis, TextStyleSpan span) => new()
    {
        FontFamily = span?.FontFamily ?? basis.FontFamily,
        FontSize = span?.FontSize ?? basis.FontSize,
        FontWeight = span?.FontWeight ?? 400,
        FontItalic = span?.Italic ?? false,
        TextColor = span?.Foreground ?? basis.Foreground,
        BackgroundColor = span?.Background ?? SKColors.Transparent,
        Underline = span?.Underline == true ? UnderlineStyle.Solid : UnderlineStyle.None,
        StrikeThrough = span?.Strikethrough == true ? StrikeThroughStyle.Solid : StrikeThroughStyle.None
    };

    private static void ValidateSpans(string text, IReadOnlyList<TextStyleSpan> spans)
    {
        if (spans == null) return;
        var previousEnd = 0;
        foreach (var span in spans)
        {
            if (span == null || span.Start < previousEnd || span.Start > text.Length || span.Length < 0 || span.Length > text.Length - span.Start)
                throw new ArgumentException("Style runs must be sorted, non-overlapping and within the line.", nameof(spans));
            var end = span.Start + span.Length;
            if (IsSurrogateInterior(span.Start) || IsSurrogateInterior(end))
                throw new ArgumentException("Style runs must not split a UTF-16 surrogate pair.", nameof(spans));
            if (span.FontSize is float size && (!float.IsFinite(size) || size <= 0) || span.FontWeight is int weight && (weight < 1 || weight > 1000))
                throw new ArgumentException("Style font size/weight is outside its supported range.", nameof(spans));
            previousEnd = end;
        }
        bool IsSurrogateInterior(int offset) => offset > 0 && offset < text.Length && char.IsSurrogatePair(text[offset - 1], text[offset]);
    }

    public int GetScalarIndex(int utf16Offset)
    {
        ThrowIfDisposed();
        if (utf16Offset < 0 || utf16Offset > Length) throw new ArgumentOutOfRangeException(nameof(utf16Offset));
        return _utf16ToScalar[utf16Offset];
    }

    public int GetUtf16Offset(int scalarIndex)
    {
        ThrowIfDisposed();
        if (scalarIndex < 0 || scalarIndex >= _scalarToUtf16.Length) throw new ArgumentOutOfRangeException(nameof(scalarIndex));
        return _scalarToUtf16[scalarIndex];
    }

    public int HitTest(float x, float y)
    {
        ThrowIfDisposed();
        if (Length == 0) return 0;
        var hit = _block.HitTest(x, y);
        return _scalarToUtf16[Math.Clamp(hit.ClosestCodePointIndex, 0, _scalarToUtf16.Length - 1)];
    }

    /// <summary>
    /// Range geometry split across actual shaped font runs. Wrapped and bidi
    /// text can produce several rectangles; logical endpoints are not assumed
    /// to describe one left-to-right rectangle.
    /// </summary>
    public IReadOnlyList<SKRect> GetRangeRectangles(int start, int length)
    {
        ThrowIfDisposed();
        if (start < 0 || start > Length || length < 0 || length > Length - start)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
        {
            var caret = GetCaretRectangle(start);
            return new[] { new SKRect(caret.Left, caret.Top, caret.Left + 2, Math.Max(caret.Top + 1, caret.Bottom)) };
        }
        var from = GetScalarIndex(start);
        var to = GetScalarIndex(start + length);
        var rectangles = new List<SKRect>();
        foreach (var run in _block.FontRuns)
        {
            var first = Math.Max(from, run.Start);
            var last = Math.Min(to, run.End);
            if (last <= first) continue;
            var x1 = run.GetXCoordOfCodePointIndex(first);
            var x2 = run.GetXCoordOfCodePointIndex(last);
            rectangles.Add(new SKRect(Math.Min(x1, x2), run.Line.YCoord,
                Math.Max(x1, x2), run.Line.YCoord + run.Line.Height));
        }
        return rectangles;
    }

    public SKRect GetCaretRectangle(int utf16Offset)
    {
        var index = GetScalarIndex(utf16Offset);
        var info = _block.GetCaretInfo(new CaretPosition(index));
        return info.IsNone ? new SKRect(0, 0, 1, Height) : info.CaretRectangle;
    }

    public void Paint(SKCanvas canvas, float x, float y, int selectionStart = 0, int selectionLength = 0, SKColor? selectionColor = null)
    {
        ThrowIfDisposed();
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        if (selectionStart < 0 || selectionStart > Length || selectionLength < 0 || selectionLength > Length - selectionStart)
            throw new ArgumentOutOfRangeException(nameof(selectionLength));
        var options = new TextPaintOptions
        {
            Selection = selectionLength == 0 ? null : new TextRange(GetScalarIndex(selectionStart), GetScalarIndex(selectionStart + selectionLength)),
            SelectionColor = selectionColor ?? new SKColor(51, 119, 207, 100)
        };
        _block.Paint(canvas, new SKPoint(x, y), options);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TextLineLayout));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _block.Clear();
    }
}
