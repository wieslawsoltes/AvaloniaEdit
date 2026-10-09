using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Rendering;
using UnoEdit.Rendering.Skia;
using Windows.Foundation;

namespace UnoEdit.Uno.Tests;

[TestFixture]
public sealed class VisualPipelineTests
{
    private TextEditor _editor;
    [SetUp] public void Setup()
    {
        _editor = new TextEditor { Width = 600, Height = 300, ShowLineNumbers = true };
        TestApp.Root.Children.Add(_editor);
        _editor.Measure(new Size(600, 300)); _editor.Arrange(new Rect(0, 0, 600, 300));
        _editor.TextArea.TextView.Viewport.SetViewport(580, 280);
    }
    [TearDown] public void Cleanup() { TestApp.Root.Children.Remove(_editor); _editor.Dispose(); }
    [Test] public void PlainVisualLinePreservesAllDocumentAndVisualCoordinates()
    {
        _editor.Text = "abc\t🙂 e\u0301";
        var view = _editor.TextArea.TextView; var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(line.VisualLength, Is.EqualTo(_editor.Document.TextLength));
        for (var i = 0; i <= _editor.Document.TextLength; i++) { Assert.That(line.GetVisualColumn(i), Is.EqualTo(i)); Assert.That(line.GetRelativeOffset(i), Is.EqualTo(i)); }
        Assert.That(line.Elements.Single(), Is.TypeOf<VisualLineText>());
        Assert.That(line.TextLines.Count, Is.EqualTo(1));
        Assert.That(line.Height, Is.GreaterThanOrEqualTo(view.DefaultLineHeight));
    }
    [Test] public void OriginalDocumentColorizerSplitsAndChangesActualShapedMetrics()
    {
        _editor.Text = "abcDEFghi"; var view = _editor.TextArea.TextView;
        var normal = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1)).Height;
        var transformer = new Colorizer(); view.LineTransformers.Add(transformer);
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(line.Elements.Count, Is.EqualTo(3));
        Assert.That(line.Elements[1].TextRunProperties.FontRenderingEmSize, Is.EqualTo(30));
        Assert.That(line.Height, Is.GreaterThan(normal));
        Assert.That(transformer.Added, Is.EqualTo(1)); view.LineTransformers.Remove(transformer);
        Assert.That(transformer.Removed, Is.EqualTo(1));
    }
    [Test] public void GeneratorProjectsAtomicTextAcrossSeveralDocumentLines()
    {
        _editor.Text = "abc\nhidden\nlastXYZ"; var view = _editor.TextArea.TextView;
        view.ElementGenerators.Add(new Generator(3, 12, "..."));
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(line.LastDocumentLine.LineNumber, Is.EqualTo(3));
        Assert.That(line.DocumentLength, Is.EqualTo(_editor.Document.TextLength));
        Assert.That(line.GetRelativeOffset(4), Is.EqualTo(15));
        Assert.That(view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(2)), Is.SameAs(line));
        Assert.That(view.Viewport.IsLineCollapsed(2), Is.True);
        Assert.That(line.Elements[1], Is.InstanceOf<FormattedTextElement>());
        using var surface = SKSurface.Create(new SKImageInfo(600, 300));
        view.Viewport.Render(surface.Canvas, _editor.TextArea.Session);
        Assert.That(view.Viewport.LastVisibleLineCount, Is.EqualTo(1));
    }
    [Test] public void AtomicRunReservesItsMeasuredWidthAndHeight()
    {
        _editor.Text = "abcZ"; var view = _editor.TextArea.TextView;
        view.ElementGenerators.Add(new DrawableGenerator());
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(line.Height, Is.GreaterThanOrEqualTo(50));
        var left = line.GetVisualPosition(3, VisualYPosition.LineTop).X;
        var right = line.GetVisualPosition(4, VisualYPosition.LineTop).X;
        Assert.That(right - left, Is.EqualTo(80).Within(0.1));
        using var surface = SKSurface.Create(new SKImageInfo(600, 300)); view.Viewport.Render(surface.Canvas);
        Assert.That(DrawableRun.Drawn, Is.True);
    }
    [Test] public void WrappedRowsHaveActualCaretAndHitTestGeometry()
    {
        _editor.Text = string.Join(" ", Enumerable.Repeat("word", 30)); _editor.WordWrap = true;
        var view = _editor.TextArea.TextView; view.Viewport.SetViewport(180, 280);
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(line.TextLines.Count, Is.GreaterThan(2));
        foreach (var textLine in line.TextLines)
        {
            var start = line.GetTextLineVisualStartColumn(textLine);
            var top = line.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop);
            var point = new Point(line.GetTextLineVisualXPosition(textLine, start) + 0.01, top + textLine.Height / 2);
            Assert.That(line.GetVisualColumn(point, false), Is.EqualTo(start));
        }
    }
    [Test] public void GeneratorIsFinishedAfterConstructionThrows()
    {
        _editor.Text = "abcdef"; var view = _editor.TextArea.TextView;
        var generator = new BrokenGenerator(); view.ElementGenerators.Add(generator);
        Assert.Throws<InvalidOperationException>(() => view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1)));
        Assert.That(generator.Finished, Is.EqualTo(1)); view.ElementGenerators.Remove(generator);
        Assert.That(view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1)).VisualLength, Is.EqualTo(6));
    }
    [Test] public void CacheEvictionDisposesVisualLineBuffers()
    {
        _editor.Text = string.Join("\n", Enumerable.Repeat("word", 300)); var view = _editor.TextArea.TextView;
        var first = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        for (var i = 2; i <= 300; i++) view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(i));
        Assert.That(first.IsDisposed, Is.True); Assert.That(view.Viewport.CachedLineCount, Is.EqualTo(256));
    }
    private sealed class Colorizer : DocumentColorizingTransformer
    {
        internal int Added, Removed;
        protected override void OnAddToTextView(TextView view) => Added++;
        protected override void OnRemoveFromTextView(TextView view) => Removed++;
        protected override void ColorizeLine(DocumentLine line) => ChangeLinePart(line.Offset + 3, line.Offset + 6, e => { e.TextRunProperties.SetFontRenderingEmSize(30); e.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Microsoft.UI.Colors.Red)); });
    }
    private sealed class Generator : VisualLineElementGenerator
    {
        private readonly int _at, _length; private readonly string _text;
        internal Generator(int at, int length, string text) { _at = at; _length = length; _text = text; }
        public override int GetFirstInterestedOffset(int startOffset) => startOffset <= _at ? _at : -1;
        public override VisualLineElement ConstructElement(int offset) => new FormattedTextElement(_text, _length);
    }
    private sealed class BrokenGenerator : VisualLineElementGenerator
    {
        internal int Finished;
        public override int GetFirstInterestedOffset(int startOffset) => 0;
        public override VisualLineElement ConstructElement(int offset) => throw new InvalidOperationException("test");
        public override void FinishGeneration() { Finished++; base.FinishGeneration(); }
    }
    private sealed class DrawableGenerator : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset) => startOffset <= 3 ? 3 : -1;
        public override VisualLineElement ConstructElement(int offset) => new DrawableElement();
    }
    private sealed class DrawableElement : VisualLineElement
    {
        internal DrawableElement() : base(1, 1) { }
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new DrawableRun(TextRunProperties);
    }
    private sealed class DrawableRun : DrawableTextRun
    {
        internal static bool Drawn;
        internal DrawableRun(TextRunProperties properties) { Properties = properties; Drawn = false; }
        public override TextRunProperties Properties { get; }
        public override Size Size => new(80, 50);
        public override double Baseline => 30;
        public override void Draw(DrawingContext context, Point origin) { Drawn = true; context.DrawRectangle(new SolidColorBrush(Microsoft.UI.Colors.Red), null, new Rect(origin, Size)); }
    }
}
