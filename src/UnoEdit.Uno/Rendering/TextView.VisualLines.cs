using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Rendering.Skia;
using UnoEdit.Utils;
using Windows.Foundation;
using Windows.UI.Text;

namespace UnoEdit.Rendering;

public partial class TextView : ITextEditorComponent
{
    private LinePipeline _pipeline;
    private ObserveAddRemoveCollection<VisualLineElementGenerator> _generators;
    private ObserveAddRemoveCollection<IVisualLineTransformer> _transformers;
    private readonly Canvas _inlineLayer = new();
    private readonly Dictionary<FrameworkElement, VisualLine> _inlineOwners = new();
    private bool _visualLinesValid;
    public IList<VisualLineElementGenerator> ElementGenerators => _generators;
    public IList<IVisualLineTransformer> LineTransformers => _transformers;
    public bool VisualLinesValid => _visualLinesValid;
    public IReadOnlyList<VisualLine> VisualLines { get { EnsureVisualLines(); return VisibleLines(); } }
    public TextEditorOptions Options { get => Session.Options; set { Session.Options = value ?? new TextEditorOptions(); Redraw(); OptionChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } }
    public event EventHandler<DocumentChangedEventArgs> DocumentChanged;
    public event PropertyChangedEventHandler OptionChanged;
    public event EventHandler<VisualLineConstructionStartEventArgs> VisualLineConstructionStarting;
    public FontFamily FontFamily { get => new(Viewport.Style.FontFamily); set => Viewport.Style = Viewport.Style with { FontFamily = value?.Source ?? "monospace" }; }
    public double FontSize { get => Viewport.Style.FontSize; set => Viewport.Style = Viewport.Style with { FontSize = (float)value }; }
    private string _metricFamily;
    private float _metricSize;
    private double _spaceWidth, _baseline, _textHeight;
    private void EnsureFontMetrics()
    {
        var style = Viewport.Style;
        if (_metricFamily == style.FontFamily && _metricSize == style.FontSize) return;
        using var space = new TextLineLayout(" ", style);
        using var sample = new TextLineLayout("Mg", style);
        _spaceWidth = Math.Max(1, space.Width); _baseline = sample.Rows[0].Baseline; _textHeight = sample.Height;
        _metricFamily = style.FontFamily; _metricSize = style.FontSize;
    }
    public double WideSpaceWidth { get { EnsureFontMetrics(); return _spaceWidth; } }
    public double DefaultBaseline { get { EnsureFontMetrics(); return _baseline; } }
    public double DefaultTextHeight { get { EnsureFontMetrics(); return _textHeight; } }
    public Point ScrollOffset => new(HorizontalOffset, VerticalOffset);
    public Rect Bounds => new(0, 0, ActualWidth, ActualHeight);
    public Brush NonPrintableCharacterBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    public bool CanHorizontallyScroll { get => !Viewport.Style.WordWrap; set => Viewport.Style = Viewport.Style with { WordWrap = !value }; }
    public bool CanVerticallyScroll { get; set; } = true;
    public IServiceContainer Services { get; } = new NativeServiceContainer();
    public object GetService(Type type) => type == typeof(TextView) ? this : type == typeof(TextDocument) ? Document : Services.GetService(type);

    private void InitializeVisualLines()
    {
        _generators = new ObserveAddRemoveCollection<VisualLineElementGenerator>(Added, Removed);
        _transformers = new ObserveAddRemoveCollection<IVisualLineTransformer>(Added, Removed);
        RebindVisualLines(null);
        PointerPressed += OnVisualElementPointerPressed;
        PointerReleased += OnVisualElementPointerReleased;
        PointerMoved += OnVisualElementPointerMoved;
        void Added(object item) { if (item == null) throw new ArgumentNullException(nameof(item)); if (item is ITextViewConnect c) c.AddToTextView(this); _pipeline?.InvalidateAll(); }
        void Removed(object item) { if (item is ITextViewConnect c) c.RemoveFromTextView(this); _pipeline?.InvalidateAll(); }
    }
    private void RebindVisualLines(TextDocument oldDocument)
    {
        _pipeline?.Dispose();
        _pipeline = new LinePipeline(this, Document);
        Viewport.LineLayoutSource = _pipeline;
        _visualLinesValid = false;
        if (oldDocument != null) DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(oldDocument, Document));
    }
    public VisualLine GetVisualLine(int lineNumber) => Viewport.FindCachedLineLayout(lineNumber) as VisualLine;
    public VisualLine GetOrConstructVisualLine(DocumentLine line) => (VisualLine)Viewport.GetLineLayout(line);
    public void EnsureVisualLines()
    {
        if (_disposed) return;
        _ = VisibleLines(); _visualLinesValid = true;
    }
    private VisualLine[] VisibleLines()
    {
        var result = new List<VisualLine>();
        foreach (var line in Viewport.GetVisibleDocumentLines())
        {
            var visual = GetOrConstructVisualLine(line);
            visual.VisualTop = Viewport.GetVisualTop(visual.FirstDocumentLine);
            if (!result.Contains(visual)) result.Add(visual);
        }
        return result.ToArray();
    }
    public void Redraw(VisualLine line)
    { if (line != null && !line.FirstDocumentLine.IsDeleted) Redraw(line.FirstDocumentLine.Offset, line.DocumentLength); }
    public void Redraw(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > Document.TextLength || length > Document.TextLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        _pipeline?.InvalidateRange(offset, length);
    }
    public void Redraw(ISegment segment) { if (segment == null) throw new ArgumentNullException(nameof(segment)); Redraw(segment.Offset, segment.Length); }
    public void InvalidateVisual() => Invalidate();
    public double GetVisualTopByDocumentLine(int lineNumber) => Viewport.GetVisualTop(Document.GetLineByNumber(lineNumber));
    public DocumentLine GetDocumentLineByVisualTop(double position) => Viewport.GetLineByVisualPosition(position);
    public Point GetVisualPosition(TextViewPosition position, VisualYPosition mode)
    {
        var line = GetOrConstructVisualLine(Document.GetLineByNumber(position.Line));
        line.VisualTop = Viewport.GetVisualTop(line.FirstDocumentLine);
        return line.GetVisualPosition(line.ValidateVisualColumn(position, Options.EnableVirtualSpace), position.IsAtEndOfLine, mode);
    }
    public TextViewPosition? GetPosition(Point visualPosition) => GetPosition(visualPosition, false);
    public TextViewPosition? GetPositionFloor(Point visualPosition) => GetPosition(visualPosition, true);
    private TextViewPosition? GetPosition(Point point, bool floor)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return null;
        var line = GetOrConstructVisualLine(Viewport.GetLineByVisualPosition(point.Y));
        line.VisualTop = Viewport.GetVisualTop(line.FirstDocumentLine);
        return floor ? line.GetTextViewPositionFloor(point, Options.EnableVirtualSpace) : line.GetTextViewPosition(point, Options.EnableVirtualSpace);
    }
    public void MakeVisible(Rect rectangle)
    {
        var x = HorizontalOffset; var y = VerticalOffset;
        if (rectangle.Left < x) x = rectangle.Left; else if (rectangle.Right > x + ActualWidth - Viewport.GutterWidth) x = rectangle.Right - ActualWidth + Viewport.GutterWidth;
        if (rectangle.Top < y) y = rectangle.Top; else if (rectangle.Bottom > y + ActualHeight) y = rectangle.Bottom - ActualHeight;
        Viewport.ScrollTo(x, y);
    }
    public CollapsedLineSection CollapseLines(DocumentLine start, DocumentLine end) => Viewport.CollapseLines(start, end);
    private VisualLineElement ElementAt(PointerRoutedEventArgs e) => HitTestVisualElement(e.GetCurrentPoint(this).Position);

    // Element targeting must use the containing cell, not the nearest caret.
    // A folding placeholder occupies one visual column regardless of its width;
    // nearest-caret rounding makes its entire right half target the next element.
    internal VisualLineElement HitTestVisualElement(Point point)
    {
        if (point.X < Viewport.GutterWidth || point.Y < 0 || point.Y >= ActualHeight) return null;
        var line = GetOrConstructVisualLine(Viewport.GetLineByVisualPosition(point.Y + VerticalOffset));
        line.VisualTop = Viewport.GetVisualTop(line.FirstDocumentLine);
        var column = line.GetVisualColumnFloor(new Point(point.X + HorizontalOffset - Viewport.GutterWidth, point.Y + VerticalOffset), false);
        return line.Elements.FirstOrDefault(v => v.VisualColumn <= column && column < v.VisualColumn + v.VisualLength);
    }
    private void OnVisualElementPointerPressed(object sender, PointerRoutedEventArgs e) { if (!_disposed) ElementAt(e)?.OnPointerPressed(e); }
    private void OnVisualElementPointerReleased(object sender, PointerRoutedEventArgs e) { if (!_disposed) ElementAt(e)?.OnPointerReleased(e); }
    private void OnVisualElementPointerMoved(object sender, PointerRoutedEventArgs e) { if (!_disposed) ElementAt(e)?.OnQueryCursor(e); }

    private void ArrangeInlineObjects()
    {
        var visible = new HashSet<FrameworkElement>();
        _inlineLayer.Clip = new RectangleGeometry { Rect = new Rect(Viewport.GutterWidth, 0, Math.Max(0, ActualWidth - Viewport.GutterWidth), ActualHeight) };
        foreach (var line in VisibleLines())
        {
            if (!line.HasInlineObjects) continue;
            foreach (var placement in line.NativeLayout.Objects)
            {
                if (placement.Run is not InlineObjectRun run) continue;
                var element = run.Element;
                if (!visible.Add(element)) throw new InvalidOperationException("The same inline control cannot appear at two positions in one viewport.");
                var box = line.NativeLayout.GetObjectRectangle(placement);
                box.X += Viewport.GutterWidth - HorizontalOffset;
                box.Y += Viewport.GetVisualTop(line.FirstDocumentLine) - VerticalOffset;
                if (!_inlineOwners.ContainsKey(element))
                {
                    if (VisualTreeHelper.GetParent(element) != null) throw new InvalidOperationException("An inline control already belongs to another visual parent.");
                    _inlineOwners.Add(element, line); _inlineLayer.Children.Add(element);
                }
                _inlineOwners[element] = line;
                Canvas.SetLeft(element, box.X); Canvas.SetTop(element, box.Y);
                element.Arrange(box);
            }
        }
        foreach (var element in _inlineOwners.Keys.Where(e => !visible.Contains(e)).ToArray())
        { _inlineLayer.Children.Remove(element); _inlineOwners.Remove(element); }
    }
    internal void ReleaseInlineObjects(VisualLine line)
    {
        foreach (var element in _inlineOwners.Where(p => ReferenceEquals(p.Value, line)).Select(p => p.Key).ToArray())
        { _inlineLayer.Children.Remove(element); _inlineOwners.Remove(element); }
    }
    private void DisposeVisualLines()
    {
        _generators.Clear(); _transformers.Clear();
        _pipeline?.Dispose(); _pipeline = null;
        _inlineOwners.Clear(); _inlineLayer.Children.Clear();
        PointerPressed -= OnVisualElementPointerPressed; PointerReleased -= OnVisualElementPointerReleased; PointerMoved -= OnVisualElementPointerMoved;
        DocumentChanged = null; OptionChanged = null; VisualLineConstructionStarting = null;
    }

    private sealed class LineProjection : TextSegment
    {
        internal DocumentLine First, Last;
        internal CollapsedLineSection Collapse;
    }
    private sealed class LinePipeline : IDocumentLineLayoutSource, IDisposable
    {
        private readonly TextView _view;
        private readonly TextSegmentCollection<LineProjection> _projections;
        private bool _constructing, _disposed;
        internal LinePipeline(TextView view, TextDocument document)
        {
            _view = view; Document = document;
            _projections = new TextSegmentCollection<LineProjection>(document);
            document.Changing += OnChanging;
            view.Viewport.HeightIndexReset += OnHeightReset;
        }
        public TextDocument Document { get; }
        public event EventHandler<LineStylesChangedEventArgs> LayoutsChanged;
        public DocumentLine ResolveFirstLine(DocumentLine line)
        {
            var projection = _projections.FindSegmentsContaining(line.Offset).FirstOrDefault(p => !p.First.IsDeleted && !p.Last.IsDeleted && p.First.LineNumber < line.LineNumber && p.Last.LineNumber >= line.LineNumber);
            return projection?.First ?? line;
        }
        public DocumentLine GetLastLine(DocumentLine firstLine, ITextLineLayout layout) => ((VisualLine)layout).LastDocumentLine;
        public ITextLineLayout CreateLayout(DocumentLine firstLine, TextViewStyle style, float? wrapWidth, ILineStyleSource styles)
        {
            if (_constructing) throw new InvalidOperationException("Recursive visual-line construction is not allowed.");
            _constructing = true;
            var version = Document.Version;
            var line = new VisualLine(_view, firstLine);
            try
            {
                var args = new VisualLineConstructionStartEventArgs(firstLine, GlobalTextRunProperties.Create(style));
                _view.VisualLineConstructionStarting?.Invoke(_view, args);
                var context = new VisualLineContext(line, args.GlobalTextRunProperties);
                line.ConstructVisualElements(context, _view._generators.ToArray());
                var transformers = new List<IVisualLineTransformer>();
                if (styles != null) transformers.Add(new SourceColorizer(styles));
                transformers.AddRange(_view._transformers.ToArray());
                line.RunTransformers(context, transformers);
                if (Document.Version.CompareAge(version) != 0) throw new InvalidOperationException("A generator or transformer changed the document during visual-line construction.");
                line.Format(context, style, wrapWidth);
                foreach (var old in _projections.FindSegmentsContaining(firstLine.Offset).Where(p => ReferenceEquals(p.First, firstLine)).ToArray()) RemoveProjection(old);
                if (line.LastDocumentLine != firstLine)
                {
                    var projection = new LineProjection { StartOffset = firstLine.Offset, Length = line.DocumentLength, First = firstLine, Last = line.LastDocumentLine };
                    projection.Collapse = _view.Viewport.CollapseLines(firstLine.NextLine, line.LastDocumentLine);
                    _projections.Add(projection);
                }
                line.VisualTop = _view.Viewport.GetVisualTop(firstLine);
                return line;
            }
            catch { line.Dispose(); throw; }
            finally { _constructing = false; }
        }
        internal void InvalidateAll()
        {
            if (_disposed) return;
            foreach (var p in _projections.ToArray()) RemoveProjection(p);
            LayoutsChanged?.Invoke(this, new LineStylesChangedEventArgs(1, Document.LineCount));
        }
        internal void InvalidateRange(int offset, int length)
        {
            if (_disposed) return;
            var first = Document.GetLineByOffset(offset).LineNumber;
            var last = Document.GetLineByOffset(offset + length).LineNumber;
            foreach (var p in _projections.FindOverlappingSegments(offset, length).ToArray())
            {
                first = Math.Min(first, p.First.LineNumber); last = Math.Max(last, p.Last.LineNumber);
                RemoveProjection(p);
            }
            LayoutsChanged?.Invoke(this, new LineStylesChangedEventArgs(first, last));
        }
        private void OnChanging(object sender, DocumentChangeEventArgs e)
        {
            foreach (var p in _projections.FindOverlappingSegments(e.Offset, e.RemovalLength).ToArray()) RemoveProjection(p);
        }
        private void OnHeightReset(object sender, EventArgs e)
        {
            foreach (var p in _projections.ToArray())
            {
                p.Collapse?.Uncollapse(); p.Collapse = null;
                if (!p.First.IsDeleted && !p.Last.IsDeleted && p.Last.LineNumber > p.First.LineNumber)
                    p.Collapse = _view.Viewport.CollapseLines(p.First.NextLine, p.Last);
            }
        }
        private void RemoveProjection(LineProjection p) { p.Collapse?.Uncollapse(); _projections.Remove(p); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Document.Changing -= OnChanging; _view.Viewport.HeightIndexReset -= OnHeightReset;
            foreach (var p in _projections.ToArray()) RemoveProjection(p);
            LayoutsChanged = null;
        }
    }
    private sealed class SourceColorizer : DocumentColorizingTransformer
    {
        private readonly ILineStyleSource _source;
        internal SourceColorizer(ILineStyleSource source) => _source = source;
        protected override void ColorizeLine(DocumentLine line)
        {
            foreach (var span in _source.GetStyles(line))
                ChangeLinePart(line.Offset + span.Start, line.Offset + span.Start + span.Length, e =>
                {
                    var p = e.TextRunProperties;
                    if (span.Foreground.HasValue) p.SetForegroundBrush(NativeRunProperties.Brush(span.Foreground.Value));
                    if (span.Background.HasValue) p.SetBackgroundBrush(NativeRunProperties.Brush(span.Background.Value));
                    if (span.FontSize.HasValue) p.SetFontRenderingEmSize(span.FontSize.Value);
                    p.SetTypeface(new Typeface(span.FontFamily != null ? new FontFamily(span.FontFamily) : p.Typeface.FontFamily,
                        span.Italic.HasValue ? span.Italic.Value ? FontStyle.Italic : FontStyle.Normal : p.Typeface.Style,
                        span.FontWeight.HasValue ? new FontWeight { Weight = (ushort)span.FontWeight.Value } : p.Typeface.Weight, p.Typeface.Stretch));
                    if (span.Underline == true) p.SetTextDecorations(TextDecorations.Underline);
                    if (span.Strikethrough == true) p.SetTextDecorations(TextDecorations.Strikethrough);
                });
        }
    }
}
public sealed class VisualLineConstructionStartEventArgs : EventArgs
{
    internal VisualLineConstructionStartEventArgs(DocumentLine line, TextRunProperties properties) { FirstDocumentLine = line; GlobalTextRunProperties = properties; }
    public DocumentLine FirstDocumentLine { get; }
    public TextRunProperties GlobalTextRunProperties { get; set; }
}
internal sealed class VisualLineContext : ITextRunConstructionContext
{
    internal VisualLineContext(VisualLine line, TextRunProperties properties) { VisualLine = line; GlobalTextRunProperties = properties; }
    public TextDocument Document => VisualLine.Document;
    public TextView TextView => VisualLine.TextView;
    public VisualLine VisualLine { get; }
    public TextRunProperties GlobalTextRunProperties { get; }
    public StringSegment GetText(int offset, int length) => new(Document.GetText(offset, length));
}
