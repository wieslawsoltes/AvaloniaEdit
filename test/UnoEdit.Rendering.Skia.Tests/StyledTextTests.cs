using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;

namespace UnoEdit.Rendering.Skia.Tests;

[TestFixture]
public sealed class StyledTextTests
{
    private static TextViewStyle Style => new() { FontFamily = "DejaVu Sans Mono", FontSize = 16 };

    [Test]
    public void StylesPaintRealForegroundPixelsAndAffectLineMetrics()
    {
        using var plain = new TextLineLayout("keyword normal", Style);
        using var styled = new TextLineLayout("keyword normal", Style, null, new[]
        {
            new TextStyleSpan(0, 7) { Foreground = SKColors.Red, FontSize = 32, FontWeight = 700, Underline = true }
        });
        Assert.That(styled.Height, Is.GreaterThan(plain.Height));
        Assert.That(styled.StyleRunCount, Is.EqualTo(2));
        using var bitmap = new SKBitmap(500, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        styled.Paint(canvas, 0, 0);
        var coloredPixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Red > 180 && color.Green < 80 && color.Blue < 80) coloredPixels++;
            }
        Assert.That(coloredPixels, Is.GreaterThan(20), "Syntax colors must be painted, not merely stored in metadata.");
    }

    [Test]
    public void SyntaxBoundariesPreserveScalarAndTabMappings()
    {
        using var plain = new TextLineLayout("A\t🙂 class", Style);
        using var styled = new TextLineLayout("A\t🙂 class", Style, null, new[]
        {
            new TextStyleSpan(0, 2) { Foreground = SKColors.Blue },
            new TextStyleSpan(2, 2) { Foreground = SKColors.Red },
            new TextStyleSpan(5, 5) { Italic = true }
        });
        for (var i = 0; i <= plain.Length; i++)
            Assert.That(styled.GetScalarIndex(i), Is.EqualTo(plain.GetScalarIndex(i)));
        for (var i = 0; i <= plain.GetScalarIndex(plain.Length); i++)
            Assert.That(styled.GetUtf16Offset(i), Is.EqualTo(plain.GetUtf16Offset(i)));
    }

    [Test]
    public void InvalidStyleRangesAreRejectedBeforeShaping()
    {
        Assert.Throws<ArgumentException>(() => new TextLineLayout("🙂x", Style, null, new[] { new TextStyleSpan(1, 1) }));
        Assert.Throws<ArgumentException>(() => new TextLineLayout("text", Style, null, new[] { new TextStyleSpan(0, 3), new TextStyleSpan(2, 2) }));
        Assert.Throws<ArgumentException>(() => new TextLineLayout("text", Style, null, new[] { new TextStyleSpan(0, 5) }));
        Assert.Throws<ArgumentException>(() => new TextLineLayout("text", Style, null, new[] { new TextStyleSpan(0, 1) { FontSize = float.NaN } }));
    }

    [Test]
    public void CachedStyledLinesAreReusedAndOnlySignaledLinesAreRebuilt()
    {
        var document = new TextDocument(string.Join("\n", Enumerable.Repeat("keyword", 1000)));
        var source = new Source(document);
        using var viewport = new DocumentViewport(document) { Style = Style, LineStyleSource = source };
        using var surface = SKSurface.Create(new SKImageInfo(640, 480));
        viewport.SetViewport(640, 480);
        viewport.Render(surface.Canvas);
        var calls = source.Calls;
        var layouts = viewport.LayoutCreationCount;
        viewport.Render(surface.Canvas);
        Assert.That(source.Calls, Is.EqualTo(calls));
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(layouts));
        source.Invalidate(4, 4);
        viewport.Render(surface.Canvas);
        Assert.That(source.Calls, Is.EqualTo(calls + 1));
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(layouts + 1));
        source.Invalidate(900, 1000);
        viewport.Render(surface.Canvas);
        Assert.That(source.Calls, Is.EqualTo(calls + 1), "Offscreen style invalidation must not shape offscreen lines.");
    }

    [Test]
    public void SourceDocumentMustMatchAndReplacementDetachesItsEvents()
    {
        var original = new TextDocument("keyword");
        var source = new Source(original);
        using var viewport = new DocumentViewport(original) { LineStyleSource = source };
        Assert.That(source.Subscribers, Is.EqualTo(1));
        Assert.Throws<ArgumentException>(() => viewport.LineStyleSource = new Source(new TextDocument("foreign")));
        viewport.Document = new TextDocument("replacement");
        Assert.That(viewport.LineStyleSource, Is.Null);
        Assert.That(source.Subscribers, Is.Zero);
        source.Invalidate(1, 1);
        var replacement = new Source(viewport.Document);
        viewport.LineStyleSource = replacement;
        viewport.Dispose();
        Assert.That(replacement.Subscribers, Is.Zero);
    }

    [Test]
    public void ChangingStyledFontSizeRecomputesMeasuredHeight()
    {
        var document = new TextDocument("keyword\nnext");
        var source = new Source(document) { FontSize = 48 };
        using var viewport = new DocumentViewport(document) { Style = Style, LineStyleSource = source };
        viewport.SetViewport(640, 480);
        var caret = viewport.GetCaretRectangle(1);
        Assert.That(caret.Height, Is.GreaterThan(30));
        var tall = viewport.ExtentHeight;
        source.FontSize = 16;
        source.Invalidate(1, 1);
        viewport.GetCaretRectangle(1);
        Assert.That(viewport.ExtentHeight, Is.LessThan(tall));
    }

    private sealed class Source : ILineStyleSource
    {
        private EventHandler<LineStylesChangedEventArgs> _changed;
        internal Source(TextDocument document) => Document = document;
        public TextDocument Document { get; }
        internal int Calls { get; private set; }
        internal int Subscribers { get; private set; }
        internal float FontSize { get; set; } = 16;
        public event EventHandler<LineStylesChangedEventArgs> StylesChanged
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }
        public IReadOnlyList<TextStyleSpan> GetStyles(DocumentLine line)
        {
            Calls++;
            return new[] { new TextStyleSpan(0, line.Length) { Foreground = SKColors.Red, FontSize = FontSize } };
        }
        internal void Invalidate(int first, int last) => _changed?.Invoke(this, new LineStylesChangedEventArgs(first, last));
    }
}
