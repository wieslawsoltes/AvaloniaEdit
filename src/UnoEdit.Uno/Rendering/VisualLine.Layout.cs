using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Rendering.Skia;

namespace UnoEdit.Rendering;

public sealed partial class VisualLine : ITextLineLayout, IProjectedTextLineLayout
{
    private NativeTextLayout _layout;
    public IReadOnlyList<ProjectedSourceSegment> SourceSegments { get; private set; } = Array.Empty<ProjectedSourceSegment>();
    public int DocumentLength => LastDocumentLine.EndOffset - StartOffset;
    int ITextLineLayout.Length => DocumentLength;
    float ITextLineLayout.Width => _layout?.Layout.Width ?? 0;
    float ITextLineLayout.Height => (float)Height;
    internal NativeTextLayout NativeLayout => _layout;

    internal void Format(ITextRunConstructionContext context, TextViewStyle style, float? wrapWidth)
    {
        var runs = new List<TextRun>();
        foreach (var element in _elements)
        {
            var column = element.VisualColumn;
            while (column < element.VisualColumn + element.VisualLength)
            {
                var run = element.CreateTextRun(column, context);
                if (run == null || run.Length < 1 || run.Length > element.VisualColumn + element.VisualLength - column)
                    throw new InvalidOperationException($"{element.GetType().Name} returned an invalid text-run length.");
                if (run is InlineObjectRun inline)
                {
                    inline.VisualLine = this;
                    inline.Element.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    inline.DesiredSize = inline.Element.DesiredSize;
                    HasInlineObjects = true;
                }
                runs.Add(run); column += run.Length;
            }
        }
        SourceSegments = Array.AsReadOnly(_elements.Where(e => e.DocumentLength > 0).Select(e => new ProjectedSourceSegment(e.RelativeTextOffset, e.DocumentLength, !e.CanSplit)).ToArray());
        _layout = new NativeTextLayout(runs, style, wrapWidth, (float)TextView.DefaultLineHeight);
        SetTextLines(_layout.Lines.ToList());
    }
    int ITextLineLayout.HitTest(float x, float y) => GetRelativeOffset(_layout.HitTest(x, y));
    SKRect ITextLineLayout.GetCaretRectangle(int offset) => _layout.GetCaretRectangle(GetVisualColumn(offset));
    IReadOnlyList<SKRect> ITextLineLayout.GetRangeRectangles(int offset, int length)
    {
        var start = GetVisualColumn(offset); var end = GetVisualColumn(offset + length);
        // A range entirely inside a hidden/atomic element still highlights its visible representation.
        if (length > 0 && start == end)
        {
            var element = _elements.FirstOrDefault(e => e.RelativeTextOffset <= offset && e.RelativeTextOffset + e.DocumentLength > offset);
            if (element != null) { start = element.VisualColumn; end = start + element.VisualLength; }
        }
        return _layout.GetRangeRectangles(start, Math.Max(0, end - start));
    }
    void ITextLineLayout.Paint(SKCanvas canvas, float x, float y, int selectionStart, int selectionLength, SKColor? selectionColor)
    {
        VisualTop = TextView.Viewport.GetVisualTop(FirstDocumentLine);
        var start = GetVisualColumn(selectionStart); var end = GetVisualColumn(selectionStart + selectionLength);
        _layout.Paint(canvas, x, y, start, Math.Max(0, end - start), selectionColor);
    }
}
