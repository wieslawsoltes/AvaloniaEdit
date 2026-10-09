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
/// Shapes one document line with font fallback, bidi processing and grapheme
/// hit testing supplied by RichTextKit/HarfBuzz. The editor-facing contract
/// always uses UTF-16 offsets; the shaping backend uses Unicode scalar indexes.
/// </summary>
public sealed class TextLineLayout : IDisposable
{
    private readonly TextBlock _block;
    private readonly int[] _utf16ToScalar;
    private readonly int[] _scalarToUtf16;
    private bool _disposed;

    public TextLineLayout(string text, TextViewStyle style, float? wrapWidth = null)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (style == null) throw new ArgumentNullException(nameof(style));
        if (!float.IsFinite(style.FontSize) || style.FontSize <= 0) throw new ArgumentOutOfRangeException(nameof(style));
        if (style.TabSize < 1 || style.TabSize > 256) throw new ArgumentOutOfRangeException(nameof(style));
        if (wrapWidth.HasValue && (!float.IsFinite(wrapWidth.Value) || wrapWidth <= 0)) throw new ArgumentOutOfRangeException(nameof(wrapWidth));
        Length = text.Length;
        _utf16ToScalar = new int[text.Length + 1];
        var reverse = new List<int>(text.Length + 1) { 0 };
        var expanded = new StringBuilder(text.Length);
        var scalar = 0;
        var column = 0;
        for (var i = 0; i < text.Length;)
        {
            _utf16ToScalar[i] = scalar;
            if (text[i] == '\t')
            {
                var spaces = style.TabSize - column % style.TabSize;
                expanded.Append(' ', spaces);
                for (var s = 1; s <= spaces; s++)
                    reverse.Add(s * 2 < spaces ? i : i + 1);
                scalar += spaces;
                column += spaces;
                i++;
            }
            else
            {
                var length = i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]) ? 2 : 1;
                expanded.Append(text, i, length);
                if (length == 2) _utf16ToScalar[i + 1] = scalar;
                scalar++;
                column++;
                i += length;
                reverse.Add(i);
            }
        }
        _utf16ToScalar[text.Length] = scalar;
        _scalarToUtf16 = reverse.ToArray();
        _block = new TextBlock { MaxWidth = wrapWidth, EllipsisEnabled = false };
        _block.AddText(expanded.Length == 0 ? "\n" : expanded.ToString(), new Style
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            TextColor = style.Foreground
        });
        _block.Layout();
        Width = _block.MeasuredWidth;
        Height = Math.Max(style.FontSize, _block.MeasuredHeight);
    }

    public int Length { get; }
    public float Width { get; }
    public float Height { get; }

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
