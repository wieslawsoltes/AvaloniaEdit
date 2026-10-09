using System;
using System.Collections.Generic;
using SkiaSharp;
using Topten.RichTextKit;

namespace UnoEdit.Rendering.Skia;

/// <summary>Actual shaped-row metrics in UTF-16 input coordinates.</summary>
public sealed record ShapedTextRow(int Start, int Length, float Top, float Height, float Baseline, float Width)
{
    public int End => Start + Length;
}

public sealed partial class TextLineLayout
{
    private ShapedTextRow[] _rows;
    private readonly Dictionary<Topten.RichTextKit.TextLine, ShapedTextRow> _rowMap = new();
    private bool _adjustedRows;
    private readonly Dictionary<FontRun, SKTextBlob> _runBlobs = new();
    public IReadOnlyList<ShapedTextRow> Rows { get { ThrowIfDisposed(); return _rows; } }

    private void InitializeRows(IReadOnlyList<TextStyleSpan> spans, float minimumHeight)
    {
        if (!float.IsFinite(minimumHeight) || minimumHeight < 0) throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        var lines = _block.Lines;
        _rows = new ShapedTextRow[lines.Count];
        float y = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var from = _scalarToUtf16[Math.Clamp(line.Start, 0, _scalarToUtf16.Length - 1)];
            var to = _scalarToUtf16[Math.Clamp(line.End, 0, _scalarToUtf16.Length - 1)];
            float ascent = line.BaseLine, descent = Math.Max(0, line.Height - line.BaseLine);
            if (spans != null)
                foreach (var span in spans)
                    if (span.Start >= from && span.Start < to && span.ObjectWidth.HasValue)
                    {
                        ascent = Math.Max(ascent, span.ObjectBaseline.Value);
                        descent = Math.Max(descent, span.ObjectHeight.Value - span.ObjectBaseline.Value);
                    }
            var height = Math.Max(minimumHeight, ascent + descent);
            var baseline = ascent + (height - ascent - descent) / 2;
            var row = new ShapedTextRow(from, to - from, y, height, baseline, line.Width);
            _rows[i] = row; _rowMap.Add(line, row);
            _adjustedRows |= Math.Abs(row.Top - line.YCoord) > 0.001f || Math.Abs(row.Baseline - line.BaseLine) > 0.001f || Math.Abs(row.Height - line.Height) > 0.001f;
            y += height;
        }
        Height = Math.Max(Height, y);
    }

    private ShapedTextRow RowFor(Topten.RichTextKit.TextLine line) => _rowMap[line];
    public ShapedTextRow GetRowByOffset(int offset, bool endOfPreviousRow = false)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        for (var i = 0; i < _rows.Length; i++)
            if (offset < _rows[i].End || endOfPreviousRow && offset == _rows[i].End || i == _rows.Length - 1) return _rows[i];
        throw new InvalidOperationException("The shaped line has no rows.");
    }
    public ShapedTextRow GetRowByY(float y)
    {
        ThrowIfDisposed();
        foreach (var row in _rows) if (y < row.Top + row.Height) return row;
        return _rows[^1];
    }
    private float ToOriginalY(float y)
    {
        for (var i = 0; i < _rows.Length; i++)
            if (y < _rows[i].Top + _rows[i].Height || i == _rows.Length - 1)
                return _block.Lines[i].YCoord + Math.Clamp(y - _rows[i].Top, 0, Math.Max(0, _block.Lines[i].Height - 0.001f));
        return y;
    }
    public float GetDistanceAtOffset(int offset, ShapedTextRow row)
    {
        ThrowIfDisposed();
        var scalar = GetScalarIndex(Math.Clamp(offset, row.Start, row.End));
        foreach (var run in _block.FontRuns)
            if (ReferenceEquals(RowFor(run.Line), row) && scalar >= run.Start && scalar <= run.End) return run.GetXCoordOfCodePointIndex(scalar);
        return row.Width;
    }

    /// <summary>Paint one row without reshaping or visiting unrelated wrapped rows.</summary>
    public void PaintRow(SKCanvas canvas, ShapedTextRow row, float x, float y)
    {
        ThrowIfDisposed();
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        if (Array.IndexOf(_rows, row) < 0) throw new ArgumentException("Row belongs to another layout.", nameof(row));
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var run in _block.FontRuns)
        {
            if (!ReferenceEquals(RowFor(run.Line), row)) continue;
            PaintRun(canvas, run, row, x, y - row.Top, paint);
        }
    }
    private void PaintRows(SKCanvas canvas, float x, float y, int start, int length, SKColor selection)
    {
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var run in _block.FontRuns)
        {
            var row = RowFor(run.Line);
            if (run.Style.BackgroundColor.Alpha != 0)
            {
                paint.Color = run.Style.BackgroundColor;
                canvas.DrawRect(x + run.XCoord, y + row.Top, run.Width, row.Height, paint);
            }
        }
        if (length > 0)
        {
            paint.Color = selection;
            foreach (var box in GetRangeRectangles(start, length)) { var b = box; b.Offset(x, y); canvas.DrawRect(b, paint); }
        }
        foreach (var run in _block.FontRuns) PaintRun(canvas, run, RowFor(run.Line), x, y, paint);
    }
    private void PaintRun(SKCanvas canvas, FontRun run, ShapedTextRow row, float x, float y, SKPaint paint)
    {
        if (run.Glyphs.Length == 0 || run.Style.TextColor.Alpha == 0) return;
        if (!_runBlobs.TryGetValue(run, out var blob))
        {
            using var font = new SKFont(run.Typeface, run.Style.FontSize);
            using var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun(run.Glyphs.AsSpan(), font, run.GlyphPositions.AsSpan());
            blob = builder.Build(); _runBlobs.Add(run, blob);
        }
        paint.Color = run.Style.TextColor;
        var dy = row.Top + row.Baseline - run.Line.YCoord - run.Line.BaseLine;
        canvas.DrawText(blob, x, y + dy, paint);
        paint.StrokeWidth = Math.Max(1, run.Style.FontSize / 14);
        if (run.Style.Underline != UnderlineStyle.None)
            canvas.DrawLine(x + run.XCoord, y + row.Top + row.Baseline + Math.Max(1, run.Descent / 2), x + run.XCoord + run.Width, y + row.Top + row.Baseline + Math.Max(1, run.Descent / 2), paint);
        if (run.Style.StrikeThrough != StrikeThroughStyle.None)
            canvas.DrawLine(x + run.XCoord, y + row.Top + row.Baseline + run.Ascent / 3, x + run.XCoord + run.Width, y + row.Top + row.Baseline + run.Ascent / 3, paint);
    }
    private void DisposeRows()
    {
        foreach (var blob in _runBlobs.Values) blob?.Dispose();
        _runBlobs.Clear(); _rowMap.Clear(); _rows = Array.Empty<ShapedTextRow>();
    }
}
